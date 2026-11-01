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
            _fireTimer = Def.Fire == FireMode.None ? float.MaxValue : NextInterval(diff, zone);
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
            float speed = Def.Speed * _hpMul * (1f + tier * GameConfig.EnemySpeedPerTier);
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
            if (Def.Fire == FireMode.None) return;
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
            float bs = Director.Instance != null ? Director.Instance.EnemyBulletSpeed : 4.2f;
            Vector2 from = transform.position;
            Vector2 dir = ((Vector2)player.transform.position - from).normalized;
            var tint = new Color(1f, 0.45f, 0.55f);
            int tier = Director.Instance != null ? Director.Instance.DensityTier : 1;
            float r = 5f * GameConfig.Px;

            void SpawnAt(Vector2 pos, Vector2 vel, Color c, float radius, string kind = "orb") =>
                FireOne(pos, vel, c, radius, kind);

            switch (Def.Fire)
            {
                case FireMode.Aimed:
                    SpawnAt(from, dir * bs, tint, r, "orb");
                    for (int i = 1; i < tier; i++)
                        SpawnAt(from, Rot(dir, (i * 6f) * (i % 2 == 1 ? 1f : -1f)) * bs, tint, r * 0.9f, "orb");
                    break;
                case FireMode.Fan:
                    for (int i = -2 - (tier - 1); i <= 2 + (tier - 1); i++)
                        SpawnAt(from, Rot(dir, i * 9f) * bs, tint, r, "orb");
                    break;
                case FireMode.Ring:
                    for (int i = 0; i < (Elite ? 14 : 10); i++)
                        SpawnAt(from, Rot(Vector2.down, i * 36f) * bs * 0.85f, new Color(1f, 0.8f, 0.4f), r * 1.1f, "orb");
                    break;
                case FireMode.Spread:
                    for (int i = -3 - (tier - 1); i <= 3 + (tier - 1); i++)
                        SpawnAt(from, Rot(dir, i * 8f) * bs, tint, r * 0.9f, "orb");
                    break;
                case FireMode.Heavy:
                    SpawnAt(from, dir * bs * 0.8f, new Color(1f, 0.85f, 0.45f), 9f * GameConfig.Px, "heavy");
                    SpawnAt(from, Rot(dir, 8f) * bs * 0.8f, new Color(1f, 0.85f, 0.45f), 9f * GameConfig.Px, "heavy");
                    break;
                case FireMode.Needle:
                    SpawnAt(from, dir * bs * 1.6f, new Color(0.4f, 1f, 0.9f), 4f * GameConfig.Px, "needle");
                    break;
                case FireMode.Mines:
                    for (int i = 0; i < 3; i++)
                        SpawnAt(from, new Vector2(Random.Range(-0.6f, 0.6f), -bs * 0.45f),
                            new Color(1f, 0.85f, 0.35f), 8f * GameConfig.Px, "mine");
                    break;
                case FireMode.Wavefire:
                    for (int i = 0; i < 2; i++) SpawnAt(from, Rot(dir, (i == 0 ? -14f : 14f)) * bs * 1.15f, tint, r * 0.85f, "orb");
                    break;
                case FireMode.Carrier:
                    SpawnAt(from, dir * bs * 0.9f, tint, r * 0.9f, "orb");
                    Director.Instance?.SpawnShardling(from + new Vector2(Random.Range(-0.4f, 0.4f), -0.5f));
                    break;
            }
        }

        void FireOne(Vector2 pos, Vector2 vel, Color tint, float radius, string kind)
        {
            var p = BulletPool.Instance.Spawn(Team.Enemy, pos, vel, 1f, radius, tint, kind, 7f);
            p.Life = 7f;
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
