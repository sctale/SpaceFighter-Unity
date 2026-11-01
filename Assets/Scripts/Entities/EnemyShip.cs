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

        SpriteRenderer _sr, _flash, _ring, _armor;
        float _age, _fireTimer, _phase, _charge, _flashT;
        float _summonT = 3.2f;
        Vector2 _anchor;
        bool _entered;
        bool _dead;
        float _hpMul = 1f;
        float _artHeight;
        float _damageTakenMul = 1f;
        float _fireMul = 1f;

        public void Setup(EnemyKind kind, int zone, Difficulty diff, bool elite = false)
        {
            Kind = kind;
            Def = EnemyDefs.Get(kind);
            Elite = elite;
            _diff = diff;
            _zone = zone;
            Radius = Def.Radius;
            Score = Def.Score;

            // 第 3 星区起普通敌机可能带词缀（精英由导演指定，两者不叠加）
            if (zone >= 3 && !elite && kind != EnemyKind.Shardling && kind != EnemyKind.Rock)
            {
                float chance = GameConfig.AffixBaseChance +
                               Mathf.Min(GameConfig.AffixMaxExtra, (zone - 3) * 0.02f);
                if (Random.value < chance) Affix = (Affix)Random.Range(1, 4);
            }

            float sizeMul = 1f;
            if (Affix == Affix.Armored) { _damageTakenMul = GameConfig.ArmoredDamageTakenMul; }
            else if (Affix == Affix.Rapid) { _fireMul = GameConfig.RapidFireMul; sizeMul *= GameConfig.RapidSizeMul; }
            else if (Affix == Affix.Volatile) { }
            if (Elite) sizeMul *= GameConfig.EliteSizeMul;
            Radius *= sizeMul;

            float hpScale = 1f + (zone - 1) * GameConfig.EnemyHpPerSector;
            float hp = Def.Hp * hpScale;
            if (Elite) hp *= GameConfig.EliteHpMul;
            if (Affix == Affix.Armored) hp *= GameConfig.ArmoredHpMul;
            if (Affix == Affix.Volatile) hp *= GameConfig.VolatileHpMul;
            Hp = MaxHp = Mathf.Round(hp);

            float score = Def.Score;
            if (Elite) score *= GameConfig.EliteScoreMul;
            if (Affix != Affix.None) score *= 1.18f;
            Score = Mathf.Round(score);

            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();

            string art = "enemy_" + kind.ToString().ToLowerInvariant();
            _artHeight = Radius / ShipArt.ArtExtent;
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
            if (_age <= GameConfig.EntryDelay + GameConfig.EntryGrace) return false;
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
            float speed = Def.Speed * (1f + tier * 0.14f);   // 原版 speedMul = 1 + tier×0.14（与血量倍率无关）
            float amp = Def.Amp;

            switch (Def.Move)
            {
                case MovePattern.Sine:
                    p.y -= speed * dt;
                    p.x = _anchor.x + Mathf.Sin(_age * 1.4f + _phase) * amp * 0.5f;
                    break;
                case MovePattern.Dive:
                    p.y -= speed * dt;
                    p.x += Mathf.Sin(_age * 1.4f + _phase) * 0.6f * dt;
                    break;
                case MovePattern.Hover:
                    if (!_entered)
                    {
                        p.y -= speed * dt;
                        if (p.y <= GameView.FromTopFrac(Def.TargetY)) _entered = true;
                    }
                    else p.x = _anchor.x + Mathf.Sin(_age * 0.7f + _phase) * 48f * GameConfig.Px;
                    break;
                case MovePattern.Orbit:
                    if (!_entered)
                    {
                        p.y -= speed * dt;
                        if (p.y <= GameView.FromTopFrac(Def.TargetY)) { _entered = true; _anchor = p; }
                    }
                    else
                    {
                        float r = 44f * GameConfig.Px;
                        p = _anchor + new Vector2(Mathf.Cos(_age * 0.9f + _phase), Mathf.Sin(_age * 0.9f + _phase)) * r;
                    }
                    break;
                case MovePattern.Rock:
                    p.y -= speed * dt;
                    p.x += 14f * GameConfig.Px * dt;
                    transform.Rotate(0f, 0f, 40f * dt);
                    break;
                case MovePattern.Strafe:
                    if (!_entered)
                    {
                        p.y -= speed * dt;
                        if (p.y <= GameView.FromTopFrac(Def.TargetY)) _entered = true;
                    }
                    else p.x = Mathf.Sin(_age * 0.55f + _phase) * (GameView.MoveHalfWidth - 0.4f);
                    break;
            }

            transform.position = p;
            if (p.y < GameView.DespawnY) Director.Instance?.Despawn(this);
        }

        void Fire(float dt)
        {
            // 原版：只有进画面、且在 80% 高度以上才开火；航母另算召唤计时
            float myY = transform.position.y;
            bool inBand = myY > -0.6f * GameView.HalfHeight && myY < GameView.HalfHeight - 20f * GameConfig.Px;

            if (Kind == EnemyKind.Carrier && inBand)
            {
                _summonT -= dt;
                if (_summonT <= 0f)
                {
                    _summonT = Random.Range(2.6f, 3.6f);
                    var d = Director.Instance;
                    if (d != null && d.AliveEnemies < 38)
                        d.SpawnShardling(transform.position + new Vector3(Random.Range(-0.4f, 0.4f), -0.5f));
                }
            }

            if (Def.Fire == FireMode.None) return;
            if (!inBand) return;
            var player = GameRoot.Instance?.Player;
            if (player == null || !player.Alive) return;

            if (_charge > 0f)
            {
                _charge -= dt;
                if (_charge <= 0f) DoFire(player);
                return;
            }

            _fireTimer -= dt;
            if (_fireTimer > 0f) return;
            _fireTimer = NextInterval(_diff, _zone);

            if (Def.Fire == FireMode.Needle) { _charge = 0.55f; return; }
            if (Def.Fire == FireMode.Mines) { _charge = 0.3f; return; }
            DoFire(player);
        }

        void DoFire(PlayerShip player)
        {
            var diff = Director.Instance != null ? Director.Instance.Diff : Difficulties.Standard;
            int tier = Director.Instance != null ? Director.Instance.DensityTier : 0;
            // 原版 entities.js:1392：弹速 = min(300, 150×(1+tier×0.14)) × 难度系数
            float spPx = Mathf.Min(300f, 150f * (1f + tier * 0.14f)) * diff.BulletSpeed;
            float sp = spPx * GameConfig.Px;
            Vector2 from = (Vector2)transform.position + Vector2.down * (Def.Radius * 0.4f);
            Vector2 toPlayer = (Vector2)player.transform.position - (Vector2)transform.position;
            float aim = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
            var tint = Def.Tint;

            void Add(float deg, float speed, string kind, float rPx)
            {
                float a = deg * Mathf.Deg2Rad;
                FireOne(from, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed,
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
                        var b = BulletPool.Instance.Spawn(Team.Enemy, from,
                            new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (105f * GameConfig.Px),
                            1f, 9f * GameConfig.Px, EnemyTint("mine", tint), "mine", Random.Range(1.8f, 2.4f));
                        b.SplitOnExpire = 10;
                    }
                    break;
                }
                case FireMode.Wavefire:
                    Add(aim - 20.6f, sp * 0.82f, "wave", 7f);
                    Add(aim + 20.6f, sp * 0.82f, "wave", 7f);
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

        void FireOne(Vector2 pos, Vector2 vel, Color tint, float radius, string kind, float life = 9f)
        {
            var p = BulletPool.Instance.Spawn(Team.Enemy, pos, vel, 1f, radius, tint, kind, life);
            p.Life = life;
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
        }

        public bool TakeDamage(float dmg)
        {
            if (_dead || !Targetable()) return false;
            Hp -= dmg * _damageTakenMul;
            if (Hp > 0f) { _flashT = 0.07f; return false; }

            _dead = true;
            if (GameRoot.Instance != null) GameRoot.Instance.Stats.Kills++;
            AudioDirector.Instance?.PlayExplode();
            if (Affix == Affix.Volatile) VolatileBlast();
            if (Kind == EnemyKind.Splitter)
            {
                Director.Instance?.SpawnShardling(transform.position + Vector3.left * 0.3f);
                Director.Instance?.SpawnShardling(transform.position + Vector3.right * 0.3f);
            }
            Director.Instance?.OnEnemyKilled(this);
            return true;
        }

        /// <summary>爆裂词缀：延迟 0.5 秒后向四周炸开 6 发等离子。</summary>
        void VolatileBlast()
        {
            var p = BulletPool.Instance.Spawn(Team.Enemy, transform.position, Vector2.zero, 1f,
                11f * GameConfig.Px, new Color(1f, 0.6f, 0.3f), "plasma", GameConfig.VolatileFuse);
            p.SplitOnExpire = GameConfig.VolatileSplit;
        }

        /// <summary>供导演在真正结算时调用（分数、掉落、星爆充能都在 GameRoot.KillEnemy 里）。</summary>
        public void MarkCounted() { _dead = true; }
    }
}
