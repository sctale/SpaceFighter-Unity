using UnityEngine;

namespace AuroraRift
{
    /// <summary>精英词缀：装甲（更硬、吃伤更少）、急速（更小、射得更勤）、爆裂（死后延迟炸开弹幕）。</summary>
    public enum Affix { None, Armored, Rapid, Volatile }

    /// <summary>
    /// 12 种敌机统一实现：移动模式 + 开火模式 + 精英/词缀，全部由 EnemyDefs 数据驱动。
    /// 与网页版对齐的三件事：血量按星区 ×(1+(s-1)×0.16)、射击间隔按密度档与难度缩放、
    /// 以及「出场公平性」——未合法入屏的敌机不能被锁定、不能受伤、也不能撞死玩家。
    /// </summary>
    public class EnemyShip : MonoBehaviour
    {
        public EnemyKind Kind;
        public EnemyDef Def;
        public Affix Affix;
        public bool Elite;
        public float Hp, MaxHp;
        public float Radius;
        public float Score;
        public bool Dead => _dead;
        public float Age => _age;

        SpriteRenderer _sr, _flash, _ring, _armor, _exhaust;
        float _age, _fireTimer, _phase, _charge, _flashT;
        float _summonT = 3.2f;
        Vector2 _anchor;
        bool _dead;
        float _artHeight;
        float _damageTakenMul = 1f;
        float _fireMul = 1f;

        // ---- 原版运动模型的状态（entities.js:1200-1341）----
        float _sizeWorld;          // 机体尺寸（画布 px × Px），美术与炮口用它
        float _stationY;           // 悬停/横掠的驻留高度（世界 y，向上为正）
        float _dirSign = 1f;       // strafe 的横穿方向：出生在哪半边就往对面穿
        float _entryDelay = GameConfig.EntryDelay;   // 母舰/分裂的子系统入场宽限更短
        Vector2 _diveVel;          // 俯冲锁定玩家后的速度
        bool _diveLocked;
        bool _leaving;             // 到寿离场：不再开火，直着往下走
        float _bank;               // 视觉横滚（弧度），与原版 angle 同义

        public void Setup(EnemyKind kind, int zone, Difficulty diff, bool elite = false,
                          float hpFrac = 1f, float entryDelayOverride = -1f, Affix? forceAffix = null)
        {
            Kind = kind;
            Def = EnemyDefs.Get(kind);
            Elite = elite;
            _diff = diff;
            _zone = zone;
            Score = Def.Score;
            if (entryDelayOverride >= 0f) _entryDelay = entryDelayOverride;

            // 第 3 星区起普通敌机可能带词缀（精英由导演指定，两者不叠加）
            // forceAffix 只给数值门禁用来复现/屏蔽某一种词缀，游戏里不会传
            if (forceAffix.HasValue) Affix = forceAffix.Value;
            else if (zone >= 3 && !elite && kind != EnemyKind.Shardling && kind != EnemyKind.Rock)
            {
                float chance = GameConfig.AffixBaseChance +
                               Mathf.Min(GameConfig.AffixMaxExtra, (zone - 3) * 0.02f);
                if (Random.value < chance) Affix = (Affix)Random.Range(1, 4);
            }

            // 体型倍率：精英 1.22 → 装甲 1.06 / 急速 0.92（原版是顺序相乘）
            float sizeMul = 1f;
            if (Elite) sizeMul *= GameConfig.EliteSizeMul;
            if (Affix == Affix.Armored) sizeMul *= GameConfig.ArmoredSizeMul;
            if (Affix == Affix.Rapid) sizeMul *= GameConfig.RapidSizeMul;
            _sizeWorld = Def.Radius * sizeMul;
            // 判定半径只有尺寸的 0.78（entities.js:1263），美术保持全尺寸
            Radius = _sizeWorld * GameConfig.EnemyCollisionSizePx;

            // 开火修正：急速 0.72（更勤）、装甲 1.12（更慢）
            if (Affix == Affix.Rapid) _fireMul = GameConfig.RapidFireMul;
            else if (Affix == Affix.Armored) _fireMul = GameConfig.ArmoredFireMul;
            if (Affix == Affix.Armored) _damageTakenMul = GameConfig.ArmoredDamageTakenMul;

            // 原版是分两步取整的：先 round(基础×星区曲线×精英)，再 round(×1.45 / ×0.9)。
            // 合成一步会和原版本差 1 点血，压线时手感就不一样。
            float hp = Mathf.Round(Def.Hp * (1f + (zone - 1) * GameConfig.EnemyHpPerSector)
                                  * (Elite ? GameConfig.EliteHpMul : 1f));
            if (Affix == Affix.Armored) hp = Mathf.Round(hp * GameConfig.ArmoredHpMul);
            else if (Affix == Affix.Volatile) hp = Mathf.Max(1f, Mathf.Round(hp * GameConfig.VolatileHpMul));
            Hp = MaxHp = Mathf.Max(1f, Mathf.Round(hp * hpFrac));

            float score = Def.Score;
            if (Elite) score *= GameConfig.EliteScoreMul;
            if (Affix != Affix.None) score *= 1.18f;
            Score = Mathf.Round(score);

            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();

            string art = "enemy_" + kind.ToString().ToLowerInvariant();
            _artHeight = _sizeWorld / ShipArt.ArtExtent;
            var sprite = ShipArt.Get(art, _artHeight);
            if (sprite != null)
            {
                _sr.sprite = sprite;
                _sr.color = Color.white;
            }
            else
            {
                _sr.sprite = kind switch
                {
                    EnemyKind.Rock => RuntimeSprites.Rock(64),
                    EnemyKind.Orbiter or EnemyKind.Bulwark or EnemyKind.Carrier or EnemyKind.Minekeeper
                        => RuntimeSprites.Hex(64),
                    EnemyKind.Drifter or EnemyKind.Splitter => RuntimeSprites.Diamond(48),
                    _ => RuntimeSprites.Arrow(48),
                };
                _sr.color = Def.Tint;
            }
            _sr.sortingOrder = 8;
            transform.localScale = Vector3.one;

            // 小型尾焰：机体朝向 -Y，尾喷在 +Y；只做一层柔光，静止机体也有"在飞"的动势
            if (kind != EnemyKind.Rock)
            {
                var exGo = new GameObject("Exhaust");
                exGo.transform.SetParent(transform, false);
                exGo.transform.localPosition = new Vector3(0f, _sizeWorld * 0.56f, 0f);
                _exhaust = exGo.AddComponent<SpriteRenderer>();
                _exhaust.sprite = ShipArt.Get("bullet_glow_soft", 1f) ?? RuntimeSprites.Disc(32);
                _exhaust.sortingOrder = 7;
                _exhaust.transform.localScale = Vector3.one * (_sizeWorld * 0.9f);
            }

            var flashGo = new GameObject("Flash");
            flashGo.transform.SetParent(transform, false);
            _flash = flashGo.AddComponent<SpriteRenderer>();
            _flash.sprite = ShipArt.Mask(art, _artHeight);
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _flash.sortingOrder = 7;
            _flash.enabled = false;
            // 白剪影压在机体后面、略放大：受击只亮一圈边，不把装甲洗成灰白
            flashGo.transform.localScale = Vector3.one * 1.05f;

            if (Affix == Affix.Armored || Elite)
            {
                var armorGo = new GameObject("Armor");
                armorGo.transform.SetParent(transform, false);
                _armor = armorGo.AddComponent<SpriteRenderer>();
                _armor.sprite = _flash.sprite;
                _armor.color = Elite ? new Color(1f, 0.82f, 0.45f, 0.55f) : new Color(0.92f, 0.96f, 1f, 0.45f);
                _armor.sortingOrder = 7;
                _armor.enabled = _flash.sprite != null;
                armorGo.transform.localScale = Vector3.one * 1.12f;
            }

            if (kind == EnemyKind.Orbiter)
            {
                var ringSprite = ShipArt.Get("enemy_orbiter_ring", _artHeight);
                if (ringSprite != null)
                {
                    var ringGo = new GameObject("Ring");
                    ringGo.transform.SetParent(transform, false);
                    _ring = ringGo.AddComponent<SpriteRenderer>();
                    _ring.sprite = ringSprite;
                    _ring.sortingOrder = 9;
                }
            }

            _phase = Random.value * 10f;
            _anchor = transform.position;
            _stationY = GameView.FromTopFrac(Def.TargetY > 0f ? Def.TargetY : 0.24f);
            _dirSign = transform.position.x < 0f ? 1f : -1f;   // 原版：x < W/2 往右穿
            _summonT = Random.Range(2.6f, 3.6f);
            _fireTimer = Def.Fire == FireMode.None ? float.MaxValue : Random.Range(0.9f, 1.6f);
        }

        /// <summary>密度档位 + 精英 + 难度共同决定的射击间隔。</summary>
        float NextInterval(Difficulty diff, int zone)
        {
            int tier = Mathf.Min(3, (zone - 1) / 5);
            float baseCd = Random.Range(Def.FireCdMin, Def.FireCdMax);
            float tierMul = Mathf.Max(GameConfig.EnemyFireIntervalFloor, 1f - tier * GameConfig.EnemyFirePerTier);
            float eliteMul = Elite ? GameConfig.EliteFireMul : 1f;
            return baseCd * _fireMul * tierMul * eliteMul * diff.EnemyRate;
        }

        Difficulty _diff;
        int _zone;

        /// <summary>出场公平性：入屏宽限期没过、或还在屏幕外，就不该被打死、被锁定、撞伤人。</summary>
        public bool Targetable()
        {
            if (_age <= _entryDelay + GameConfig.EntryGrace) return false;
            float y = transform.position.y;
            if (y > GameView.HalfHeight + GameConfig.EntryTopInsetPx * GameConfig.Px) return false;
            if (y < GameView.EntryBottomY) return false;
            return true;
        }

        void Update()
        {
            if (_dead) return;
            var root = GameRoot.Instance;
            if (root == null || root.State != GameState.Playing) return;

            float dt = Time.deltaTime;
            _age += dt;
            Move(dt);
            Fire(dt);
            TickVisuals(dt);
        }

        void Move(float dt)
        {
            int tier = Mathf.Min(3, (_zone > 0 ? _zone - 1 : 0) / 5);
            Vector2 p = transform.position;
            float speed = Def.Speed * (1f + tier * GameConfig.EnemySpeedPerTier);
            float amp = Def.Amp;
            float turn = 0f;      // 视觉转向角（Unity Z 度）

            // 到寿离场：悬停/环绕机不会永远占着屏幕。原版 hover 走 170px/s、orbit 160px/s。
            if (_leaving)
            {
                float leavePx = Def.Move == MovePattern.Orbit ? GameConfig.OrbitLeaveSpeedPx
                                                              : GameConfig.HoverLeaveSpeedPx;
                p.y -= leavePx * GameConfig.Px * dt;
                transform.position = p;
                transform.rotation = Quaternion.Euler(0f, 0f, turn);
                if (p.y < GameView.DespawnY) Director.Instance?.Despawn(this);
                return;
            }

            switch (Def.Move)
            {
                case MovePattern.Sine:
                    p.y -= speed * dt;
                    p.x = _anchor.x + Mathf.Sin(_age * 1.5f + _phase) * amp;
                    turn = -Mathf.Sin(_age * 1.5f + _phase) * 11.5f;
                    break;

                case MovePattern.Dive:
                    if (_age > GameConfig.EntryDelay)
                    {
                        if (!_diveLocked)
                        {
                            var pl = GameRoot.Instance != null ? GameRoot.Instance.Player : null;
                            if (pl != null)
                            {
                                // 锁定玩家此刻的位置，之后 1.5 倍速直线撞过去（原版 diveTarget）
                                Vector2 t = pl.transform.position;
                                float ang = Mathf.Atan2(t.y - p.y, t.x - p.x);
                                _diveVel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang))
                                           * speed * GameConfig.DiveSpeedMul;
                                _diveLocked = true;
                            }
                        }
                        p += _diveVel * dt;
                        turn = Mathf.Atan2(_diveVel.y, _diveVel.x) * Mathf.Rad2Deg + 90f;
                    }
                    else p.y -= GameConfig.DiveSlowSpeedPx * GameConfig.Px * dt;
                    break;

                case MovePattern.Hover:
                    if (_age > GameConfig.EnemyLifeSeconds) _leaving = true;
                    else if (p.y > _stationY)
                    {
                        p.y = Mathf.Max(_stationY, p.y - speed * dt);
                        p.x = _anchor.x + Mathf.Sin(_age * 0.9f + _phase)
                              * GameConfig.ApproachWobblePx * GameConfig.Px;
                    }
                    else
                    {
                        float spanPx = Kind == EnemyKind.Bulwark ? GameConfig.HoverBulwarkSpanPx
                                                                 : GameConfig.HoverSpanPx;
                        p.x = _anchor.x + Mathf.Sin(_age * 0.7f + _phase) * spanPx * GameConfig.Px
                                          + Mathf.Sin(_age * 0.23f) * GameConfig.HoverSecondAmpPx * GameConfig.Px;
                        p.y = _stationY + Mathf.Sin(_age * 1.1f + _phase) * GameConfig.HoverBobPx * GameConfig.Px;
                        turn = -Mathf.Cos(_age * 0.7f + _phase) * 8.6f;
                    }
                    break;

                case MovePattern.Orbit:
                    if (_age > GameConfig.EnemyLifeSeconds) _leaving = true;
                    else if (p.y > _stationY)
                    {
                        p.y = Mathf.Max(_stationY, p.y - speed * dt);
                        p.x = _anchor.x + Mathf.Sin(_age * 0.9f + _phase)
                              * GameConfig.ApproachWobblePx * GameConfig.Px;
                    }
                    else
                    {
                        // 原版是扁椭圆：横向 62px、纵向 24px，角速度 1.15
                        p.x = _anchor.x + Mathf.Cos(_age * GameConfig.OrbitRate + _phase)
                              * GameConfig.OrbitRadiusPx * GameConfig.Px;
                        p.y = _stationY + Mathf.Sin(_age * GameConfig.OrbitRate + _phase)
                              * GameConfig.OrbitRadiusYPx * GameConfig.Px;
                    }
                    break;

                case MovePattern.Rock:
                    p.y -= speed * dt;
                    p.x += Mathf.Sin(_age * 0.6f + _phase) * GameConfig.RockDriftPx * GameConfig.Px * dt;
                    transform.Rotate(0f, 0f, 40f * dt);
                    break;

                case MovePattern.Strafe:
                    if (p.y > _stationY)
                    {
                        p.y = Mathf.Max(_stationY, p.y - GameConfig.StrafeDropSpeedPx * GameConfig.Px * dt);
                    }
                    else
                    {
                        p.y = _stationY + Mathf.Sin(_age * 1.8f + _phase) * GameConfig.StrafeBobPx * GameConfig.Px;
                        p.x += _dirSign * speed * dt;    // 一路横穿，出屏就消失
                        turn = _dirSign > 0f ? 90f : -90f;
                    }
                    break;
            }

            transform.position = p;
            if (Def.Move != MovePattern.Rock) transform.rotation = Quaternion.Euler(0f, 0f, turn);
            // 原版出屏判定含 x（entities.js:1382）：横穿与偏出边界的机体要真的被回收
            float marginPx = 135f * GameConfig.Px;
            if (p.y < GameView.DespawnY ||
                p.x < -GameView.HalfWidth - marginPx || p.x > GameView.HalfWidth + marginPx)
                Director.Instance?.Despawn(this);
        }

        void Fire(float dt)
        {
            var root = GameRoot.Instance;
            if (root == null || root.State != GameState.Playing) return;

            // 原版开火带：y∈(20px, H×0.8)；母舰召唤带只到 H×0.45（上半屏）
            float myY = transform.position.y;
            float topLimit = GameView.HalfHeight - 20f * GameConfig.Px;
            bool inBand = myY < topLimit && myY > -0.6f * GameView.HalfHeight;
            bool inSummonBand = myY < topLimit && myY > GameView.HalfHeight * (1f - 2f * GameConfig.CarrierBandTopFrac);

            if (Kind == EnemyKind.Carrier && !_leaving && inSummonBand)
            {
                _summonT -= dt;
                if (_summonT <= 0f)
                {
                    _summonT = GameConfig.CarrierSummonEvery;
                    var d = Director.Instance;
                    if (d != null && d.AliveEnemies < 38)
                    {
                        // 原版 55 % 分裂幼体 / 45 % 俯冲蜂：母舰放的是会撞人的快机型
                        bool shard = Random.value < GameConfig.CarrierShardlingChance;
                        float px = Mathf.Clamp(transform.position.x + Random.Range(-_sizeWorld, _sizeWorld),
                                               -GameView.HalfWidth + 30f * GameConfig.Px,
                                               GameView.HalfWidth - 30f * GameConfig.Px);
                        Vector2 at = new Vector2(px, transform.position.y - _sizeWorld * 0.6f);
                        d.SpawnChild(shard ? EnemyKind.Shardling : EnemyKind.Wasp, at, 1f,
                                     GameConfig.CarrierChildEntryDelay);
                    }
                }
            }

            if (Def.Fire == FireMode.None || _leaving) return;
            if (!inBand) return;
            var player = GameRoot.Instance?.Player;
            if (player == null || !player.Alive) return;

            if (_charge > 0f)
            {
                _charge -= dt;
                if (_charge <= 0f)
                {
                    DoFire(player);
                    _fireTimer = NextInterval(_diff, _zone);
                }
                return;
            }

            _fireTimer -= dt;
            if (_fireTimer > 0f) return;

            // 原版：蓄力期间 fireTimer 继续走，出招后才重roll间隔；
            // 之前是先 roll 间隔再蓄力，等于把蓄力时间从节奏里抹掉了。
            if (Def.Charge > 0f)
            {
                _charge = Def.Charge * (Elite ? GameConfig.EnemyChargeEliteMul : 1f);
                Impact.Spawn(transform.position, new Color(Def.Tint.r, Def.Tint.g, Def.Tint.b, 0.45f),
                             _sizeWorld * 1.6f, 0.3f);
                return;
            }
            DoFire(player);
            _fireTimer = NextInterval(_diff, _zone);
        }

        void DoFire(PlayerShip player)
        {
            var diff = Director.Instance != null ? Director.Instance.Diff : Difficulties.Standard;
            int tier = Director.Instance != null ? Director.Instance.DensityTier : 0;
            // 原版 entities.js:1392：弹速 = min(300, 150×(1+tier×0.14)) × 难度系数
            float spPx = Mathf.Min(300f, 150f * (1f + tier * 0.14f)) * diff.BulletSpeed;
            float sp = spPx * GameConfig.Px;
            Vector2 from = (Vector2)transform.position + Vector2.down * (_sizeWorld * 0.4f);
            Vector2 toPlayer = (Vector2)player.transform.position - (Vector2)transform.position;
            float aim = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
            var tint = Def.Tint;

            Projectile Add(float deg, float speed, string kind, float rPx)
            {
                float a = deg * Mathf.Deg2Rad;
                return FireOne(from, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed,
                    EnemyTint(kind, tint), rPx * GameConfig.Px, kind, 9f);
            }
            void Fan(int n, float spreadDeg, float speed, string kind, float rPx)
            {
                for (int i = 0; i < n; i++) Add(aim + (i - (n - 1) * 0.5f) * spreadDeg, speed, kind, rPx);
            }

            switch (Def.Fire)
            {
                case FireMode.Aimed:
                    Add(aim, sp, "orb", 6f);
                    if (Elite) Fan(3, 11.5f, sp * 0.95f, "shard", 6f);
                    break;
                case FireMode.Fan:
                    Fan(Elite ? 5 : 3, 10.3f, sp * 0.95f, "shard", 6f);
                    break;
                case FireMode.Ring:
                {
                    int n = Elite ? 14 : 10;
                    float off = _age * 0.5f * Mathf.Rad2Deg;
                    for (int i = 0; i < n; i++) Add(off + i * 360f / n, sp * 0.72f, "orb", 6f);
                    break;
                }
                case FireMode.Spread:
                    Add(aim - 18.3f, sp * 0.8f, "orb", 6f);
                    Add(aim + 18.3f, sp * 0.8f, "orb", 6f);
                    break;
                case FireMode.Heavy:
                    if (Elite)
                    {
                        Add(aim - 12.6f, sp * 0.42f, "plasma", 13f);
                        Add(aim + 12.6f, sp * 0.42f, "plasma", 13f);
                    }
                    Fan(3, 14.9f, sp * 0.55f, "heavy", 10f);
                    break;
                case FireMode.Needle:
                    Fan(Elite ? 5 : 3, 4.3f, 330f * GameConfig.Px, "needle", 5f);
                    break;
                case FireMode.Mines:
                {
                    int n = Elite ? 4 : 3;
                    for (int i = 0; i < n; i++)
                    {
                        float a = (aim + (i - (n - 1) * 0.5f) * 31.5f) * Mathf.Deg2Rad;
                        // 原版水雷：life 8 秒、引信 1.8~2.4 秒、炸 10 颗破片。
                        // 之前把引信当成了 life，所以水雷既不会减速成"雷帘"，破片数也只有 6。
                        var b = BulletPool.Instance.Spawn(Team.Enemy, from,
                            new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (105f * GameConfig.Px),
                            1f, 9f * GameConfig.Px, EnemyTint("mine", tint), "mine", 8f);
                        b.Fuse = Random.Range(1.8f, 2.4f);
                        b.SplitOnExpire = 10;
                    }
                    break;
                }
                case FireMode.Wavefire:
                    Add(aim - 20.6f, sp * 0.82f, "wave", 7f).Wave = 2.6f;
                    Add(aim + 20.6f, sp * 0.82f, "wave", 7f).Wave = 2.6f;
                    Add(aim, sp * 1.1f, "needle", 5f);
                    break;
                case FireMode.Carrier:
                    Add(aim - 9.2f, sp * 0.6f, "orb", 7f);
                    Add(aim + 9.2f, sp * 0.6f, "orb", 7f);
                    break;
            }
        }

        /// <summary>原版弹体配色：针弹青、水雷金、重弹橙、碎片跟机体同色。</summary>
        static Color EnemyTint(string kind, Color fallback)
        {
            switch (kind)
            {
                case "needle": return new Color(0.55f, 0.98f, 0.95f);
                case "mine": return new Color(1f, 0.85f, 0.35f);
                case "heavy":
                case "plasma": return new Color(1f, 0.86f, 0.55f);
                default: return fallback;
            }
        }

        Projectile FireOne(Vector2 pos, Vector2 vel, Color tint, float radius, string kind, float life = 9f)
        {
            var p = BulletPool.Instance.Spawn(Team.Enemy, pos, vel, 1f, radius, tint, kind, life);
            p.Life = life;
            return p;
        }

        static Vector2 Rot(Vector2 v, float deg)
        {
            float rad = deg * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>受击/蓄力/装甲/旋转环，全部走叠加层，不动本体颜色。</summary>
        void TickVisuals(float dt)
        {
            if (_flash != null)
            {
                if (_flashT > 0f)
                {
                    _flashT -= dt;
                    _flash.enabled = true;
                    _flash.color = new Color(1f, 1f, 1f, 0.85f * Mathf.Clamp01(_flashT / 0.07f));
                }
                else if (_charge > 0f)
                {
                    _flash.enabled = true;
                    _flash.color = new Color(1f, 1f, 1f, 0.12f + Mathf.PingPong(Time.time * 6f, 1f) * 0.22f);
                }
                else _flash.enabled = false;
            }
            if (_ring != null) _ring.transform.Rotate(0f, 0f, 46f * dt);
            if (_exhaust != null)
            {
                float k = 0.24f + Mathf.Sin(Time.time * 17f + _phase) * 0.07f;
                _exhaust.color = new Color(Def.Tint.r, Def.Tint.g, Def.Tint.b, k);
                _exhaust.transform.localScale = Vector3.one * (_sizeWorld * (0.82f + Mathf.Sin(Time.time * 21f + _phase) * 0.07f));
            }
        }

        public bool TakeDamage(float dmg)
        {
            if (_dead || !Targetable()) return false;
            Hp -= dmg * _damageTakenMul;
            if (Hp > 0f) { _flashT = 0.07f; return false; }

            _dead = true;
            if (GameRoot.Instance != null) GameRoot.Instance.Stats.Kills++;
            AudioDirector.Instance?.PlayExplode();
            // 击破碎片 + 热闪：大目标多两片、更亮，再补半圈白闪和轻微震动
            bool heavy = Elite || Kind == EnemyKind.Bulwark || Kind == EnemyKind.Carrier;
            Debris.Burst(transform.position, Def.Tint, _sizeWorld, heavy ? 7 : 4, heavy ? 0.72f : 0.42f);
            Impact.Spawn(transform.position, Color.Lerp(Def.Tint, Color.white, 0.55f),
                         _sizeWorld * (heavy ? 2.6f : 1.9f), heavy ? 0.26f : 0.2f);
            if (heavy)
            {
                Impact.Spawn(transform.position, new Color(1f, 1f, 1f, 0.9f), _sizeWorld * 3.4f, 0.1f);
                CameraShake.Instance?.Shake(0.09f, 0.16f);
            }
            if (Affix == Affix.Volatile) VolatileBlast();
            if (Kind == EnemyKind.Splitter)
            {
                // 原版：两只幼体在 ±16px、下方 4px 出生，血量只有母体的一半，入场宽限 0.1 秒
                float px = 16f * GameConfig.Px;
                Vector2 basePos = (Vector2)transform.position + Vector2.down * (4f * GameConfig.Px);
                var d = Director.Instance;
                if (d != null)
                {
                    d.SpawnChild(EnemyKind.Shardling, basePos + Vector2.left * px,
                                 GameConfig.SplitterChildHpFrac, 0.1f);
                    d.SpawnChild(EnemyKind.Shardling, basePos + Vector2.right * px,
                                 GameConfig.SplitterChildHpFrac, 0.1f);
                }
            }
            Director.Instance?.OnEnemyKilled(this);
            return true;
        }

        /// <summary>爆裂词缀：引信 0.5 秒后向四周炸开 6 发等离子（life 1.2 秒）。</summary>
        void VolatileBlast()
        {
            var p = BulletPool.Instance.Spawn(Team.Enemy, transform.position, Vector2.zero, 1f,
                11f * GameConfig.Px, new Color(1f, 0.6f, 0.3f), "plasma", 1.2f);
            p.Fuse = GameConfig.VolatileFuse;
            p.SplitOnExpire = GameConfig.VolatileSplit;
        }

        /// <summary>供导演在真正结算时调用（分数、掉落、星爆充能都在 GameRoot.KillEnemy 里）。</summary>
        public void MarkCounted() { _dead = true; }
    }
}
