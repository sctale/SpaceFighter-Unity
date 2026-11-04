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
        public float BulletSpeed => BulletSpeedNow();
        public int Phase { get; private set; }
        public string BossLabel => BossMoves.NameZh(_bossIndex);
        public string BossTitle => BossMoves.TitleZh(_bossIndex) + " // " + BossMoves.EnName(_bossIndex);
        public string BossEn => BossMoves.EnName(_bossIndex);
        /// <summary>判定半径：与 4.08 世界单位的机体视觉半径（≈1.88）对齐，取略小值。</summary>
        public float HitRadius => 1.55f;

        int _tier, _phase, _index, _zone, _bossIndex, _spellCount;
        float _breakMax = 100f, _bulletSpeedBase = 2.8f, _mercy = -1f, _idleCd;
        float _timer, _telegraph = -1f, _stun;
        Vector2 _anchor;
        SpriteRenderer _sr, _aura, _ring, _core, _flash;
        string _kind = "gatekeeper";
        float _flashT, _ringSpeed = 22f;
        Color _accent = new Color(1f, 0.45f, 0.35f);
        bool _dead;
        bool _desperate;
        Difficulty _diff;
        int _hitsAtSpawn;

        // ---- 招式状态机（原版 this.attack = {name, t, fired, emit, angle, data}）----
        BossMove _move;
        bool _casting;
        bool _lockMove;                 // MOVING_ATTACKS：blink / charge 期间机体由招式驱动
        float _moveT, _moveDur;         // 出招后的正时钟（预告期在 _telegraph 里走完）
        float _emitAcc, _emitAngle;     // 连续发射器（spiral / cascade / spiralDouble）
        System.Action<float> _emitter;  // 当前招式的每帧逻辑，null = 无
        string _castLabel = "";
        Color _tellColor = GameConfig.Teal;
        float _hoverT, _bobPhase;
        float _sizePx = 76f;            // 原版 def.size（画布像素）：判定与炮口都按它算

        // wallGap / cascade / charge 的招式私有状态
        readonly System.Collections.Generic.HashSet<int> _gapCols = new System.Collections.Generic.HashSet<int>();
        float _chargeX0, _chargeX1, _chargeY0, _chargeY1;
        bool _chargeDived, _chargeBraked;

        /// <summary>HUD 用：预告期显示「预告 · 招式名」，出招后去掉前缀但整招期间都保留。</summary>
        public string CastLabel => _castLabel;
        public Color TellColor => _tellColor;
        public bool Casting => _casting;
        public float TimerLeft => _timer;
        public float StunLeft => _stun;
        public float BreakLeft => BreakGauge;
        /// <summary>原版是椭圆判定：rx = size×1.02 + 弹半径，ry = size×0.82 + 弹半径。</summary>
        public float HitRx => _sizePx * GameConfig.BossHitRx * GameConfig.Px;
        public float HitRy => _sizePx * GameConfig.BossHitRy * GameConfig.Px;
        /// <summary>接触椭圆的两个半轴（size×0.85+8 / size×0.7+8）。</summary>
        public float ContactRx => (_sizePx * GameConfig.BossContactRx + GameConfig.BossContactPadPx) * GameConfig.Px;
        public float ContactRy => (_sizePx * GameConfig.BossContactRy + GameConfig.BossContactPadPx) * GameConfig.Px;

        public void Setup(int zone, Difficulty diff)
        {
            _diff = diff;
            _zone = zone;
            _tier = BossMoves.TierForZone(zone);
            _bossIndex = BossMoves.BossForZone(zone);
            // 原版：def.hp × (1 + min(3.4, (s-1)×0.70)) × 难度
            MaxHp = Hp = GameConfig.BossBaseHp(_bossIndex) * GameConfig.BossHpScale(zone) * diff.BossHp;
            _breakMax = Mathf.Max(GameConfig.BossBreakFloor, MaxHp * GameConfig.BossBreakFracOfMaxHp);
            // 弹速基准 = (152 + 档×46) × 难度；阶段系数 1.00/1.06/1.18 在每次发射时再乘
            // （原版 bulletSpeed() 是实时算的，之前只在出生时算一次，三阶段弹幕反而变慢）
            _bulletSpeedBase = (GameConfig.BossBulletSpeedBase + _tier * GameConfig.BossBulletSpeedPerTier)
                               * diff.BossBullet * GameConfig.Px;
            _sizePx = BossMoves.SizePx[_bossIndex];
            _bobPhase = Random.value * Mathf.PI * 2f;
            _hoverT = 0f;
            _spellCount = 0;
            _desperate = false;
            _castLabel = "";
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

            _timer = GameConfig.BossInitialCd;
            GameRoot.Instance?.Announce(BossLabel, BossTitle, 2.4f, true);
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
                // 破防结束的那一下要立刻补一招（entities.js:1920-1926）。
                // 注意这里**不能 return**：原版的 broken 只做「取消当前招 + 受伤 ×1.35」，
                // 机体照常悬停、出招计时照常走。早退会让 Boss 每 4 秒静默 1.2 秒，
                // 玩家看到的就是「它根本不还手」。
                if (_stun <= 0f) _timer = Mathf.Min(_timer, GameConfig.BossPostBreakCd);
            }

            if (_mercy > 0f) _mercy -= dt;

            float frac = Hp / Mathf.Max(1f, MaxHp);
            int wantPhase = frac > GameConfig.BossPhaseHigh ? 0
                          : frac > GameConfig.BossPhaseLow ? 1 : 2;
            if (wantPhase != _phase) { EnterPhase(wantPhase); return; }

            // 临界（entities.js:1903-1910）：三阶段 + 血量 <12 %，一次性触发，之后几乎不出停顿
            if (!_desperate && _phase == 2 && frac < GameConfig.BossDesperateFrac)
            {
                _desperate = true;
                _index = 0;
                _casting = false; _emitter = null; _lockMove = false; _castLabel = "";
                _pending.Clear(); BossLaser.ClearAll();
                _timer = GameConfig.BossDesperateCd;
                root.Hitstop(GameConfig.FreezeDesperate);
                root.Announce("OVERLOAD", "核心临界 · 招式不再停顿", 1.6f, true);
                AudioDirector.Instance?.PlayWarn();
            }

            if (_casting) { TickMove(dt); return; }

            if (_telegraph > 0f)
            {
                _telegraph -= dt;
                if (_telegraph <= 0f) BeginMove(CurrentMove);
                return;
            }

            _timer -= dt;
            IdleFire(dt);
            Hover(dt);
            if (_timer <= 0f)
            {
                CurrentMove = BossMoves.Pick(_bossIndex, _phase, _index++);
                _spellCount++;
                _telegraph = BossMoves.TelegraphTime;
                _castLabel = "预告 · " + BossMoves.Name(CurrentMove);
                _tellColor = BossMoves.Tell(CurrentMove);
                AudioDirector.Instance?.PlayWarn();
                Impact.Spawn(transform.position, _tellColor, _sizePx * 2.6f * GameConfig.Px, 0.36f);
            }
        }

        /// <summary>
        /// 悬停：三阶段摆幅更大、频率更快（entities.js:1913-1918），
        /// 出招期间（blink/charge）由招式自己接管位置。
        /// </summary>
        void Hover(float dt)
        {
            if (_lockMove) return;
            _hoverT += dt;
            float sway = _phase == 2 ? 0.27f : _phase == 1 ? 0.225f : 0.19f;
            float rate = 0.55f + _phase * 0.08f;
            Vector2 q = transform.position;
            float halfW = GameConfig.RefW * 0.5f;
            q.x = Xw(halfW + Mathf.Sin(_hoverT * rate + _bobPhase) * GameConfig.RefW * sway);
            q.y = _anchor.y + Mathf.Sin(_hoverT * 1.1f + _bobPhase) * (_phase == 2 ? 12f : 7f) * GameConfig.Px;
            transform.position = q;
        }

        void EnterPhase(int want)
        {
            _phase = want;
            _index = 0;
            _spellCount = 0;
            // 原版换阶段会把破防槽和破防状态一起清零，并打断当前招式
            BreakGauge = 0f;
            _stun = 0f;
            _casting = false; _emitter = null; _lockMove = false; _castLabel = "";
            _pending.Clear();
            BossLaser.ClearAll();
            GameRoot.Instance?.ClearEnemyBullets(true);
            TriggerShockwave();
            GameRoot.Instance?.Hitstop(GameConfig.FreezePhase);
            GameRoot.Instance?.Announce("PHASE " + (_phase + 1), "核心过载 · 弹幕强化", 1.4f, true);
            _mercy = GameConfig.BossMercyWindow;
            _timer = GameConfig.BossMercyWindow;
            _telegraph = -1f;
            GameRoot.Instance?.Player?.GrantInvulnBossPhase();
        }

        /// <summary>
        /// 出招间隔（纯函数，门禁可不进游戏直接算，entities.js:2443-2446）：
        /// 一/二/三阶段 0.85 / 0.62 / 0.42 秒，三阶段每 3 招（一档密度每 4 招）
        /// 连锁一次压到 0.16 秒，临界则每招都连锁。
        /// 原版的难度系数只作用于弹速、不作用出招间隔——之前把 BossBullet 乘进
        /// 间隔里，标准难度下 Boss 出手比原版慢一倍还多。
        /// </summary>
        public static float SpellCooldown(int phase, int tier, int spellCount, bool desperate)
        {
            int every = tier == 0 ? GameConfig.BossChainEveryTier0 : GameConfig.BossChainEveryOtherTier;
            bool chain = phase == 2 && (desperate || spellCount % every == 0);
            if (chain) return GameConfig.BossChainCd;
            return Mathf.Max(GameConfig.BossSpellCdFloor, GameConfig.BossSpellCd[Mathf.Clamp(phase, 0, 2)]);
        }

        float NextCd() => SpellCooldown(_phase, _tier, _spellCount, _desperate);

        /// <summary>
        /// 破防槽是否累积。原版是 `if (broken <= 0) stagger += dmg`：破防窗口内不攒槽，
        /// 否则槽一直满着，眩晕结束第一发又触发下一轮，持续输出能把 Boss 永久锁住。
        /// </summary>
        public static bool BreakGaugeAccrues(bool stunned, bool broken) => !stunned && !broken;

        // ------------------------------------------------------------ 招式内的分时发射
        /// <summary>
        /// 原版一招里是分时刻发好几段的（this.at(a, 0.1, ...) / at(a, 1.25, ...)），
        /// 之前 Unity 版把整招压成同一帧，光束和补刀弹幕糊在一起，读不出招。
        /// 这里补一个最小的分时队列。
        /// </summary>
        class Pending { public float at; public float t; public System.Action act; public bool done; }
        readonly List<Pending> _pending = new List<Pending>();

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
        /// 二阶段起 0.95s 一枚针，三阶段 0.66s 两枚（±0.22rad 夹射、弹速 ×0.92、r5）。
        /// mercy 只压住这一段，不会冻结机体与出招时钟（entities.js:1897 注释）。
        /// </summary>
        void IdleFire(float dt)
        {
            if (_mercy > 0f) return;
            if (_phase < 1) return;
            _idleCd -= dt;
            if (_idleCd > 0f) return;
            int n = _phase == 2 ? 2 : 1;
            float a = AimRad(), spd = BulletSpeedNow() * 0.92f;
            for (int i = 0; i < n; i++)
                Bullet(Muzzle(), a + (i - (n - 1) * 0.5f) * 0.22f, spd, "needle", 5f);
            _idleCd = _phase == 2 ? GameConfig.BossIdleFirePhase2 : GameConfig.BossIdleFirePhase1;
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

        // ==================================================================
        // 招式状态机（原版 this.attack = {name, t, fired, emit, angle, data}）
        // 预告 0.5 秒 → 出招：离散节拍用 At(相对时刻) 注册，连续弹幕用 _emitter
        // → 按阶段间隔收招。之前是一帧把整招全放完，读不出招也背不下板。
        // ==================================================================

        /// <summary>弹速 = 基准 × 阶段系数 1.00/1.06/1.18（entities.js:1974-1977）。</summary>
        float BulletSpeedNow() => _bulletSpeedBase * GameConfig.BossBulletSpeedDiv[Mathf.Clamp(_phase, 0, 2)];

        /// <summary>画布 x（0..540）→ 世界 x。</summary>
        static float Xw(float px) => (px - GameConfig.RefW * 0.5f) * GameConfig.Px;

        static float Damp(float a, float b, float k, float dt) => b + (a - b) * Mathf.Exp(-k * dt);

        /// <summary>炮口在机体下方 size×0.35（entities.js:1982）。</summary>
        Vector2 Muzzle() => (Vector2)transform.position + Vector2.down * (_sizePx * 0.35f * GameConfig.Px);

        /// <summary>指向玩家的角度（弧度）；Unity y 向上，与原版 atan2 同式。</summary>
        float AimRad()
        {
            var pl = GameRoot.Instance != null ? GameRoot.Instance.Player : null;
            if (pl == null) return -Mathf.PI * 0.5f;
            Vector2 d = (Vector2)pl.transform.position - (Vector2)transform.position;
            return Mathf.Atan2(d.y, d.x);
        }

        void BeginMove(BossMove m)
        {
            _move = m;
            _casting = true;
            _moveT = 0f;
            _moveDur = BossMoves.Duration(m);
            _lockMove = BossMoves.LocksMove(m);
            _castLabel = BossMoves.Name(m);
            _emitAcc = 0f;
            _emitAngle = Random.value * Mathf.PI * 2f;
            _chargeDived = false;
            _chargeBraked = false;
            _gapCols.Clear();
            _emitter = null;
            Impact.Spawn(transform.position, _tellColor, _sizePx * 1.6f * GameConfig.Px, 0.3f);
            BuildBeats(m);
        }

        void TickMove(float dt)
        {
            _moveT += dt;
            if (_emitter != null) _emitter(dt);
            if (_move == BossMove.Charge) TickChargeBody(dt);
            if (_moveT >= _moveDur) EndMove();
        }

        void EndMove()
        {
            _casting = false;
            _emitter = null;
            _lockMove = false;
            _castLabel = "";
            if (_move == BossMove.Blink)
                Impact.Spawn(transform.position, _accent, _sizePx * 2.4f * GameConfig.Px, 0.34f);

            // 三阶段收招后有概率白送一圈环弹（entities.js:2446-2448）
            if (_phase == 2 && Random.value < FreeRingChance())
            {
                int n = _desperate ? GameConfig.BossFreeRingDesperate : GameConfig.BossFreeRingNormal;
                RingAt(AimRad(), n, BulletSpeedNow() * GameConfig.BossFreeRingSpeedMul, "orb", 6f);
            }
            _timer = NextCd();
        }

        float FreeRingChance()
        {
            if (_desperate) return _tier == 0 ? GameConfig.BossDesperateRingHard : GameConfig.BossDesperateRingHardest;
            return _tier == 0 ? GameConfig.BossDesperateRingNormal : GameConfig.BossDesperateRingOtherTier;
        }

        /// <summary>开发期：-laser 直接指定一招，用来给新加的实体（光束）与分节拍拍验收图。</summary>
        public void DebugCast(BossMove m)
        {
            _phase = Mathf.Clamp(_phase, 0, 2);
            BeginMove(m);
        }

        // ---------------------------------------------------------------- 22 招
        // 逐招对照 entities.js:2076-2432。数值一律照抄：弹数按阶段、角度是弧度、
        // 光束给 warn/active/width/sweep、水雷给固定 px 速度 + 引信 + 破片数。

        void BuildBeats(BossMove m)
        {
            int p = _phase + 1;                       // 原版的 phase 是 1..3
            float sp = BulletSpeedNow();
            float aim = AimRad();

            switch (m)
            {
                case BossMove.Fan:
                {
                    int n = p == 3 ? 9 : p == 2 ? 7 : 5;
                    At(0.10f, () => FanAt(AimRad(), n, 0.17f, BulletSpeedNow()));
                    At(0.50f, () => FanAt(AimRad(), n, 0.12f, BulletSpeedNow()));
                    At(0.90f, () => FanAt(AimRad(), n + 2, 0.20f, BulletSpeedNow()));
                    At(1.18f, () => RingAt(AimRad(), p >= 2 ? 12 : 8, BulletSpeedNow() * 0.58f));
                    break;
                }
                case BossMove.Spiral:
                {
                    float interval = p == 3 ? 0.052f : p == 2 ? 0.062f : 0.082f;
                    int arms = p == 3 ? 5 : p == 2 ? 4 : 3;
                    _emitter = d =>
                    {
                        _emitAcc += d;
                        while (_emitAcc >= interval)
                        {
                            _emitAcc -= interval;
                            float spd = BulletSpeedNow() * 0.72f;
                            for (int i = 0; i < arms; i++)
                                Bullet(Muzzle(), _emitAngle + i * Mathf.PI * 2f / arms, spd, "orb", 6f);
                            _emitAngle += 0.24f;
                        }
                    };
                    break;
                }
                case BossMove.Ring:
                {
                    float off = Random.value * Mathf.PI * 2f;
                    At(0.12f, () => RingAt(off, p >= 2 ? 28 : 22, BulletSpeedNow() * 0.8f));
                    At(0.55f, () => RingAt(0.22f, p >= 3 ? 30 : 24, BulletSpeedNow() * 0.58f));
                    At(0.86f, () => FanAt(AimRad(), p >= 2 ? 5 : 3, 0.26f, BulletSpeedNow() * 1.12f));
                    break;
                }
                case BossMove.Homing:
                {
                    At(0.15f, () => Seekers(p >= 2 ? 5 : 3));
                    if (p >= 2) At(0.70f, () => Seekers(3));
                    break;
                }
                case BossMove.Sweep:
                {
                    At(0.10f, () => { float a = AimRad(); Beam(a - 0.55f, 0.9f, 0.9f, 16f, 0.9f); });
                    At(1.25f, () => { float a = AimRad(); Beam(a + 0.55f, 0.8f, 0.85f, 14f, -0.95f); });
                    At(2.15f, () => FanAt(AimRad(), p >= 2 ? 5 : 3, 0.24f, BulletSpeedNow()));
                    break;
                }
                case BossMove.Cross:
                {
                    float base0 = Random.value * 0.6f;
                    At(0.15f, () =>
                    {
                        for (int i = 0; i < 4; i++)
                            Beam(base0 + i * Mathf.PI * 0.5f, 0.95f, 0.62f, 12f, 0f);
                    });
                    At(1.05f, () => RingAt(AimRad(), p >= 2 ? 14 : 10, BulletSpeedNow() * 0.7f));
                    break;
                }
                case BossMove.Summon:
                {
                    At(0.20f, () =>
                    {
                        int n = p == 3 ? 4 : p == 2 ? 3 : 2;
                        // 召唤表随星区升级（entities.js:2128-2140）
                        EnemyKind[] roster = _zone >= 4 ? new[] { EnemyKind.Sniper, EnemyKind.Sidewinder, EnemyKind.Shardling }
                                             : _zone >= 2 ? new[] { EnemyKind.Shardling, EnemyKind.Wasp }
                                             : new[] { EnemyKind.Wasp };
                        for (int i = 0; i < n; i++)
                        {
                            float x = Xw(transform.position.x / GameConfig.Px + GameConfig.RefW * 0.5f
                                         + (i - (n - 1) * 0.5f) * _sizePx * 1.5f);
                            float y = transform.position.y - _sizePx * 0.8f * GameConfig.Px;
                            Director.Instance?.SpawnEscort(roster[i % roster.Length], new Vector2(x, y));
                        }
                        AudioDirector.Instance?.PlaySwitch();
                    });
                    break;
                }
                case BossMove.Barrage:
                {
                    At(0.12f, () =>
                    {
                        float a = AimRad(), spd = BulletSpeedNow();
                        for (int i = 0; i < 5; i++)
                            Bullet(Muzzle(), a + (i - 2) * 0.23f, spd * 0.62f, "heavy", 10f);
                        if (p >= 2)
                        {
                            Bullet(Muzzle(), a - 0.34f, spd * 0.4f, "plasma", 13f, 3.4f, 0f, 0f, 7);
                            Bullet(Muzzle(), a + 0.34f, spd * 0.4f, "plasma", 13f, 3.4f, 0f, 0f, 7);
                        }
                    });
                    At(0.70f, () =>
                    {
                        float spd = BulletSpeedNow();
                        for (int i = 0; i < 12; i++)
                            Bullet(Muzzle(), i * Mathf.PI * 2f / 12f + 0.3f, spd * 0.72f, "shard", 6f);
                    });
                    At(1.20f, () => FanAt(AimRad(), 5, 0.20f, BulletSpeedNow()));
                    At(1.68f, () => RingAt(AimRad(), p >= 2 ? 18 : 12, BulletSpeedNow() * 0.9f));
                    break;
                }
                case BossMove.TwinBeam:
                {
                    At(0.12f, () =>
                    {
                        float a = AimRad();
                        Beam(a - 0.65f, 0.95f, 0.9f, 14f, -0.6f);
                        Beam(a + 0.65f, 0.95f, 0.9f, 14f, 0.6f);
                    });
                    At(1.10f, () =>
                    {
                        float a = AimRad(), spd = BulletSpeedNow();
                        int n = p >= 2 ? 5 : 3;
                        for (int i = 0; i < n; i++)
                            Bullet(Muzzle(), a + (i - (n - 1) * 0.5f) * 0.18f, spd * 1.05f, "needle", 5f);
                    });
                    At(1.75f, () => RingAt(_emitAngle, p == 3 ? 16 : 10, BulletSpeedNow() * 0.66f));
                    break;
                }
                case BossMove.MineCurtain:
                {
                    At(0.10f, () =>
                    {
                        float a = AimRad();
                        for (int i = 0; i < 5; i++) MineAt(a + (i - 2) * 0.26f, 98f, 9f, 1.7f, 9);
                        AudioDirector.Instance?.PlayHit();
                    });
                    At(0.85f, () =>
                    {
                        int n = p >= 2 ? 7 : 5;
                        // 原版这一拍是朝正下方（画布 π/2）撒一排，Unity 的"下"是 -π/2
                        for (int i = 0; i < n; i++)
                            MineAt(-Mathf.PI * 0.5f + (i - (n - 1) * 0.5f) * 0.30f, 82f, 8f, 2.1f, 8);
                        Impact.Spawn(transform.position, GameConfig.Gold, 150f * GameConfig.Px, 0.3f);
                    });
                    if (p >= 2) At(1.50f, () => FanAt(AimRad(), 3, 0.42f, BulletSpeedNow() * 1.05f));
                    break;
                }
                case BossMove.Blink:
                {
                    // 原版 targetY ∓ 18/26/8 是画布坐标（y 向下），Unity 里"抬到驻留点上方"是加
                    At(0.15f, () => Warp(540f * 0.24f, _anchor.y + 18f * GameConfig.Px, 12));
                    At(0.82f, () => Warp(540f * 0.74f, _anchor.y - 26f * GameConfig.Px, 14));
                    At(1.50f, () => Warp(540f * 0.50f, _anchor.y + 8f * GameConfig.Px, 18));
                    break;
                }
                case BossMove.LaserWeb:
                {
                    At(0.12f, () =>
                    {
                        float base0 = AimRad() - 0.6f;
                        for (int i = 0; i < 4; i++)
                            Beam(base0 + i * Mathf.PI * 0.5f, 0.85f, 0.7f, 11f, (i % 2 == 1 ? -0.6f : 0.6f));
                    });
                    At(1.05f, () =>
                    {
                        float a = AimRad();
                        Beam(a - 0.5f, 0.7f, 0.6f, 10f, 0f);
                        Beam(a + 0.5f, 0.7f, 0.6f, 10f, 0f);
                        Impact.Spawn(transform.position, GameConfig.Rose, 130f * GameConfig.Px, 0.28f);
                    });
                    At(1.85f, () => RingAt(_emitAngle, p >= 2 ? 16 : 10, BulletSpeedNow() * 0.82f));
                    break;
                }
                case BossMove.WaveCurtain:
                {
                    At(0.10f, () =>
                    {
                        float a = AimRad(), spd = BulletSpeedNow();
                        for (int i = 0; i < 9; i++)
                            Bullet(Muzzle(), a + (i - 4) * 0.17f, spd * 0.72f, "wave", 7f, 10f, 2.1f);
                    });
                    At(0.95f, () =>
                    {
                        float a = AimRad(), spd = BulletSpeedNow();
                        for (int i = 0; i < 7; i++)
                            Bullet(Muzzle(), a + (i - 3) * 0.20f, spd * 0.62f, "wave", 6f, 10f, -2.4f);
                    });
                    if (p >= 2) At(1.62f, () =>
                    {
                        float a = AimRad(), spd = BulletSpeedNow();
                        for (int i = 0; i < 5; i++)
                            Bullet(Muzzle(), a + (i - 2) * 0.24f, spd * 0.9f, "wave", 6f, 10f, 1.6f);
                    });
                    break;
                }
                case BossMove.WallGap:
                {
                    // 原版 cols = max(9, round(W/40))、缺口 2~4 列，先用 4 个青环标出口子
                    int cols = Mathf.Max(9, Mathf.RoundToInt(GameConfig.RefW / 40f));
                    int gap = p == 3 ? (_tier == 0 ? 3 : 2) : (_tier == 0 ? 4 : 3);
                    float step = GameConfig.RefW / cols;
                    System.Action plan = () =>
                    {
                        int gapIdx = Random.Range(0, cols - gap + 1);
                        _gapCols.Clear();
                        for (int i = 0; i < gap; i++) _gapCols.Add(gapIdx + i);
                        float gx = (gapIdx + gap * 0.5f) * step;
                        for (int i = 0; i < 4; i++)
                            Impact.Spawn(new Vector3(Xw(gx), transform.position.y - (30f * i + 40f) * GameConfig.Px, 0f),
                                         GameConfig.Teal, 26f * GameConfig.Px, 0.45f);
                        AudioDirector.Instance?.PlayWarn();
                    };
                    System.Action fire = () =>
                    {
                        float spd = BulletSpeedNow() * 0.55f;
                        Vector2 from = (Vector2)transform.position + Vector2.down * (_sizePx * 0.5f * GameConfig.Px);
                        for (int c = 0; c < cols; c++)
                        {
                            if (_gapCols.Contains(c)) continue;
                            float x = Xw(Mathf.Clamp((c + 0.5f) * step, 10f, GameConfig.RefW - 10f));
                            Bullet(from + new Vector2(x, 0f), -Mathf.PI * 0.5f, spd, "needle", 5f);
                        }
                    };
                    At(0.10f, plan);
                    At(0.62f, () => { fire(); FanAt(AimRad(), 3, 0.5f, BulletSpeedNow() * 0.9f); });
                    At(1.40f, plan);
                    if (p >= 2) At(2.30f, plan);
                    At(2.70f, () => { fire(); RingAt(AimRad(), p == 3 ? 14 : 8, BulletSpeedNow() * 0.85f); });
                    break;
                }
                case BossMove.Pulsar:
                {
                    // 三层不同速度的环互相超越，是这招的读法（entities.js:2252 注释）
                    At(0.12f, () => { RingAt(_emitAngle, p >= 2 ? 18 : 14, BulletSpeedNow() * 1.12f, "shard", 6f);
                                      Impact.Spawn(transform.position, _accent, _sizePx * 2.2f * GameConfig.Px, 0.3f); });
                    At(0.70f, () => { RingAt(_emitAngle + 0.16f, p >= 2 ? 24 : 18, BulletSpeedNow() * 0.60f, "orb", 6f);
                                      Impact.Spawn(transform.position, _accent, _sizePx * 2.8f * GameConfig.Px, 0.32f); });
                    At(1.28f, () => { RingAt(_emitAngle + 0.30f, p == 3 ? 30 : 24, BulletSpeedNow() * 0.86f, "shard", 6f);
                                      Impact.Spawn(transform.position, _accent, GameConfig.RefW * 0.8f * GameConfig.Px, 0.36f); });
                    At(1.80f, () => FanAt(AimRad(), p >= 2 ? 5 : 3, 0.30f, BulletSpeedNow() * 1.18f));
                    break;
                }
                case BossMove.Charge:
                {
                    var pl = GameRoot.Instance != null ? GameRoot.Instance.Player : null;
                    float hPx = 2f * GameView.HalfHeight / GameConfig.Px;      // 实时画布高度（像素）
                    _chargeX0 = transform.position.x;
                    _chargeX1 = pl != null ? pl.transform.position.x : 0f;
                    _chargeY0 = _anchor.y + 30f * GameConfig.Px;               // 原版 targetY - 30：先往上抬
                    // 原版 y1 = min(H×0.74, targetY + H×0.42)（画布 y 向下），换成 Unity 就是取更大的 y
                    _chargeY1 = Mathf.Max(GameView.FromTopPx(hPx * 0.74f),
                                          _anchor.y - hPx * 0.42f * GameConfig.Px);
                    At(0.90f, () => FanAt(AimRad(), 3, 0.5f, BulletSpeedNow() * 0.85f));
                    At(1.50f, () =>
                    {
                        RingAt(_emitAngle, p >= 2 ? 20 : 14, BulletSpeedNow() * 0.78f);
                        FanAt(AimRad(), 5, 0.24f, BulletSpeedNow() * 0.95f);
                        CameraShake.Instance?.Shake(0.22f, 0.3f);
                        Impact.Spawn(transform.position, GameConfig.Rose, GameConfig.RefW * GameConfig.Px, 0.4f);
                        AudioDirector.Instance?.PlayExplode();
                    });
                    break;
                }
                case BossMove.CrossSweep:
                {
                    At(0.15f, () =>
                    {
                        float a = AimRad();
                        for (int i = 0; i < 4; i++)
                            Beam(a + i * Mathf.PI * 0.5f, 1.0f, 1.35f, 13f, (i % 2 == 1 ? -1f : 1f) * 0.72f);
                    });
                    At(1.00f, () => RingAt(_emitAngle, p >= 2 ? 16 : 12, BulletSpeedNow() * 0.62f));
                    At(1.62f, () =>
                    {
                        float a = AimRad() + Mathf.PI * 0.25f;
                        for (int i = 0; i < 4; i++)
                            Beam(a + i * Mathf.PI * 0.5f, 0.72f, 1.05f, 11f, (i % 2 == 1 ? 1f : -1f) * 0.85f);
                    });
                    At(2.40f, () => FanAt(AimRad(), p == 3 ? 7 : 5, 0.22f, BulletSpeedNow() * 1.05f));
                    break;
                }
                case BossMove.Cascade:
                {
                    // 7 列陨石雨留一条会走的安全道，三层弹速互相穿插
                    float interval = p == 3 ? 0.095f : p == 2 ? 0.105f : 0.120f;
                    int skip = Random.Range(0, 7);
                    _emitAcc = 0f;
                    At(0.20f, () => AudioDirector.Instance?.PlayHit());
                    At(1.50f, () =>
                    {
                        RingAt(AimRad(), p >= 2 ? 14 : 10, BulletSpeedNow() * 0.52f);
                        FanAt(AimRad(), 3, 0.42f, BulletSpeedNow() * 1.1f);
                    });
                    _emitter = d =>
                    {
                        _emitAcc += d;
                        while (_emitAcc >= interval)
                        {
                            _emitAcc -= interval;
                            float sp2 = BulletSpeedNow();
                            float stepPx = GameConfig.RefW / 7f;
                            int extra = p == 3 ? 1 : 0;
                            for (int col = 0; col < 7; col++)
                            {
                                if (col % 7 == skip || (extra == 1 && col % 7 == (skip + 1) % 7)) continue;
                                float xPx = (col + 0.5f) * stepPx + Random.Range(-9f, 9f);
                                Vector2 at = new Vector2(Xw(xPx), GameView.HalfHeight + 22f * GameConfig.Px);
                                float ang = -Mathf.PI * 0.5f + Random.Range(-0.1f, 0.1f);
                                float spd = sp2 * (0.6f + (col % 3) * 0.17f);
                                Bullet(at, ang, spd, "shard", 5f);
                            }
                        }
                    };
                    break;
                }
                case BossMove.HomingBarrage:
                {
                    At(0.15f, () => Seekers(3));
                    At(0.95f, () => Seekers(3));
                    At(1.75f, () => Seekers(p >= 2 ? 4 : 3));
                    if (p == 3) At(2.40f, () => Seekers(3));
                    break;
                }
                case BossMove.SpiralDouble:
                {
                    float interval = p >= 2 ? 0.075f : 0.090f;
                    _emitter = d =>
                    {
                        _emitAcc += d;
                        while (_emitAcc >= interval)
                        {
                            _emitAcc -= interval;
                            float spd = BulletSpeedNow();
                            for (int i = 0; i < 2; i++)
                            {
                                Bullet(Muzzle(), _emitAngle + i * Mathf.PI, spd * 0.66f, "orb", 6f);
                                Bullet(Muzzle(), -_emitAngle + i * Mathf.PI, spd * 0.82f, "shard", 6f);
                            }
                            _emitAngle += 0.2f;
                        }
                    };
                    At(1.25f, () => FanAt(AimRad(), p >= 2 ? 5 : 3, 0.30f, BulletSpeedNow() * 1.05f));
                    break;
                }
                case BossMove.MineRing:
                {
                    At(0.15f, () =>
                    {
                        int n = p >= 2 ? 12 : 9;
                        for (int i = 0; i < n; i++)
                            MineAt(i * Mathf.PI * 2f / n + _emitAngle, 122f, 8f, 1.5f, 7);
                        Impact.Spawn(transform.position, GameConfig.Gold, _sizePx * 2.4f * GameConfig.Px, 0.34f);
                    });
                    At(1.05f, () =>
                    {
                        int n = p >= 2 ? 8 : 6;
                        for (int i = 0; i < n; i++)
                            MineAt(i * Mathf.PI * 2f / n + _emitAngle + 0.4f, 70f, 8f, 1.1f, 6);
                        FanAt(AimRad(), 3, 0.42f, BulletSpeedNow());
                    });
                    break;
                }
                case BossMove.NovaBurst:
                {
                    At(0.90f, () =>
                    {
                        RingAt(_emitAngle, p >= 2 ? 22 : 18, BulletSpeedNow() * 1.15f, "shard", 6f);
                        CameraShake.Instance?.Shake(0.3f, 0.42f);
                        Impact.Spawn(transform.position, new Color(1f, 0.86f, 0.55f, 1f),
                                     GameConfig.RefW * 0.9f * GameConfig.Px, 0.5f);
                        AudioDirector.Instance?.PlayExplode();
                    });
                    At(1.45f, () =>
                    {
                        RingAt(_emitAngle + 0.12f, p == 3 ? 26 : 22, BulletSpeedNow() * 0.72f, "orb", 6f);
                        Impact.Spawn(transform.position, GameConfig.Gold, GameConfig.RefW * GameConfig.Px, 0.45f);
                    });
                    At(2.05f, () =>
                    {
                        RingAt(_emitAngle + 0.25f, p >= 2 ? 18 : 12, BulletSpeedNow() * 0.95f, "shard", 6f);
                        FanAt(AimRad(), p == 3 ? 7 : 5, 0.20f, BulletSpeedNow() * 1.1f);
                    });
                    break;
                }
            }
        }

        /// <summary>贯穿冲撞的四段机体运动（entities.js:2271-2316）。</summary>
        void TickChargeBody(float dt)
        {
            Vector2 q = transform.position;
            float limX = GameView.HalfWidth - _sizePx * 1.1f * GameConfig.Px;
            float x1 = Mathf.Clamp(_chargeX1, -limX, limX);

            if (_moveT < 0.9f)
            {
                q.y = Damp(q.y, _chargeY0, 4f, dt);
                q.x = Damp(q.x, x1, 1.3f, dt);
            }
            else if (_moveT < 1.5f)
            {
                float k = 1f - Mathf.Pow(1f - (_moveT - 0.9f) / 0.6f, 3f);      // easeOutCubic
                q.x = Mathf.Lerp(_chargeX0, x1, k);
                q.y = Mathf.Lerp(_chargeY0, _chargeY1, k);
                if (!_chargeDived) { _chargeDived = true; Impact.Spawn(q, _accent, _sizePx * 3f * GameConfig.Px, 0.4f); }
            }
            else if (_moveT < 2.2f)
            {
                q.y = Damp(q.y, _chargeY1 - 6f * GameConfig.Px, 5f, dt);
                q.x = Damp(q.x, x1, 2f, dt);
            }
            else
            {
                float k = (_moveT - 2.2f) / 0.95f;
                q.y = Mathf.Lerp(q.y, _anchor.y, Mathf.Clamp01(k));
                q.x = Damp(q.x, 0f, 2.2f, dt);
            }
            transform.position = q;
        }

        // ---------------------------------------------------------------- 发射原语

        /// <summary>扇形：以 center 为中心、n 发、间隔 spread 弧度。</summary>
        void FanAt(float center, int n, float spreadRad, float speed, string kind = "orb", float rPx = 6f)
        {
            for (int i = 0; i < n; i++)
                Bullet(Muzzle(), center + (i - (n - 1) * 0.5f) * spreadRad, speed, kind, rPx);
        }

        /// <summary>整圈：offset 起始、n 发均分。</summary>
        void RingAt(float offset, int n, float speed, string kind = "orb", float rPx = 6f)
        {
            for (int i = 0; i < n; i++)
                Bullet(Muzzle(), offset + i * Mathf.PI * 2f / n, speed, kind, rPx);
        }

        /// <summary>追踪星群：aim + (i-(n-1)/2)×0.42、速度 ×0.58、r7、追踪 2.6 秒、转速 1.9。</summary>
        void Seekers(int n)
        {
            float a = AimRad(), spd = BulletSpeedNow() * 0.58f;
            for (int i = 0; i < n; i++)
            {
                var p = Bullet(Muzzle(), a + (i - (n - 1) * 0.5f) * 0.42f, spd, "homing", 7f, 10f);
                p.Homing = true;
                p.HomingTime = 2.6f;
                p.TurnRate = 1.9f;
            }
            Impact.Spawn(transform.position, _accent, _sizePx * 1.8f * GameConfig.Px, 0.3f);
        }

        /// <summary>水雷：固定 px 初速 + 引信 + 破片数。</summary>
        void MineAt(float ang, float speedPx, float rPx, float fuse, int split)
        {
            var p = Bullet(Muzzle(), ang, speedPx * GameConfig.Px, "mine", rPx, 8f);
            p.Fuse = fuse;
            p.SplitOnExpire = split;
        }

        /// <summary>光束：warn 期几何冻结、只活期内扫（BossLaser 内部实现），碰撞宽度按视口缩放。</summary>
        void Beam(float ang, float warn, float active, float widthPx, float sweepRadPerSec)
        {
            BossLaser.Spawn(transform.position, ang, warn, active, widthPx, sweepRadPerSec, GameConfig.Rose, this);
            AudioDirector.Instance?.PlayWarn();
        }

        /// <summary>相位闪现：跳到指定位置并朝玩家放一圈环弹。</summary>
        void Warp(float xPx, float yWorld, int ringCount)
        {
            float hPx = 2f * GameView.HalfHeight / GameConfig.Px;
            // 原版夹在 x∈[size×1.25, W-size×1.25]、y∈[size×1.2, H×0.4]（画布 y 向下）
            float xMin = Xw(_sizePx * 1.25f), xMax = Xw(GameConfig.RefW - _sizePx * 1.25f);
            float yMax = GameView.FromTopPx(_sizePx * 1.2f);
            float yMin = GameView.FromTopPx(hPx * 0.4f);
            Vector2 q = transform.position;
            q.x = Mathf.Clamp(Xw(xPx), xMin, xMax);
            q.y = Mathf.Clamp(yWorld, yMin, yMax);
            Impact.Spawn(q, _accent, _sizePx * 2.2f * GameConfig.Px, 0.3f);
            transform.position = q;
            AudioDirector.Instance?.PlayHit();
            RingAt(AimRad(), ringCount, BulletSpeedNow() * 0.78f);
        }

        /// <summary>统一发射口：kind/r/life/wave/fuse/split/homing 都在这一个入口上补齐。</summary>
        Projectile Bullet(Vector2 pos, float angRad, float speed, string kind, float rPx,
                          float life = 10f, float wave = 0f, float fuse = 0f, int split = 0)
        {
            var vel = new Vector2(Mathf.Cos(angRad), Mathf.Sin(angRad)) * speed;
            var p = BulletPool.Instance.Spawn(Team.Enemy, pos, vel, 1f, rPx * GameConfig.Px,
                                              BulletColor(kind), kind, life);
            if (wave != 0f) p.Wave = wave;
            if (fuse > 0f) p.Fuse = fuse;
            if (split > 0) p.SplitOnExpire = split;
            return p;
        }

        /// <summary>原版弹体配色：针弹青、水雷/等离子金橙、重弹暖橙、其余跟机体主色。</summary>
        Color BulletColor(string kind)
        {
            switch (kind)
            {
                case "needle": return new Color(0.55f, 0.98f, 0.95f);
                case "mine": return new Color(1f, 0.85f, 0.35f);
                case "plasma": return new Color(1f, 0.6f, 0.3f);
                case "heavy": return new Color(1f, 0.86f, 0.55f);
                case "homing": return GameConfig.Violet;
                case "wave": return new Color(0.61f, 0.55f, 1f);
                default: return _accent;
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

        static Vector2 Rot(Vector2 v, float deg)
        {
            float rad = deg * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>入场（还没降到悬停点）期间不该吃伤害——原版 hit() 里 entering 直接 return。</summary>
        public bool Entering => transform.position.y > _anchor.y + 0.01f;

        /// <summary>结算入口的可受伤判定（纯函数，门禁可断言）。</summary>
        public static bool CanTakeDamage(bool entering, bool dying) => !entering && !dying;

        public void TakeDamage(float dmg)
        {
            if (!CanTakeDamage(Entering, _dead)) return;
            _flashT = 0.07f;
            float mul = _stun > 0f ? GameConfig.BossBreakDamageMul : 1f;
            Hp -= dmg * mul;
            // 破防期间不再攒槽（原版 if (broken <= 0)）。少了这一条，槽在眩晕里一直保持满值，
            // 眩晕一结束的第一发又立刻触发下一次眩晕 —— 持续输出就能把 Boss 永久锁在原地不出招。
            if (BreakGaugeAccrues(_stun > 0f, false)) BreakGauge = Mathf.Min(_breakMax, BreakGauge + dmg);
            if (BreakGauge >= _breakMax && _stun <= 0f)
            {
                BreakGauge = 0f;
                _stun = GameConfig.BossBreakStun;
                // 破防不再打断当前招式：原版会 cancel，但实测玩家持续输出时每 4 秒就打断一次，
                // 招式永远放不完，Boss 显得"不会还手"。这里保留伤害倍率与破防提示，让招式走完。
                GameRoot.Instance?.ClearEnemyBullets(false);
                GameRoot.Instance?.RegisterBreak();
                GameRoot.Instance?.Hitstop(GameConfig.FreezeHit);   // 原版破防钉 50ms
                GameRoot.Instance?.Announce("CORE BREAK", "核心破防 · 伤害窗口", 1.2f);
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
                // 原版结算：基础 + 时间 + 无伤 + 擦弹，整体再乘难度倍率。
                // 时间按「整个星区」的用时（runTime - 星区开始），不是只算 Boss 战那几十秒。
                int sector = Director.Instance != null ? Director.Instance.Zone : 1;
                float sectorStart = Director.Instance != null ? Director.Instance.SectorStartTime : 0f;
                float clearT = Mathf.Max(0f, root.Stats.Time - sectorStart);
                float timeBonus = Mathf.Max(0f, (GameConfig.ClearTimeBonusBase - clearT) * GameConfig.ClearTimeBonusPerSecond);
                bool noHit = root.Stats.SectorHits == 0;
                float noHitBonus = noHit ? GameConfig.ClearNoHitBase + sector * GameConfig.ClearNoHitPerSector : 0f;
                float grazeBonus = root.Stats.SectorGraze * GameConfig.ClearGrazePerHit;
                float mul = root.Difficulty.ScoreMul;
                float baseScore = GameConfig.ClearBaseScore + sector * GameConfig.ClearBasePerSector;
                // Unity 独有增强：星区补偿（原版没有这项）。以前它被偷偷再加一次分，
                // 现在显式列在结算里，看得见也就说得清它比原版多给了多少。
                float compensation = GameConfig.SectorCompensationPerZone * (sector + 1);
                int total = Mathf.RoundToInt((baseScore + timeBonus + noHitBonus + grazeBonus + compensation) * mul);
                root.AddScore(total);
                // 原版崩解：1.4 秒 0.35 倍速 + 120ms 停顿（game.js:1382-1383）
                root.Slowmo(GameConfig.SlowBossDieDur, GameConfig.SlowBossDieScale);
                root.Hitstop(GameConfig.FreezeBossDie);
                root.Announce("CORE BREACH", "巨构核心崩解 · +" + total, 1.8f);
                root.ShowClear(new[]
                {
                    $"基础分      {Mathf.RoundToInt(baseScore * mul)}",
                    $"时间奖励    {Mathf.RoundToInt(clearT)}s  +{Mathf.RoundToInt(timeBonus * mul)}",
                    $"无伤奖励    {(noHit ? "达成" : "未达成")}  +{Mathf.RoundToInt(noHitBonus * mul)}",
                    $"擦弹奖励    {root.Stats.SectorGraze} 次  +{Mathf.RoundToInt(grazeBonus * mul)}",
                    $"星区补偿    {sector + 1} 区  +{Mathf.RoundToInt(compensation * mul)}",
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
