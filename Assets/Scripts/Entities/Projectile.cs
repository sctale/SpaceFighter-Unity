using UnityEngine;

namespace AuroraRift
{
    public enum Team { Player, Enemy }

    /// <summary>
    /// 极简弹体：位置由 Transform 持有，运动学自己算，不走物理引擎——
    /// 弹幕游戏要的是确定性，不是碰撞器。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public Team Team;
        public float Damage = 1f;
        public float Radius = 0.12f;
        public Vector2 Velocity;
        public float Life = GameConfig.BulletLifetime;
        public bool Homing;
        public Transform HomingTarget;
        public float TurnRate = 3.2f;
        public float HomingDelay = 0.25f;
        public bool Piercing;

        SpriteRenderer _sr;
        float _age;
        float _baseScale = 1f;
        bool _alignToVelocity;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        public void Init(Team team, Vector2 velocity, float damage, float radius, Color tint)
        {
            Team = team;
            Velocity = velocity;
            Damage = damage;
            Radius = radius;
            _alignToVelocity = false;

            if (_sr == null) return;

            bool hc = GameRoot.Instance != null && GameRoot.Instance.HighContrast;
            if (team == Team.Enemy && hc)
            {
                _sr.sprite = RuntimeSprites.Ring(48);
                _sr.color = Color.white;
                _baseScale = radius * 2.6f;
                transform.localScale = Vector3.one * _baseScale;
                return;
            }

            // 按弹种挑预渲染精灵：外形决定辨识度，发光靠贴图，颜色仍按阵营/武器染
            string art;
            float size;
            if (team == Team.Player)
            {
                if (radius >= 0.11f) { art = "bullet_player_lance"; size = radius * 4.3f; _alignToVelocity = true; }
                else if (radius <= 0.06f) { art = "bullet_player_micro"; size = radius * 5.4f; _alignToVelocity = true; }
                else { art = "bullet_player_bolt"; size = radius * 6.1f; _alignToVelocity = true; }
            }
            else
            {
                if (radius >= 0.14f) { art = "bullet_enemy_heavy"; size = radius * 3.7f; }
                else if (radius >= 0.11f) { art = "bullet_enemy_mine"; size = radius * 3.2f; }
                else if (radius <= 0.065f) { art = "bullet_enemy_needle"; size = radius * 6.9f; _alignToVelocity = true; }
                else { art = "bullet_enemy_orb"; size = radius * 3.5f; }
            }

            var sp = ShipArt.Get(art, 1f);
            if (sp != null)
            {
                _sr.sprite = sp;
                var outer = team == Team.Player ? Color.Lerp(tint, Color.white, 0.30f) : tint;
                _sr.color = new Color(outer.r, outer.g, outer.b, 1f);
            }
            else
            {
                _sr.sprite = team == Team.Player ? RuntimeSprites.Capsule(24) : RuntimeSprites.Diamond(24);
                _sr.color = tint;
            }
            _baseScale = size;
            transform.localScale = Vector3.one * _baseScale;
            Align();
        }

        void Align()
        {
            if (!_alignToVelocity) return;
            Vector2 v = Velocity;
            if (v.sqrMagnitude < 1e-5f) return;
            transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f);
        }

        public void Tick(float dt)
        {
            _age += dt;
            Life -= dt;
            if (Life <= 0f) { Recycle(); return; }

            if (Homing && _age >= HomingDelay && HomingTarget != null && HomingTarget.gameObject.activeInHierarchy)
            {
                Vector2 want = ((Vector2)HomingTarget.position - (Vector2)transform.position).normalized;
                Vector2 cur = Velocity.normalized;
                float speed = Velocity.magnitude;
                float maxRad = TurnRate * dt;
                Velocity = Vector2.Lerp(cur, want, Mathf.Clamp01(maxRad)).normalized * speed;
            }

            transform.position += (Vector3)(Velocity * dt);
            Align();
            if (Mathf.Abs(transform.position.x) > GameConfig.PlayHalfWidth + 1.5f ||
                transform.position.y < -GameConfig.OrthoSizeMin - 1.5f ||
                transform.position.y > GameConfig.OrthoSizeMin + 1.5f)
            {
                Recycle();
            }
        }

        void Recycle()
        {
            BulletPool.Instance?.Release(this);
        }
    }
}
