using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// Boss：三阶段（66% / 33% 切换，切阶段清屏 + 喘息），每阶段按固定序列出招（可背板），
    /// 每招 0.5–0.85 秒预告；血量 &lt;12% 进入「临界」不再停顿；破防槽满 → 停招 1.2s 且受伤 ×1.35。
    /// 招式池见 BossMoves（22 招）。
    /// </summary>
    public class BossController : MonoBehaviour
    {
        public float Hp, MaxHp;
        public float BreakGauge;
        public BossMove CurrentMove { get; private set; }
        public float TelegraphLeft => _telegraph;
        public bool Critical => Hp <= MaxHp * 0.12f;
        public int Phase { get; private set; }
        /// <summary>判定半径：与 4.08 世界单位的机体视觉半径（≈1.88）对齐，取略小值。</summary>
        public float HitRadius => 1.55f;

        int _tier, _phase, _index;
        float _timer, _telegraph = -1f, _stun;
        Vector2 _anchor;
        SpriteRenderer _sr, _aura, _ring, _core, _flash;
        string _kind = "gatekeeper";
        float _flashT, _ringSpeed = 22f;
        Color _accent = new Color(1f, 0.45f, 0.35f);
        bool _dead;
        Difficulty _diff;
        float _spawnTime;
        int _hitsAtSpawn;

        public void Setup(int zone, Difficulty diff)
        {
            _diff = diff;
            _tier = BossMoves.TierForZone(zone);
            MaxHp = Hp = (220f + zone * 60f) * diff.BossHp;
            _anchor = new Vector2(0f, GameConfig.OrthoSizeMin * 0.45f);
            transform.position = new Vector3(_anchor.x, GameConfig.OrthoSizeMin + 1.5f, 0f);

            _kind = _tier == 0 ? "gatekeeper" : _tier == 1 ? "weaver" : "helios";
            _accent = _tier == 0 ? GameConfig.Hex("FF6F91")
                    : _tier == 1 ? GameConfig.Hex("A597FF")
                    : GameConfig.Hex("FFD27A");
            _ringSpeed = _tier == 0 ? 18f : _tier == 1 ? -32f : 14f;

            _sr = gameObject.AddComponent<SpriteRenderer>();
            var body = ShipArt.Get("boss_" + _kind, 4.08f);
            if (body != null)
            {
                _sr.sprite = body;
                _sr.color = Color.white;
                transform.localScale = Vector3.one;
            }
            else
            {
                _sr.sprite = RuntimeSprites.Hex(96);
                _sr.color = _accent;
                transform.localScale = Vector3.one * 2.2f;
            }
            _sr.sortingOrder = 9;

            var ringSprite = ShipArt.Get("boss_" + _kind + "_ring", 4.08f);
            if (ringSprite != null)
            {
                var ringGo = new GameObject("Ring");
                ringGo.transform.SetParent(transform, false);
                _ring = ringGo.AddComponent<SpriteRenderer>();
                _ring.sprite = ringSprite;
                _ring.sortingOrder = 10;
            }

            var auraGo = new GameObject("Aura");
            auraGo.transform.SetParent(transform, false);
            _aura = auraGo.AddComponent<SpriteRenderer>();
            _aura.sprite = ShipArt.Get("bullet_aura_ring", 5.9f) ?? RuntimeSprites.Ring(96);
            _aura.color = new Color(_accent.r, _accent.g, _accent.b, 0.16f);
            _aura.sortingOrder = 7;

            var coreGo = new GameObject("CoreGlow");
            coreGo.transform.SetParent(transform, false);
            _core = coreGo.AddComponent<SpriteRenderer>();
            _core.sprite = ShipArt.Get("bullet_glow_soft", 3.0f) ?? RuntimeSprites.Disc(64);
            _core.color = new Color(_accent.r, _accent.g, _accent.b, 0.26f);
            _core.sortingOrder = 8;

            var flashGo = new GameObject("Flash");
            flashGo.transform.SetParent(transform, false);
            _flash = flashGo.AddComponent<SpriteRenderer>();
            _flash.sprite = ShipArt.Mask("boss_" + _kind, 4.08f);
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _flash.sortingOrder = 8;
            _flash.enabled = false;
            // 白剪影压在机体后面、略放大，只露出受击光环，不再整机泛白
            flashGo.transform.localScale = Vector3.one * 1.035f;

            _timer = 2.2f;
            GameRoot.Instance?.ShowBanner("! 星区核心接近");

            _spawnTime = Time.time;
            _hitsAtSpawn = GameRoot.Instance != null ? GameRoot.Instance.Stats.Hits : 0;
        }

        void Update()
        {
            if (_dead) return;
            var root = GameRoot.Instance;
            if (root == null || root.State != GameState.Playing) return;
            float dt = Time.deltaTime;
            TickVisuals(dt);

            Vector2 p = transform.position;
            if (p.y > _anchor.y)
            {
                p.y = Mathf.MoveTowards(p.y, _anchor.y, 2.2f * dt);
                transform.position = p;
                return;
            }

            if (_stun > 0f)
            {
                _stun -= dt;
                return;
            }

            int wantPhase = Hp / MaxHp > 0.66f ? 0 : Hp / MaxHp > 0.33f ? 1 : 2;
            if (wantPhase != _phase)
            {
                _phase = wantPhase;
                _index = 0;
                ClearBullets();
                TriggerShockwave();
                GameRoot.Instance?.ShowBanner($"核心进入第 {_phase + 1} 阶段");
                _timer = 1.2f;
                _telegraph = -1f;
                return;
            }

            if (_telegraph > 0f)
            {
                _telegraph -= dt;
                if (_telegraph <= 0f) { Execute(CurrentMove); _timer = Cooldown(); }
                return;
            }

            _timer -= dt;
            if (_timer <= 0f)
            {
                CurrentMove = BossMoves.Pick(_tier, _phase, _index++);
                if (Critical) { Execute(CurrentMove); _timer = Cooldown(); }
                else
                {
                    _telegraph = BossMoves.Telegraph(CurrentMove);
                    AudioDirector.Instance?.PlayWarn();
                }
            }
        }

        float Cooldown() => (Critical ? 0.85f : 1.35f) * (2f - _diff.BossBullet);

        /// <summary>视觉层：旋转环、呼吸光环、核心脉动、受击/破防/临界叠色。本体贴图不染色。</summary>
        void TickVisuals(float dt)
        {
            float t = Time.time;
            if (_ring != null) _ring.transform.Rotate(0f, 0f, _ringSpeed * dt);

            bool stun = _stun > 0f;
            bool crit = Critical;
            Color auraCol = stun ? new Color(0.55f, 0.75f, 1f)
                          : crit ? new Color(1f, 0.25f, 0.25f)
                          : _accent;

            if (_aura != null)
            {
                _aura.transform.Rotate(0f, 0f, -6f * dt);
                float sc = 1f + Mathf.Sin(t * 1.6f) * 0.025f;
                _aura.transform.localScale = Vector3.one * sc;
                _aura.color = new Color(auraCol.r, auraCol.g, auraCol.b,
                    stun ? 0.28f : crit ? 0.30f : 0.14f);
            }
            if (_core != null)
            {
                float pulse = 1f + Mathf.Sin(t * 3.4f) * 0.08f;
                _core.transform.localScale = Vector3.one * pulse;
                _core.color = new Color(auraCol.r, auraCol.g, auraCol.b,
                    stun ? 0.32f : crit ? 0.40f : 0.24f);
            }
            if (_flash != null)
            {
                if (_flashT > 0f)
                {
                    _flashT -= dt;
                    _flash.enabled = true;
                    // Boss 持续挨打，白闪只做外圈，不洗掉装甲配色
                    _flash.color = new Color(1f, 1f, 1f, 0.55f * Mathf.Clamp01(_flashT / 0.07f));
                }
                else if (stun)
                {
                    _flash.enabled = true;
                    _flash.color = new Color(0.55f, 0.75f, 1f, 0.14f);
                }
                else if (crit)
                {
                    _flash.enabled = true;
                    _flash.color = new Color(1f, 0.2f, 0.2f, 0.06f + Mathf.Sin(t * 6f) * 0.035f);
                }
                else _flash.enabled = false;
            }
        }

        void Execute(BossMove m)
        {
            var player = GameRoot.Instance?.Player;
            Vector2 from = transform.position;
            Vector2 aim = player != null
                ? ((Vector2)player.transform.position - from).normalized
                : Vector2.down;
            float sp = 3.6f * _diff.BossBullet;
            var hot = new Color(1f, 0.72f, 0.3f);
            var cold = new Color(1f, 0.55f, 0.5f);
            var pink = new Color(1f, 0.4f, 0.75f);
            var cyan = new Color(0.4f, 1f, 0.9f);

            switch (m)
            {
                case BossMove.Ring: Ring(from, 14, sp, hot); break;
                case BossMove.RingDouble:
                    Ring(from, 12, sp, hot);
                    Ring(from, 12, sp * 0.7f, cold);
                    break;
                case BossMove.Fan: Fan(from, aim, 5, 10f, sp, cold); break;
                case BossMove.FanWide: Fan(from, aim, 9, 8f, sp * 0.95f, cold); break;
                case BossMove.Aimed: Shot(from, aim * sp * 1.4f, cyan, 0.07f); break;
                case BossMove.AimedBurst:
                    for (int i = 0; i < 3; i++) Shot(from, Rot(aim, (i - 1) * 6f) * sp * 1.4f, cyan, 0.07f);
                    break;
                case BossMove.Spiral:
                    for (int i = 0; i < 10; i++) Shot(from, Rot(Vector2.up, i * 36f) * sp * 0.9f, pink, 0.08f);
                    break;
                case BossMove.SpiralReverse:
                    for (int i = 0; i < 10; i++) Shot(from, Rot(Vector2.up, -i * 36f) * sp * 0.9f, pink, 0.08f);
                    break;
                case BossMove.WallGap:
                    for (int i = -6; i <= 6; i++)
                    {
                        if (Mathf.Abs(i) <= 1) continue;
                        Shot(from + new Vector2(i * 0.7f, 0f), Vector2.down * sp, cold, 0.1f);
                    }
                    break;
                case BossMove.WallRotate:
                    for (int i = -6; i <= 6; i++)
                        Shot(from + new Vector2(i * 0.7f, 0f), Rot(Vector2.down, 18f) * sp, cold, 0.1f);
                    break;
                case BossMove.LaserSweep:
                    for (int i = 0; i < 16; i++)
                        Shot(from, Rot(Vector2.down, -28f + i * 3.5f) * sp * 1.5f, cyan, 0.06f);
                    break;
                case BossMove.LaserCross:
                    for (int i = 0; i < 12; i++)
                    {
                        Shot(from, Rot(Vector2.up, i * 30f) * sp * 1.2f, cyan, 0.06f);
                        Shot(from, Rot(Vector2.down, i * 30f) * sp * 1.2f, cyan, 0.06f);
                    }
                    break;
                case BossMove.Mines:
                    for (int i = 0; i < 4; i++)
                        Shot(from + new Vector2(Random.Range(-2f, 2f), 0f), new Vector2(0f, -sp * 0.4f), hot, 0.13f);
                    break;
                case BossMove.MineField:
                    for (int i = -3; i <= 3; i++)
                        Shot(from + new Vector2(i * 1.1f, 0f), new Vector2(0f, -sp * 0.35f), hot, 0.12f);
                    break;
                case BossMove.Teleport:
                    transform.position = new Vector3(Random.Range(-3f, 3f), _anchor.y, 0f);
                    Ring(transform.position, 10, sp, pink);
                    break;
                case BossMove.Dash:
                    transform.position = new Vector3(
                        Mathf.MoveTowards(transform.position.x, _anchor.x, 3f), _anchor.y, 0f);
                    Ring(transform.position, 8, sp * 1.2f, hot);
                    break;
                case BossMove.SpiralDual:
                    for (int i = 0; i < 8; i++)
                    {
                        Shot(from, Rot(Vector2.up, i * 45f) * sp, pink, 0.08f);
                        Shot(from, Rot(Vector2.up, i * 45f + 22.5f) * sp * 0.8f, cold, 0.08f);
                    }
                    break;
                case BossMove.AimedRing:
                    Ring(from, 10, sp * 0.9f, cold);
                    Shot(from, aim * sp * 1.5f, cyan, 0.07f);
                    break;
                case BossMove.BulletRain:
                    for (int i = 0; i < 14; i++)
                    {
                        Vector2 at = new Vector2(Random.Range(-GameConfig.PlayHalfWidth, GameConfig.PlayHalfWidth),
                                                 GameConfig.OrthoSizeMin + 0.5f);
                        Shot(at, Vector2.down * sp * 1.1f, hot, 0.09f);
                    }
                    break;
                case BossMove.Converge:
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f;
                        Vector2 spawn = (Vector2)from + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * 4f;
                        Shot(spawn, ((Vector2)from - spawn).normalized * sp * 1.1f, pink, 0.085f);
                    }
                    break;
                case BossMove.Crossfire:
                    for (int i = 0; i < 6; i++)
                    {
                        Shot(from, Rot(aim, i * 12f - 30f) * sp, cold, 0.08f);
                        Shot(from, Rot(-aim, i * 12f - 30f) * sp, cold, 0.08f);
                    }
                    break;
                case BossMove.FinalBurst:
                    Ring(from, 20, sp * 1.15f, hot);
                    Fan(from, aim, 7, 9f, sp * 1.1f, pink);
                    break;
            }
        }

        /// <summary>阶段切换的视觉冲击：一圈快速扩散并淡出的光环。</summary>
        void TriggerShockwave()
        {
            var go = new GameObject("Shock");
            go.transform.position = transform.position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ShipArt.Get("bullet_aura_ring", 1.4f) ?? RuntimeSprites.Ring(128);
            sr.color = new Color(1f, 0.7f, 0.4f, 0.7f);
            sr.sortingOrder = 12;
            go.transform.localScale = Vector3.one * 1.5f;
            go.AddComponent<Shockwave>();
        }

        void Ring(Vector2 from, int n, float sp, Color c)
        {
            float off = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float a = off + i * Mathf.PI * 2f / n;
                Shot(from, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, c, 0.09f);
            }
        }

        void Fan(Vector2 from, Vector2 dir, int count, float spread, float sp, Color c)
        {
            for (int i = 0; i < count; i++)
            {
                float d = (i - (count - 1) * 0.5f) * spread;
                Shot(from, Rot(dir, d) * sp, c, 0.085f);
            }
        }

        void Shot(Vector2 pos, Vector2 vel, Color c, float r)
        {
            var p = BulletPool.Instance.Spawn(Team.Enemy, pos, vel, 1f, r, c);
            p.Life = 7f;
        }

        static Vector2 Rot(Vector2 v, float deg)
        {
            float rad = deg * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public void TakeDamage(float dmg)
        {
            if (_dead) return;
            _flashT = 0.07f;
            float mul = _stun > 0f ? 1.35f : 1f;
            Hp -= dmg * mul;
            BreakGauge = Mathf.Min(100f, BreakGauge + dmg * 1.6f);
            if (BreakGauge >= 100f && _stun <= 0f)
            {
                BreakGauge = 0f;
                _stun = 1.2f;
                ClearBullets();
            }
            if (Hp <= 0f) Die();
        }

        static void ClearBullets()
        {
            var pool = BulletPool.Instance;
            if (pool == null) return;
            for (int i = pool.Active.Count - 1; i >= 0; i--)
                if (pool.Active[i] != null && pool.Active[i].Team == Team.Enemy) pool.Release(pool.Active[i]);
        }

        void Die()
        {
            _dead = true;
            var root = GameRoot.Instance;
            if (root != null)
            {
                float elapsed = Mathf.Max(0f, Time.time - _spawnTime);
                int timeBonus = Mathf.RoundToInt(Mathf.Max(400f, 2400f - elapsed * 40f));
                bool noHit = root.Stats.Hits == _hitsAtSpawn;
                int noHitBonus = noHit ? 1500 : 0;
                int grazeBonus = Mathf.Min(1500, root.Stats.Graze * 8);
                int baseScore = Mathf.RoundToInt(2000f + Director.Instance.Zone * 400f);

                root.AddScore(baseScore);
                root.AddScore(timeBonus + noHitBonus + grazeBonus);
                root.ShowClear(new[]
                {
                    $"核心击破  基础 +{Mathf.RoundToInt(baseScore * root.Difficulty.ScoreMul)}",
                    $"时间奖励  {Mathf.RoundToInt(elapsed)}s  +{timeBonus}",
                    $"无伤奖励  {(noHit ? "达成" : "未达成")}  +{noHitBonus}",
                    $"擦弹奖励   {root.Stats.Graze} 次  +{grazeBonus}",
                });
            }
            if (GameRoot.Instance != null) GameRoot.Instance.Stats.BossKills++;
            AudioDirector.Instance?.PlayExplode();
            CameraShake.Instance?.Shake(0.32f, 0.6f);
            for (int i = 0; i < 6; i++)
                Impact.Spawn(transform.position + (Vector3)(Random.insideUnitCircle * 1.4f),
                    new Color(1f, 0.7f, 0.35f, 0.9f), 0.9f, 0.35f);
            Director.Instance?.OnBossDefeated();
            Destroy(gameObject);
        }
    }
}
