using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 三条武器线 × 5 级。完全按网页版 entities.js 的经济模型：
    /// 开火掉蓄能 → 打空失谐（伤害 ×0.55、间隔 ×1.45、星爆 ×0.45）；
    /// 手上这把要排气到 100 才重新就绪，收纳中的回到 55 即恢复；
    /// 换线要「停满 1.2 秒 + 目标线蓄能 ≥55 且未失谐」才叠共鸣，否则清零。
    /// </summary>
    public class WeaponSystem : MonoBehaviour
    {
        public class LineState
        {
            public string Id;
            public int Level;
            public bool Unlocked;
            public float Charge = 100f;
            public bool Detuned;
        }

        public LineState Pulse = new LineState { Id = "pulse", Level = GameConfig.StartPulseLevel, Unlocked = true };
        public LineState Prism = new LineState { Id = "prism", Level = 1 };
        public LineState Swarm = new LineState { Id = "swarm", Level = 1 };

        public string ActiveId = "pulse";
        public int Level => Active.Level;
        public float Resonance { get; private set; }
        public float FireCooldown { get; private set; }
        public bool Precision;

        float _resTimer;
        PlayerShip _owner;
        Transform _mount;
        readonly LineState[] _all = new LineState[3];

        public LineState Active
        {
            get
            {
                foreach (var l in _all) if (l.Id == ActiveId) return l;
                return _all[0];
            }
        }

        public void Bind(PlayerShip owner, Transform mount)
        {
            _owner = owner;
            _mount = mount;
            _all[0] = Pulse; _all[1] = Prism; _all[2] = Swarm;
        }

        public int UnlockedCount()
        {
            int n = 0;
            foreach (var l in _all) if (l.Unlocked) n++;
            return n;
        }

        /// <summary>射击间隔（纯函数，门禁可不进游戏直接算）：等级曲线 × 超频 0.74 × 失谐 1.45。</summary>
        public static float Interval(string id, int level, bool overdrive, bool detuned)
        {
            var def = GameConfig.Line(id);
            int lv = Mathf.Clamp(level, 1, 5);
            return def.FireRate[lv - 1] * (overdrive ? GameConfig.OverdriveFireRateMul : 1f)
                                       * (detuned ? GameConfig.DetunedIntervalMul : 1f);
        }

        /// <summary>当前等级的射击间隔（秒）：等级曲线 × 超频 0.74 × 失谐 1.45。</summary>
        public float FireInterval() => Interval(ActiveId, Active.Level,
            _owner != null && _owner.Overdrive > 0f, Active.Detuned);

        /// <summary>
        /// 每枪蓄能开销（纯函数）：drain × 本枪间隔 ×（只有一条线时 0.55）。
        /// 要害在于「按枪扣」，所以每秒掉电量正好等于 drain；一旦写成按帧扣，
        /// 60fps 下 pulse LV1 会从 7.4/s 变成 ~53/s，两秒打空、长期失谐。
        /// </summary>
        public static float ChargeCost(string id, int level, int unlockedCount, bool overdrive, bool detuned)
            => GameConfig.Line(id).Drain * Interval(id, level, overdrive, detuned)
               * (unlockedCount < 2 ? GameConfig.SoloDrainMul : 1f);

        /// <summary>共鸣只在「停够 1.2 秒 + 目标线蓄能 ≥55 且未失谐」时叠一层。</summary>
        public static bool ResonanceGain(bool wellTimed, float targetCharge, bool targetDetuned)
            => wellTimed && targetCharge >= GameConfig.DetuneClearCharge && !targetDetuned;

        public float DamageMul()
        {
            float res = 1f + Resonance * GameConfig.ResonancePerStackDamage;
            float det = Active.Detuned ? GameConfig.DetunedDamageMul : 1f;
            float od = _owner != null && _owner.Overdrive > 0f ? GameConfig.OverdriveDamageMul : 1f;
            return res * det * od;
        }

        int Calibrate(float baseDmg) => Mathf.Max(1, Mathf.RoundToInt(baseDmg * DamageMul()));

        public void Tick(float dt)
        {
            foreach (var l in _all)
            {
                if (!l.Unlocked) continue;
                var def = GameConfig.Line(l.Id);
                if (l.Id != ActiveId)
                {
                    // 收纳中：按本线回充速度补，回到 55 就重新可用
                    l.Charge = Mathf.Min(100f, l.Charge + def.Recover * dt);
                    if (l.Detuned && l.Charge >= GameConfig.DetuneClearCharge) l.Detuned = false;
                }
                else if (l.Detuned)
                {
                    // 手上这把打空了：一路排气到 100 才重新就绪，避免 0→30 的锯齿
                    l.Charge = Mathf.Min(GameConfig.DetuneRearmCharge,
                                          l.Charge + def.Drain * GameConfig.VentRatio * dt);
                    if (l.Charge >= GameConfig.DetuneRearmCharge) l.Detuned = false;
                }
            }

            // 蓄能只在「开出一枪」时扣（原版 entities.js:965 在 shoot() 里调 spendCharge）。
            // 这里以前每帧扣一次，等于按帧率把 drain 放大了 60 倍：
            // pulse LV1 应该是 7.4/s，实际 ~53/s，两秒就打空并卡在失谐里，伤害长期 ×0.55。

            if (_resTimer > 0f)
            {
                _resTimer -= dt;
                if (_resTimer <= 0f)
                {
                    Resonance = Mathf.Max(0f, Resonance - 1f);
                    if (Resonance > 0f) _resTimer = GameConfig.ResonanceWindow;
                }
            }

            FireCooldown -= dt;
        }

        void SpendCharge()
        {
            var w = Active;
            // 排气中不再扣蓄能，否则会被一直钉在 0（原版 entities.js:504）
            if (w.Detuned) return;
            w.Charge = Mathf.Max(0f, w.Charge - ChargeCost(w.Id, w.Level, UnlockedCount(),
                _owner != null && _owner.Overdrive > 0f, false));
            if (w.Charge <= 0.5f) w.Detuned = true;
        }

        /// <summary>
        /// 原版 autofire 只看 fireTimer 与 alive（entities.js:788-792），不看蓄能：
        /// 打空的线照样开火，代价走 fireRate 的 ×1.45 慢循环与 damageMul 的 ×0.55。
        /// 之前这里有一道 Charge &lt;= 0 就 return 的门，失谐期间直接停火，等于凭空多一层惩罚。
        /// </summary>
        public void TryFire()
        {
            if (FireCooldown > 0f) return;
            FireCooldown = FireInterval();

            Vector3 origin = _mount != null ? _mount.position : transform.position;
            AudioDirector.Instance?.PlayShot();
            Impact.Spawn(origin, new Color(GameConfig.Line(ActiveId).Tint.r,
                GameConfig.Line(ActiveId).Tint.g, GameConfig.Line(ActiveId).Tint.b, 0.85f), 0.22f, 0.09f);

            switch (ActiveId)
            {
                case "prism": FirePrism(origin); break;
                case "swarm": FireSwarm(origin); break;
                default: FirePulse(origin); break;
            }
            SpendCharge();
        }

        const float P = 0.0185185185f;   // GameConfig.Px（此处用常量便于写偏移）

        void FirePulse(Vector3 o)
        {
            int lv = Mathf.Clamp(Active.Level, 1, 5);
            var def = GameConfig.Line("pulse");
            float sp = def.Speed;
            float r = 5f * P;
            void Shot(float dx, float dy, float vx, float dmg, float rad = 0f, float life = 2.2f)
            {
                var pos = o + new Vector3(dx * P, dy * P, 0f);
                if (Precision) { pos.x = o.x + dx * P * GameConfig.FocusPatternCompress; vx *= GameConfig.FocusLateralMul; }
                BulletPool.Instance.Spawn(Team.Player, pos, new Vector2(vx, -0f) + Vector2.up * sp,
                    dmg, rad > 0 ? rad : r, def.Tint, "bolt", life);
            }
            switch (lv)
            {
                case 1: Shot(0, 0, 0, Calibrate(1)); break;
                case 2: Shot(-7, 0, 0, Calibrate(1)); Shot(7, 0, 0, Calibrate(1)); break;
                case 3:
                    Shot(0, 4, 0, Calibrate(1)); Shot(-9, -4, -45, Calibrate(1)); Shot(9, -4, 45, Calibrate(1));
                    break;
                case 4:
                    Shot(-6, 2, 0, Calibrate(1)); Shot(6, 2, 0, Calibrate(1));
                    Shot(-15, -5, -85, Calibrate(1)); Shot(15, -5, 85, Calibrate(1));
                    break;
                default:
                    Shot(0, 6, 0, Calibrate(2), 6f * P);
                    Shot(-8, -2, 0, Calibrate(1)); Shot(8, -2, 0, Calibrate(1));
                    Shot(-17, -7, -105, Calibrate(1)); Shot(17, -7, 105, Calibrate(1));
                    break;
            }
            if (_owner != null && _owner.Overdrive > 0f)
            {
                Shot(-24, -10, -160, Calibrate(1)); Shot(24, -10, 160, Calibrate(1));
            }
        }

        void FirePrism(Vector3 o)
        {
            int lv = Mathf.Clamp(Active.Level, 1, 5);
            var def = GameConfig.Line("prism");
            float sp = def.Speed;
            float b = GameConfig.PrismDamage[lv - 1];
            int pierce = GameConfig.PrismPierce[lv - 1];

            Lance(0, -4, 0, Calibrate(b), 8f * P, pierce);
            if (lv >= 2)
            {
                Lance(-10, 2, 0, Calibrate(Mathf.Max(2, b - 1)), 6f * P, pierce, 0.97f);
                Lance(10, 2, 0, Calibrate(Mathf.Max(2, b - 1)), 6f * P, pierce, 0.97f);
            }
            if (lv >= 4)
            {
                Lance(-20, 6, -95, Calibrate(Mathf.Max(2, b - 2)), 5f * P, 1, 0.94f);
                Lance(20, 6, 95, Calibrate(Mathf.Max(2, b - 2)), 5f * P, 1, 0.94f);
            }

            void Lance(float dx, float dy, float vx, float dmg, float rad, int pier, float speedMul = 1f)
            {
                var pos = o + new Vector3(dx * P, dy * P, 0f);
                if (Precision) { pos.x = o.x + dx * P * GameConfig.FocusPatternCompress; vx *= GameConfig.FocusLateralMul; }
                var p = BulletPool.Instance.Spawn(Team.Player, pos,
                    new Vector2(vx * P, sp * speedMul), dmg, rad, def.Tint, "lance", 3f);
                p.Piercing = pier > 0;
                p.PierceLeft = pier;
                p.BossBonus = GameConfig.PrismBossBonus;
            }
        }

        void FireSwarm(Vector3 o)
        {
            int lv = Mathf.Clamp(Active.Level, 1, 5);
            var def = GameConfig.Line("swarm");
            float sp = def.Speed;
            int count = GameConfig.SwarmCount[lv - 1];
            float baseDmg = GameConfig.SwarmDamage[lv - 1];
            for (int i = 0; i < count; i++)
            {
                float off = i - (count - 1) * 0.5f;
                var pos = o + new Vector3(off * 6f * P, -6f * P, 0f);
                float vx = off * 120f;
                if (Precision)
                {
                    pos.x = o.x + off * 6f * P * GameConfig.FocusPatternCompress;
                    vx *= GameConfig.FocusLateralMul;
                }
                var p = BulletPool.Instance.Spawn(Team.Player, pos, new Vector2(vx * P, sp),
                    Calibrate(baseDmg), 4f * P, def.Tint, "micro", 2.2f);
                p.Homing = true;
                p.HomingTime = GameConfig.SwarmHoming;   // 只追 1.45 秒，之后变直线弹
                p.TurnRate = GameConfig.SwarmTurnRate;
                p.HomingDelay = GameConfig.SwarmLockDelay;
                p.HomingTarget = Director.Instance != null ? Director.Instance.NearestEnemy(pos) : null;
            }
        }

        public void SwitchTo(string id)
        {
            if (id == ActiveId) return;
            var target = System.Array.Find(_all, l => l.Id == id);
            if (target == null || !target.Unlocked) return;
            SelectWeapon(id);
        }

        /// <summary>点武器胶囊循环换线（网页版 cycleWeapon）。</summary>
        public bool CycleWeapon()
        {
            var unlocked = new System.Collections.Generic.List<string>();
            foreach (var l in _all) if (l.Unlocked) unlocked.Add(l.Id);
            if (unlocked.Count < 2) return false;
            int idx = unlocked.IndexOf(ActiveId);
            return SelectWeapon(unlocked[(idx + 1) % unlocked.Count]);
        }

        public bool SelectWeapon(string id)
        {
            var target = System.Array.Find(_all, l => l.Id == id);
            if (target == null || !target.Unlocked || id == ActiveId) return false;

            // 原版先读停留时间、再立刻把计时清零（entities.js:546-549）。
            // 之前只读不清，_swapIdle 一路累加，1.2 秒的共鸣门槛等于永远满足，
            // 连按 1/2/3 就能把 5 层共鸣刷满——换线节奏的意义被抽空了。
            bool wellTimed = _owner == null || _owner.SwapIdle >= GameConfig.SwapResonanceGap;
            if (_owner != null) _owner.ResetSwapIdle();

            ActiveId = id;
            FireCooldown = Mathf.Min(FireCooldown, 0.07f);

            // 只有「停够时间 + 目标线健康」的换线才叠共鸣；乱按反而清零，避免刷链
            if (ResonanceGain(wellTimed, target.Charge, target.Detuned))
            {
                Resonance = Mathf.Min(GameConfig.ResonanceMaxStacks, Resonance + 1f);
                _resTimer = GameConfig.ResonanceWindow;
            }
            else
            {
                Resonance = 0f;
                _resTimer = 0f;
            }
            AudioDirector.Instance?.PlaySwitch();
            return true;
        }

        /// <summary>补给箱：当前线升级；已满级则解锁/升级下一条未解锁的线。</summary>
        public bool GrantLevel()
        {
            var w = Active;
            if (w.Level < 5) { w.Level++; return true; }
            foreach (var l in _all)
            {
                if (!l.Unlocked) { l.Unlocked = true; l.Level = 1; l.Charge = 100f; SelectWeapon(l.Id); return true; }
            }
            return false;
        }

        /// <summary>指定线的升级箱（prism / swarm 箱）：未解锁则解锁，已解锁则 +1 级。</summary>
        public bool GrantLine(string id)
        {
            var l = System.Array.Find(_all, x => x.Id == id);
            if (l == null) return false;
            if (!l.Unlocked) { l.Unlocked = true; l.Level = 1; l.Charge = 100f; SelectWeapon(id); return true; }
            if (l.Level < 5)
            {
                l.Level++;
                if (id == ActiveId) return true;
                SelectWeapon(id);
                return true;
            }
            return false;
        }

        /// <summary>换线箱：转到蓄能最满的收纳线并补一点，只有一条线时改为回充。</summary>
        public bool SwapToBest(float topUp = 30f)
        {
            var holstered = new System.Collections.Generic.List<LineState>();
            foreach (var l in _all) if (l.Unlocked && l.Id != ActiveId) holstered.Add(l);
            if (holstered.Count == 0)
            {
                var w = Active;
                w.Charge = Mathf.Min(100f, w.Charge + topUp + 15f);
                if (w.Detuned && w.Charge >= GameConfig.DetuneClearCharge) w.Detuned = false;
                return false;
            }
            LineState best = holstered[0];
            foreach (var l in holstered) if (l.Charge > best.Charge) best = l;
            best.Charge = Mathf.Min(100f, best.Charge + topUp);
            if (best.Detuned && best.Charge >= GameConfig.DetuneClearCharge) best.Detuned = false;
            return SelectWeapon(best.Id);
        }

        /// <summary>三线全部回满并解除失谐。</summary>
        public void RefillAll()
        {
            foreach (var l in _all) { l.Charge = 100f; l.Detuned = false; }
        }

        /// <summary>受伤：当前线掉 45 蓄能、共鸣清零、掉一级。</summary>
        public void OnPlayerHit()
        {
            var w = Active;
            w.Charge = Mathf.Max(0f, w.Charge - GameConfig.HitChargePenalty);
            if (w.Charge <= 0.5f) w.Detuned = true;
            Resonance = 0f;
            _resTimer = 0f;
            if (w.Level > 1) w.Level--;
        }
    }
}
