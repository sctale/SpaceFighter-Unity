using System.Collections.Generic;
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
        public bool Critical => Hp <= MaxHp * GameConfig.BossCriticalFrac;
        public bool Dying => _dead;
        public float BreakMax => _breakMax;
        public float BulletSpeed => _bulletSpeed;
        public int Phase { get; private set; }
        /// <summary>判定半径：与 4.08 世界单位的机体视觉半径（≈1.88）对齐，取略小值。</summary>
        public float HitRadius => 1.55f;

        int _tier, _phase, _index, _zone, _bossIndex;
        float _breakMax = 100f, _bulletSpeed = 2.8f, _mercy = -1f, _idleTimer;
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
            _zone = zone;
            _tier = BossMoves.TierForZone(zone);
            _bossIndex = BossMoves.BossForZone(zone);
            // 原版：def.hp × (1 + min(3.4, (s-1)×0.70)) × 难度
            MaxHp = Hp = GameConfig.BossBaseHp(_bossIndex) * GameConfig.BossHpScale(zone) * diff.BossHp;
            _breakMax = Mathf.Max(GameConfig.BossBreakFloor, MaxHp * GameConfig.BossBreakFracOfMaxHp);
            _bulletSpeed = (GameConfig.BossBulletSpeedBase + _tier * GameConfig.BossBulletSpeedPerTier)
                           * diff.BossBullet * GameConfig.Px;
            // 原版 boss.targetY = max(H*0.27, 252px)（距顶），入场点在屏外一格。
            // 必须用实时视口高度，写死 OrthoSizeMin 会让 20:9 长屏的 Boss 悬在画面中间。
            float topPx = Mathf.Max(2f * GameView.HalfHeight / GameConfig.Px * 0.27f, 252f);
            _anchor = new Vector2(0f, GameView.FromTopPx(topPx));
            transform.position = new Vector3(_anchor.x, GameView.HalfHeight + 1.5f, 0f);
            _pending.Clear();
            BossLaser.ClearAll();

            _kind = _bossIndex == 0 ? "gatekeeper" : _bossIndex == 1 ? "weaver" : "helios";
            _accent = _bossIndex == 0 ? GameConfig.Hex("FF6F91")
                    : _bossIndex == 1 ? GameConfig.Hex("A597FF")
                    : GameConfig.Hex("FFD27A");
            _ringSpeed = _bossIndex == 0 ? 18f : _bossIndex == 1 ? -32f : 14f;

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
            TickPending(dt);

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

            float frac = Hp / Mathf.Max(1f, MaxHp);
            int wantPhase = frac > GameConfig.BossPhaseHigh ? 0
                          : frac > GameConfig.BossPhaseLow ? 1 : 2;
            if (wantPhase != _phase)
            {
                _phase = wantPhase;
                _index = 0;
                GameRoot.Instance?.ClearEnemyBullets(true);
                // 原版换阶段是一口明确的喘息：清弹、清光束、停掉未发完的分时回调
                _pending.Clear();
                BossLaser.ClearAll();
                TriggerShockwave();
                GameRoot.Instance?.ShowBanner($"核心进入第 {_phase + 1} 阶段");
                // mercy：换阶段后给一段明确的喘息窗口
                _mercy = GameConfig.BossMercyWindow;
                _timer = GameConfig.BossMercyWindow;
                _telegraph = -1f;
                if (GameRoot.Instance != null) GameRoot.Instance.Player?.GrantInvulnBossPhase();
                return;
            }

            if (_mercy > 0f)
            {
                _mercy -= dt;
                return;
            }

            if (_telegraph > 0f)
            {
                _telegraph -= dt;
                if (_telegraph <= 0f) { Execute(CurrentMove); _timer = Cooldown(); }
                return;
            }

            _timer -= dt;
            IdleFire(dt);
            if (_timer <= 0f)
            {
                CurrentMove = BossMoves.Pick(_bossIndex, _phase, _index++);
                if (Critical) { Execute(CurrentMove); _timer = Cooldown(); }
                else
                {
                    _telegraph = BossMoves.Telegraph(CurrentMove);
                    AudioDirector.Instance?.PlayWarn();
                }
            }
        }

        float Cooldown() => (Critical ? 0.85f : 1.35f) * (2f - _diff.BossBullet);

        // ------------------------------------------------------------ 招式内的分时发射
        /// <summary>
        /// 原版一招里是分时刻发好几段的（this.at(a, 0.1, ...) / at(a, 1.25, ...)），
        /// 之前 Unity 版把整招压成同一帧，光束和补刀弹幕糊在一起，读不出招。
        /// 这里补一个最小的分时队列。
        /// </summary>
        class Pending { public float at; public float t; public System.Action act; public bool done; }
        readonly List<Pending> _pending = new List<Pending>();
        float _idleCd;

        void At(float at, System.Action act) => _pending.Add(new Pending { at = at, act = act });

        void TickPending(float dt)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var e = _pending[i];
                e.t += dt;
                if (e.t >= e.at) { _pending.RemoveAt(i); if (!e.done) { e.done = true; e.act?.Invoke(); } }
            }
        }

        /// <summary>
        /// 原版 entities.js:2029 idleFire：招式间隙机体不会停火。
        /// 二阶段起 0.95s 一枚针，三阶段 0.66s 两枚（±0.11rad 夹射）。
        /// </summary>
        void IdleFire(float dt)
        {
            if (_mercy > 0f || _phase < 1) return;
            _idleCd -= dt;
            if (_idleCd > 0f) return;
            var player = GameRoot.Instance?.Player;
            Vector2 from = transform.position;
            Vector2 aim = player != null
                ? ((Vector2)player.transform.position - from).normalized
                : Vector2.down;
            int n = _phase == 2 ? 2 : 1;
            var needle = new Color(0.55f, 0.95f, 1f);
            for (int i = 0; i < n; i++)
            {
                float d = (i - (n - 1) * 0.5f) * 0.22f * Mathf.Rad2Deg;
                Shot(from, Rot(aim, d) * _bulletSpeed * 0.92f, needle, 5f * GameConfig.Px);
            }
            _idleCd = _phase == 2 ? 0.66f : 0.95f;
        }

        /// <summary>分时回调里不能沿用出招瞬间的坐标，发射时刻重新取一次。</summary>
        Vector2 AimDir()
        {
            var player = GameRoot.Instance?.Player;
            Vector2 from = transform.position;
            return player != null ? ((Vector2)player.transform.position - from).normalized : Vector2.down;
        }

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

        /// <summary>开发期：-laser 直接指定一招，用来给新加的实体（光束）拍验收图。</summary>
        public void DebugCast(BossMove m) => Execute(m);

        void Execute(BossMove m)
        {
            var player = GameRoot.Instance?.Player;
            Vector2 from = transform.position;
            Vector2 aim = player != null
                ? ((Vector2)player.transform.position - from).normalized
                : Vector2.down;
            float sp = _bulletSpeed;
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
                {
                    // 原版 sweep：两条错开 1.15s 的扫射光束（一顺一逆），最后补一发扇形
                    float ang = Mathf.Atan2(aim.y, aim.x);
                    At(0.10f, () => { BossLaser.Spawn(transform.position, ang - 0.55f, 0.9f, 0.9f, 16f, 0.9f, _accent, this); AudioDirector.Instance?.PlayWarn(); });
                    At(1.25f, () => { BossLaser.Spawn(transform.position, ang + 0.55f, 0.8f, 0.85f, 14f, -0.95f, _accent, this); AudioDirector.Instance?.PlayWarn(); });
                    At(2.15f, () => Fan(transform.position, AimDir(), _phase >= 1 ? 5 : 3, 13.8f, sp, cold));
                    break;
                }
                case BossMove.LaserCross:
                {
                    // 原版 cross：随机基角的十字四分光束，1s 后补一圈环弹收尾
                    float base0 = Random.value * 0.6f;
                    At(0.15f, () =>
                    {
                        for (int i = 0; i < 4; i++)
                            BossLaser.Spawn(transform.position, base0 + i * Mathf.PI * 0.5f, 0.95f, 0.62f, 12f, 0f, _accent, this);
                        AudioDirector.Instance?.PlayWarn();
                    });
                    At(1.05f, () => Ring(transform.position, _phase >= 1 ? 14 : 10, sp * 0.7f, cold));
                    break;
                }
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
                                                 GameView.HalfHeight + 0.5f);
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
            float mul = _stun > 0f ? GameConfig.BossBreakDamageMul : 1f;
            Hp -= dmg * mul;
            BreakGauge = Mathf.Min(_breakMax, BreakGauge + dmg);
            if (BreakGauge >= _breakMax && _stun <= 0f)
            {
                BreakGauge = 0f;
                _stun = GameConfig.BossBreakStun;
                GameRoot.Instance?.ClearEnemyBullets(false);
                GameRoot.Instance?.RegisterBreak();
                GameRoot.Instance?.ShowBanner("核心破防 · 伤害窗口");
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
            _pending.Clear();
            BossLaser.ClearAll();
            var root = GameRoot.Instance;
            if (root != null)
            {
                // 原版结算：基础 + 时间 + 无伤 + 擦弹，整体再乘难度倍率
                float clearT = Mathf.Max(0f, Time.time - _spawnTime);
                int sector = Director.Instance != null ? Director.Instance.Zone : 1;
                float timeBonus = Mathf.Max(0f, (GameConfig.ClearTimeBonusBase - clearT) * GameConfig.ClearTimeBonusPerSecond);
                bool noHit = root.Stats.SectorHits == 0;
                float noHitBonus = noHit ? GameConfig.ClearNoHitBase + sector * GameConfig.ClearNoHitPerSector : 0f;
                float grazeBonus = root.Stats.SectorGraze * GameConfig.ClearGrazePerHit;
                float mul = root.Difficulty.ScoreMul;
                float baseScore = GameConfig.ClearBaseScore + sector * GameConfig.ClearBasePerSector;
                int total = Mathf.RoundToInt((baseScore + timeBonus + noHitBonus + grazeBonus) * mul);
                root.AddScore(total);
                root.ShowBanner("核心击破 +" + total);
                root.ShowClear(new[]
                {
                    $"基础分      {Mathf.RoundToInt(baseScore * mul)}",
                    $"时间奖励    {Mathf.RoundToInt(clearT)}s  +{Mathf.RoundToInt(timeBonus * mul)}",
                    $"无伤奖励    {(noHit ? "达成" : "未达成")}  +{Mathf.RoundToInt(noHitBonus * mul)}",
                    $"擦弹奖励    {root.Stats.SectorGraze} 次  +{Mathf.RoundToInt(grazeBonus * mul)}",
                });
                root.Stats.SectorGraze = 0;
                root.Stats.SectorHits = 0;
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
