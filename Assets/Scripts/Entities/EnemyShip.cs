using UnityEngine;

namespace AuroraRift
{
    public enum Affix { None, Armored, Swift, Blast }

    /// <summary>
    /// 12 种敌机统一实现：移动模式 + 开火模式 + 精英词缀，全部由 EnemyDefs 数据驱动。
    /// 第 3 星区起普通敌机可能带 装甲/急速/爆裂 词缀（轮廓不变、解法不同）。
    /// </summary>
    public class EnemyShip : MonoBehaviour
    {
        public EnemyKind Kind;
        public EnemyDef Def;
        public Affix Affix;
        public float Hp, MaxHp;
        public float Radius;
        public float Score;
        public bool Dead => _dead;

        SpriteRenderer _sr, _flash, _ring, _armor;
        float _age, _fireTimer, _phase, _charge, _flashT;
        Vector2 _anchor;
        bool _entered;
        bool _dead;
        float _hpMul = 1f;
        float _artHeight;

        public void Setup(EnemyKind kind, int zone, Difficulty diff)
        {
            Kind = kind;
            Def = EnemyDefs.Get(kind);
            Radius = Def.Radius;
            Score = Def.Score;

            // 星区与难度缩放（hp 随星区线性，弹速由难度乘）
            float zoneHp = 1f + (zone - 1) * 0.22f;
            Hp = MaxHp = Def.Hp * zoneHp * diff.BossHp;

            // 第 3 星区起普通敌机可能带词缀
            if (zone >= 3 && kind != EnemyKind.Shardling && Random.value < 0.22f)
            {
                Affix = (Affix)Random.Range(1, 4);
                if (Affix == Affix.Armored) { Hp *= 1.6f; MaxHp = Hp; _hpMul = 0.85f; }
                else if (Affix == Affix.Swift) _hpMul = 1f;
            }

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

            // 受击白闪 / 蓄力泛光：单独一层白剪影，不污染本体的金属色
            var flashGo = new GameObject("Flash");
            flashGo.transform.SetParent(transform, false);
            _flash = flashGo.AddComponent<SpriteRenderer>();
            _flash.sprite = ShipArt.Mask(art, _artHeight);
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _flash.sortingOrder = 7;
            _flash.enabled = false;
            // 放在机体后面并放大 5%：受击只闪一圈白边，不把整块装甲刷成灰白
            flashGo.transform.localScale = Vector3.one * 1.05f;

            if (Affix == Affix.Armored)
            {
                var armorGo = new GameObject("Armor");
                armorGo.transform.SetParent(transform, false);
                _armor = armorGo.AddComponent<SpriteRenderer>();
                _armor.sprite = _flash.sprite;
                _armor.color = new Color(0.92f, 0.96f, 1f, 0.5f);
                _armor.sortingOrder = 7;
                _armor.enabled = _flash.sprite != null;
                armorGo.transform.localScale = Vector3.one * 1.10f;
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
            _fireTimer = Def.Fire == FireMode.None ? float.MaxValue : Random.Range(Def.FireCdMin, Def.FireCdMax) * 1.35f;
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
                    float p = 0.12f + Mathf.PingPong(Time.time * 6f, 1f) * 0.22f;
                    _flash.color = new Color(1f, 1f, 1f, p);
                }
                else _flash.enabled = false;
            }
            if (_ring != null) _ring.transform.Rotate(0f, 0f, 46f * dt);
        }

        void Move(float dt)
        {
            Vector2 p = transform.position;
            float speed = Def.Speed * _hpMul * (Affix == Affix.Swift ? 1.35f : 1f);
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
                        if (p.y <= GameConfig.OrthoSizeMin * Def.TargetY) _entered = true;
                    }
                    else p.x = _anchor.x + Mathf.Sin(_age * 0.7f + _phase) * 1.2f;
                    break;
                case MovePattern.Orbit:
                    if (!_entered)
                    {
                        p.y -= speed * dt;
                        if (p.y <= GameConfig.OrthoSizeMin * Def.TargetY) { _entered = true; _anchor = p; }
                    }
                    else
                    {
                        float r = 1.1f;
                        p = _anchor + new Vector2(Mathf.Cos(_age * 0.9f + _phase), Mathf.Sin(_age * 0.9f + _phase)) * r;
                    }
                    break;
                case MovePattern.Rock:
                    p.y -= speed * dt;
                    p.x += 0.35f * dt;
                    transform.Rotate(0f, 0f, 40f * dt);
                    break;
                case MovePattern.Strafe:
                    if (!_entered)
                    {
                        p.y -= speed * dt;
                        if (p.y <= GameConfig.OrthoSizeMin * Def.TargetY) _entered = true;
                    }
                    else p.x = Mathf.Sin(_age * 0.55f + _phase) * (GameConfig.PlayHalfWidth - 1.2f);
                    break;
            }

            transform.position = p;
            if (p.y < -GameConfig.OrthoSizeMin - 1.2f) Director.Instance?.Despawn(this);
        }

        void Fire(float dt)
        {
            if (Def.Fire == FireMode.None) return;
            var player = GameRoot.Instance?.Player;
            if (player == null || !player.Alive) return;

            // 长炮/布雷有起手蓄力（0.55s / 0.3s）
            if (_charge > 0f)
            {
                _charge -= dt;
                if (_charge <= 0f) DoFire(player);
                return;
            }

            _fireTimer -= dt;
            if (_fireTimer > 0f) return;
            _fireTimer = Random.Range(Def.FireCdMin, Def.FireCdMax) * 1.35f;

            if (Def.Fire == FireMode.Needle) { _charge = 0.55f; return; }
            if (Def.Fire == FireMode.Mines) { _charge = 0.3f; return; }
            DoFire(player);
        }

        void DoFire(PlayerShip player)
        {
            float bs = Def.Speed > 0 ? 4.2f : 4.2f; // 弹速基准，Director 会按难度覆盖
            bs = Director.Instance != null ? Director.Instance.EnemyBulletSpeed : bs;
            Vector2 from = transform.position;
            Vector2 dir = ((Vector2)player.transform.position - from).normalized;
            var tint = new Color(1f, 0.45f, 0.55f);

            int tier = Director.Instance != null ? Director.Instance.DensityTier : 1;

            switch (Def.Fire)
            {
                case FireMode.Aimed:
                    Spawn(from, dir * bs, tint, 0.085f);
                    for (int i = 1; i < tier; i++)
                        Spawn(from, Rot(dir, (i * 6f) * (i % 2 == 1 ? 1f : -1f)) * bs, tint, 0.08f);
                    break;
                case FireMode.Fan:
                    for (int i = -2 - (tier - 1); i <= 2 + (tier - 1); i++)
                        Spawn(from, Rot(dir, i * 9f) * bs, tint, 0.085f);
                    break;
                case FireMode.Ring:
                    for (int i = 0; i < 12; i++) Spawn(from, Rot(Vector2.up, i * 30f) * bs * 0.85f, new Color(1f, 0.8f, 0.4f), 0.09f);
                    break;
                case FireMode.Spread:
                    for (int i = -3 - (tier - 1); i <= 3 + (tier - 1); i++)
                        Spawn(from, Rot(dir, i * 8f) * bs, tint, 0.08f);
                    break;
                case FireMode.Heavy:
                    Spawn(from, dir * bs * 0.8f, new Color(1f, 0.85f, 0.45f), 0.15f);
                    Spawn(from, Rot(dir, 8f) * bs * 0.8f, new Color(1f, 0.85f, 0.45f), 0.15f);
                    break;
                case FireMode.Needle:
                    Spawn(from, dir * bs * 1.6f, new Color(0.4f, 1f, 0.9f), 0.06f);
                    break;
                case FireMode.Mines:
                    for (int i = 0; i < 3; i++)
                        Spawn(from, new Vector2(Random.Range(-0.6f, 0.6f), -bs * 0.45f), new Color(1f, 0.85f, 0.35f), 0.12f);
                    break;
                case FireMode.Wavefire:
                    for (int i = 0; i < 2; i++) Spawn(from, Rot(dir, (i == 0 ? -14f : 14f)) * bs * 1.15f, tint, 0.075f);
                    break;
                case FireMode.Carrier:
                    Spawn(from, dir * bs * 0.9f, tint, 0.08f);
                    Director.Instance?.SpawnShardling(from + new Vector2(Random.Range(-0.4f, 0.4f), -0.5f));
                    break;
            }
        }

        void Spawn(Vector2 pos, Vector2 vel, Color tint, float radius)
        {
            var p = BulletPool.Instance.Spawn(Team.Enemy, pos, vel, 1f, radius, tint);
            p.Life = 6f;
        }

        static Vector2 Rot(Vector2 v, float deg)
        {
            float rad = deg * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public bool TakeDamage(float dmg)
        {
            if (_dead) return false;
            Hp -= dmg;
            if (Hp > 0f) { _flashT = 0.07f; return false; }

            _dead = true;
            GameRoot.Instance?.AddScore(Score);
            AudioDirector.Instance?.PlayExplode();
            if (GameRoot.Instance != null) GameRoot.Instance.Stats.Kills++;
            if (Affix == Affix.Blast) Blast();
            if (Kind == EnemyKind.Splitter) Director.Instance?.SpawnShardling(transform.position + Vector3.left * 0.3f);
            if (Kind == EnemyKind.Splitter) Director.Instance?.SpawnShardling(transform.position + Vector3.right * 0.3f);
            Director.Instance?.OnEnemyKilled(this);
            return true;
        }

        void Blast()
        {
            for (int i = 0; i < 8; i++)
                BulletPool.Instance.Spawn(Team.Enemy, transform.position,
                    new Vector2(Mathf.Cos(i * 45f * Mathf.Deg2Rad), Mathf.Sin(i * 45f * Mathf.Deg2Rad)) * 3.2f,
                    1f, 0.09f, new Color(1f, 0.6f, 0.3f));
        }
    }
}
