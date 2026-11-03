using UnityEngine;

namespace AuroraRift
{
    public enum Team { Player, Enemy }

    /// <summary>
    /// 弹体：位置由 Transform 持有，运动学自己算，不走物理引擎——弹幕游戏要的是确定性。
    /// 半径一律用网页版的像素半径 × GameConfig.Px，视觉尺寸按原版贴图比例放大。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public Team Team;
        public float Damage = 1f;
        public float Radius = 0.09f;
        public Vector2 Velocity;
        public float Life = GameConfig.BulletLifetime;
        public bool Homing;
        public Transform HomingTarget;
        public float TurnRate = 2.4f;
        public float HomingDelay = 0f;

        /// <summary>
        /// 原版的 homing 是「还能追踪几秒」的倒计时（entities.js:126-147），不是一直追。
        /// 蜂群 micro 1.45s、僚机 micro 1.6s、Boss 追踪星群 2.6s，到点就变直线弹。
        /// </summary>
        public float HomingTime = 0f;
        public bool Piercing;
        public int PierceLeft;
        public float BossBonus = 1f;
        public string Kind = "orb";

        /// <summary>爆裂词缀：这颗弹到寿时向四周炸开 N 发。</summary>
        public int SplitOnExpire;

        /// <summary>水雷引信（秒）。与 life 分开：水雷靠引信起爆，等离子靠到寿起爆。</summary>
        public float Fuse;

        /// <summary>波动弹的角速度摆幅（rad/s 的系数）；0 = 直线。</summary>
        public float Wave;

        /// <summary>上一帧位置，命中判定用线段扫掠（原版 prevX/prevY）。</summary>
        public Vector2 PrevPos;

        /// <summary>擦弹与相位都要「每颗弹只记一次」，靠这两个标记去重。</summary>
        public bool Grazed;
        public bool Phased;

        SpriteRenderer _sr;
        float _age;
        float _seed = Random.value * 10f;   // 原版每颗弹的随机相位（波动弹与破片角度都靠它去重）
        bool _mineArmed;

        // 贴图长宽比来自原版：视觉长度 / 判定半径
        static float VisualScale(string kind, float radius)
        {
            switch (kind)
            {
                case "bolt": return radius * 7.5f;
                case "lance": return radius * 8.0f;
                case "micro": return radius * 7.5f;
                case "needle": return radius * 6.5f;
                case "heavy": return radius * 3.0f;
                case "mine": return radius * 2.8f;
                case "plasma": return radius * 2.6f;
                case "drone": return radius * 6.0f;
                default: return radius * 3.2f;   // orb
            }
        }

        static string SpriteFor(string kind, Team team)
        {
            if (team == Team.Player)
            {
                if (kind == "lance" || kind == "beam") return "bullet_player_lance";
                if (kind == "micro") return "bullet_player_micro";
                return "bullet_player_bolt";
            }
            switch (kind)
            {
                case "needle": return "bullet_enemy_needle";
                case "heavy": return "bullet_enemy_heavy";
                case "plasma": return "bullet_enemy_heavy";
                case "mine": return "bullet_enemy_mine";
                default: return "bullet_enemy_orb";
            }
        }

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        public void Init(Team team, Vector2 velocity, float damage, float radius, Color tint,
                         string kind = null, float life = 0f)
        {
            Team = team;
            Velocity = velocity;
            Damage = damage;
            Radius = radius;
            Kind = string.IsNullOrEmpty(kind) ? (team == Team.Player ? "bolt" : "orb") : kind;
            Life = life > 0f ? life : GameConfig.BulletLifetime;
            Grazed = false;
            Phased = false;
            SplitOnExpire = 0;
            Fuse = 0f;
            Wave = 0f;
            PrevPos = transform.position;
            _seed = Random.value * 10f;
            _mineArmed = false;
            Homing = false;
            HomingTarget = null;
            HomingDelay = 0f;
            HomingTime = 0f;
            TurnRate = 2.4f;
            Piercing = false;
            PierceLeft = 0;
            BossBonus = 1f;
            _age = 0f;

            if (_sr == null) return;

            bool hc = GameRoot.Instance != null && GameRoot.Instance.HighContrast && team == Team.Enemy;
            var sprite = hc ? RuntimeSprites.Ring(48) : ShipArt.Get(SpriteFor(Kind, Team), 1f);
            if (sprite == null)
                sprite = team == Team.Player ? RuntimeSprites.Capsule(24) : RuntimeSprites.Diamond(24);
            _sr.sprite = sprite;
            var outer = team == Team.Player ? Color.Lerp(tint, Color.white, 0.30f) : tint;
            _sr.color = new Color(outer.r, outer.g, outer.b, 1f);

            transform.localScale = Vector3.one * VisualScale(Kind, Radius);
            Align();
        }

        bool Aligned => Kind == "bolt" || Kind == "lance" || Kind == "beam"
                        || Kind == "micro" || Kind == "needle";

        void Align()
        {
            if (!Aligned) return;
            Vector2 v = Velocity;
            if (v.sqrMagnitude < 1e-5f) return;
            transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f);
        }

        public void Tick(float dt)
        {
            _age += dt;
            // 原版每帧先记下上一帧位置，命中判定走「线段到点」而非「点对点和」
            // （entities.js:98-99 + game.js:899）。330px/s 的针弹在低帧率下每帧走 5.5px，
            // 点位判定会整颗从 4.6px 判定圈旁边穿过去。
            PrevPos = transform.position;

            // 水雷靠引信起爆（fuse），等离子靠到寿起爆（life）——两条路都要炸。
            if (Fuse > 0f)
            {
                Fuse -= dt;
                if (Fuse <= 0f) { Burst(); BulletPool.Instance?.Release(this); return; }
            }

            Life -= dt;
            if (Life <= 0f)
            {
                if (Kind == "mine" || Kind == "plasma") Burst();
                BulletPool.Instance?.Release(this);
                return;
            }

            if (Kind == "mine")
            {
                // 水雷是「布」出来的：速度按 exp(-3.8dt) 衰减到几乎停住，
                // 引信只剩 0.9 秒时闪一圈提示可以穿了（entities.js:143-149）。
                Velocity *= Mathf.Exp(-GameConfig.MineDrag * dt);
                if (!_mineArmed && Fuse < 0.9f)
                {
                    _mineArmed = true;
                    Impact.Spawn(transform.position, new Color(1f, 0.82f, 0.35f, 0.7f), 0.62f, 0.24f);
                }
            }
            else if (Wave != 0f)
            {
                // 波动弹：角速度按 sin(age×7.2 + seed) 摆动，画出蛇形轨迹（entities.js:152-158）
                float spd = Velocity.magnitude;
                float cur = Mathf.Atan2(Velocity.y, Velocity.x);
                float na = cur + Mathf.Sin(_age * GameConfig.WavePhaseRate + _seed) * Wave * dt;
                Velocity = new Vector2(Mathf.Cos(na), Mathf.Sin(na)) * spd;
            }

            // 原版 entities.js:126-147：homing 是「还能追几秒」的倒计时，每帧把方向朝
            // 目标转「不超过 turn×dt 弧度」。之前用 Vector2.Lerp 当转向上限，实际是按
            // 角度差的比例转——大角度差一帧就转过头，小角度差又转不动，手感与原版不符。
            if (Homing && HomingTime > 0f && _age >= HomingDelay)
            {
                Vector2? aim = HomingAim();
                if (aim.HasValue)
                {
                    HomingTime -= dt;
                    Vector2 from = transform.position;
                    float curAng = Mathf.Atan2(Velocity.y, Velocity.x) * Mathf.Rad2Deg;
                    float wantAng = Mathf.Atan2(aim.Value.y - from.y, aim.Value.x - from.x) * Mathf.Rad2Deg;
                    float maxDeg = TurnRate * Mathf.Rad2Deg * dt;
                    float next = (curAng + Mathf.Clamp(Mathf.DeltaAngle(curAng, wantAng), -maxDeg, maxDeg))
                                 * Mathf.Deg2Rad;
                    Velocity = new Vector2(Mathf.Cos(next), Mathf.Sin(next)) * Velocity.magnitude;
                }
            }

            transform.position += (Vector3)(Velocity * dt);
            Align();

            if (Mathf.Abs(transform.position.x) > GameView.HalfWidth + 1f ||
                transform.position.y < GameView.DespawnY - 0.5f ||
                transform.position.y > GameView.SpawnY + 0.5f)
            {
                Recycle();
            }
        }

        /// <summary>
        /// 追踪目标：敌弹锁玩家（原版 !friendly 时直接指 player，entities.js:134-137）；
        /// 友弹锁最近「可打」敌机——不能提前锁定还没入屏的编队，否则追弹替玩家清波。
        /// </summary>
        Vector2? HomingAim()
        {
            if (Team == Team.Enemy)
            {
                var p = GameRoot.Instance != null ? GameRoot.Instance.Player : null;
                if (p != null && p.Alive) return p.transform.position;
                return null;
            }
            if (HomingTarget != null && HomingTarget.gameObject.activeInHierarchy) return HomingTarget.position;
            Transform t = Director.Instance != null ? Director.Instance.NearestEnemy(transform.position) : null;
            if (t != null) { HomingTarget = t; return t.position; }
            return null;
        }

        /// <summary>
        /// 到点炸开（原版 burst()，entities.js:71-94）：
        /// 水雷炸 split(默认 10) 颗 shard，速度 128 + 星区×2；等离子炸 6 颗 orb，速度 105。
        /// 之前只有一个通用 Split()，固定 6 颗、固定 110px/s，水雷帘和爆裂词缀混成同一种炸法。
        /// </summary>
        void Burst()
        {
            if (Team == Team.Player) return;
            int zone = Director.Instance != null ? Director.Instance.Zone : 1;

            if (Kind == "mine")
            {
                int n = SplitOnExpire > 0 ? SplitOnExpire : 10;
                float sp = (128f + zone * 2f) * GameConfig.Px;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n + _seed * 0.3f;
                    BulletPool.Instance.Spawn(Team.Enemy, transform.position,
                        new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, 1f, 5f * GameConfig.Px,
                        new Color(1f, 0.85f, 0.35f), "shard", 7f);
                }
                Impact.Spawn(transform.position, new Color(1f, 0.82f, 0.35f, 0.85f), 0.95f, 0.3f);
                AudioDirector.Instance?.PlayHit();
            }
            else if (Kind == "plasma")
            {
                int n = SplitOnExpire > 0 ? SplitOnExpire : GameConfig.VolatileSplit;
                float sp = 105f * GameConfig.Px;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n + _seed;
                    BulletPool.Instance.Spawn(Team.Enemy, transform.position,
                        new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, 1f, 6f * GameConfig.Px,
                        new Color(1f, 0.6f, 0.3f), "orb", 7f);
                }
                Impact.Spawn(transform.position, new Color(0.61f, 0.55f, 1f, 0.8f), 1.1f, 0.32f);
            }
            SplitOnExpire = 0;
        }

        void Recycle() { BulletPool.Instance?.Release(this); }
    }
}
