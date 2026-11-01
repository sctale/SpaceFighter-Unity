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
        public bool Piercing;
        public int PierceLeft;
        public float BossBonus = 1f;
        public string Kind = "orb";

        /// <summary>爆裂词缀：这颗弹到寿时向四周炸开 N 发。</summary>
        public int SplitOnExpire;

        /// <summary>擦弹与相位都要「每颗弹只记一次」，靠这两个标记去重。</summary>
        public bool Grazed;
        public bool Phased;

        SpriteRenderer _sr;
        float _age;

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
                if (kind == "lance") return "bullet_player_lance";
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
            Homing = false;
            HomingTarget = null;
            HomingDelay = 0f;
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

        bool Aligned => Kind == "bolt" || Kind == "lance" || Kind == "micro" || Kind == "needle";

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
            Life -= dt;
            if (Life <= 0f) { Split(); Recycle(); return; }

            if (Homing && _age >= HomingDelay && HomingTarget != null && HomingTarget.gameObject.activeInHierarchy)
            {
                Vector2 want = ((Vector2)HomingTarget.position - (Vector2)transform.position).normalized;
                Vector2 cur = Velocity.normalized;
                float speed = Velocity.magnitude;
                // 只锁定「合法入屏」的目标，追踪弹不能提前清波
                float maxRad = TurnRate * dt;
                Velocity = Vector2.Lerp(cur, want, Mathf.Clamp01(maxRad)).normalized * speed;
            }
            else if (Homing && _age >= HomingDelay)
            {
                var t = Director.Instance != null ? Director.Instance.NearestEnemy(transform.position) : null;
                if (t != null) HomingTarget = t;
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

        void Split()
        {
            if (SplitOnExpire <= 0) return;
            SplitOnExpire = 0;
            for (int i = 0; i < GameConfig.VolatileSplit; i++)
            {
                float a = i * Mathf.PI * 2f / GameConfig.VolatileSplit + Random.value * 0.2f;
                var v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 110f * GameConfig.Px;
                BulletPool.Instance.Spawn(Team.Enemy, transform.position, v, 1f, 5f * GameConfig.Px,
                    new Color(1f, 0.6f, 0.3f), "orb", 5f);
            }
            Impact.Spawn(transform.position, new Color(1f, 0.6f, 0.3f, 0.9f), 0.7f, 0.2f);
        }

        void Recycle() { Split(); BulletPool.Instance?.Release(this); }
    }
}
