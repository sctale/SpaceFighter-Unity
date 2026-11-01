using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 三条武器线 × 5 级，蓄能耗尽会"失谐"（威力 55%、射速下降），收纳中回充更快。
    /// 换线叠共鸣，每层 +8% 伤害、最多 5 层，停手会衰减——原版的核心循环。
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

        public LineState Pulse = new LineState { Id = "pulse", Level = 2, Unlocked = true };
        public LineState Prism = new LineState { Id = "prism", Level = 1 };
        public LineState Swarm = new LineState { Id = "swarm", Level = 1 };

        public string ActiveId = "pulse";
        public float Resonance { get; private set; }
        public float FireCooldown { get; private set; }
        public float IdleTimer { get; private set; }
        public bool Precision;

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

        public void Tick(float dt, bool firing)
        {
            float activeDrain = 0f;
            var def = GameConfig.Line(ActiveId);

            if (firing && Active.Charge > 0f)
            {
                activeDrain = def.Drain;
                if (Active.Charge - activeDrain * dt <= 0f)
                {
                    Active.Charge = 0f;
                    Active.Detuned = true;
                    Resonance = 0f;
                }
                else Active.Charge -= activeDrain * dt;
                IdleTimer = 0f;
            }
            else
            {
                IdleTimer += dt;
                // 停手 1.2 秒后共鸣逐层衰减
                if (IdleTimer > 1.2f && Resonance > 0f) Resonance = Mathf.Max(0f, Resonance - dt * 0.8f);
            }

            // 收纳中的线回充（比开火快），失谐线排气到满才恢复
            foreach (var l in _all)
            {
                if (!l.Unlocked || l == Active) continue;
                var ld = GameConfig.Line(l.Id);
                l.Charge = Mathf.Min(100f, l.Charge + ld.Recover * dt * 1.35f);
                if (l.Detuned && l.Charge >= 100f) l.Detuned = false;
            }
            if (!firing && Active.Charge > 0f)
            {
                Active.Charge = Mathf.Min(100f, Active.Charge + def.Recover * dt * 0.6f);
                if (Active.Detuned && Active.Charge >= 100f) Active.Detuned = false;
            }

            FireCooldown -= dt;
        }

        public void TryFire()
        {
            var st = Active;
            if (st.Charge <= 0f || FireCooldown > 0f) return;
            var def = GameConfig.Line(st.Id);

            float rate = BaseRate(st.Id) * (st.Detuned ? GameConfig.DetunedFireRate : 1f);
            FireCooldown = rate;

            float power = (st.Detuned ? GameConfig.DetunedPower : 1f)
                        * (1f + Resonance * GameConfig.ResonancePerStack)
                        * (1f + (st.Level - 1) * 0.12f);

            Vector3 origin = _mount != null ? _mount.position : transform.position;
            AudioDirector.Instance?.PlayShot();
            Impact.Spawn(origin, new Color(def.Tint.r, def.Tint.g, def.Tint.b, 0.85f), 0.30f, 0.12f);
            switch (st.Id)
            {
                case "prism":
                    FirePrism(origin, power, st.Level);
                    break;
                case "swarm":
                    FireSwarm(origin, power, st.Level);
                    break;
                default:
                    FirePulse(origin, power, st.Level);
                    break;
            }
        }

        static float BaseRate(string id)
        {
            if (id == "prism") return 0.34f;
            if (id == "swarm") return 0.19f;
            return 0.115f;
        }

        void FirePulse(Vector3 o, float power, int level)
        {
            var tint = GameConfig.Pulse.Tint;
            int streams = Precision ? 1 : (level >= 4 ? 3 : level >= 2 ? 2 : 1);
            for (int i = 0; i < streams; i++)
            {
                float dx = (i - (streams - 1) * 0.5f) * 0.13f;
                var vel = new Vector2(dx * 6f, GameConfig.Pulse.Speed);
                BulletPool.Instance.Spawn(Team.Player, o + new Vector3(dx, 0f, 0f), vel,
                    1.0f * power, 0.07f, tint);
            }
        }

        void FirePrism(Vector3 o, float power, int level)
        {
            var tint = GameConfig.Prism.Tint;
            int streams = Precision ? 1 : (level >= 5 ? 2 : 1);
            for (int i = 0; i < streams; i++)
            {
                float dx = streams == 1 ? 0f : (i == 0 ? -0.09f : 0.09f);
                var p = BulletPool.Instance.Spawn(Team.Player, o + new Vector3(dx, 0f, 0f),
                    new Vector2(0f, GameConfig.Prism.Speed), 3.2f * power, 0.13f, tint);
                p.Piercing = level >= 3;
            }
        }

        void FireSwarm(Vector3 o, float power, int level)
        {
            var tint = GameConfig.Swarm.Tint;
            int count = level >= 4 ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                float dx = count == 1 ? 0f : (i == 0 ? -0.16f : 0.16f);
                var p = BulletPool.Instance.Spawn(Team.Player, o + new Vector3(dx, 0f, 0f),
                    new Vector2(dx * 8f, GameConfig.Swarm.Speed * 0.85f), 1.5f * power, 0.08f, tint);
                p.Homing = true;
                p.HomingTarget = Director.Instance != null ? Director.Instance.NearestEnemy(o) : null;
            }
        }

        public void SwitchTo(string id)
        {
            if (id == ActiveId) return;
            var target = System.Array.Find(_all, l => l.Id == id);
            if (target == null || !target.Unlocked) return;
            ActiveId = id;
            IdleTimer = 0f;
            // 换到蓄能健康的线才叠共鸣
            if (target.Charge > 45f && Resonance < GameConfig.ResonanceMaxStacks) Resonance += 1f;
        }

        /// <summary>受伤掉当前武器一级（最低 1 级），原版规则。</summary>
        public void OnPlayerHit()
        {
            Resonance = 0f;
            if (Active.Level > 1) Active.Level--;
        }

        /// <summary>补给箱：优先给当前线升级；满级则解锁下一条未解锁的线。</summary>
        public void GrantLevel()
        {
            foreach (var l in _all)
                if (l.Unlocked && l.Level < 5) { l.Level++; return; }
            foreach (var l in _all)
                if (!l.Unlocked) { l.Unlocked = true; l.Level = 1; l.Charge = 100f; return; }
        }

        /// <summary>补给箱：三条线全部回满并解除失谐。</summary>
        public void RefillAll()
        {
            foreach (var l in _all) { l.Charge = 100f; l.Detuned = false; }
        }
    }
}
