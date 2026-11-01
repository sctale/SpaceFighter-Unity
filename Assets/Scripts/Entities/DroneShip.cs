using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 僚机：停在左翼/右翼/尾部的固定挂载点，跟随带阻尼而不绕圈；
    /// 弹药形态随当前武器线。玩家被击中时僚机爆炸失联（原版规则）。
    /// </summary>
    public enum DroneType { Seeker, Cannon, Support }

    public class DroneShip : MonoBehaviour
    {
        public int Slot;                 // 0=左翼 1=右翼 2=尾部
        public DroneType Type;
        public float FireCooldown;

        PlayerShip _owner;
        SpriteRenderer _sr, _glow;
        Vector2 _vel;

        static readonly Vector2[] Offsets =
        {
            new Vector2(-0.85f, -0.05f),
            new Vector2(0.85f, -0.05f),
            new Vector2(0f, -0.9f),
        };

        public void Bind(PlayerShip owner, int slot)
        {
            _owner = owner;
            Slot = slot;
            Type = slot == 0 ? DroneType.Seeker : slot == 1 ? DroneType.Cannon : DroneType.Support;
            _sr = gameObject.AddComponent<SpriteRenderer>();
            string art = Type switch
            {
                DroneType.Seeker => "drone_seeker",
                DroneType.Cannon => "drone_cannon",
                _ => "drone_support",
            };
            _sr.sprite = ShipArt.Get(art, 0.58f) ?? RuntimeSprites.Disc(32);
            _sr.color = Color.white;
            _sr.sortingOrder = 9;
            transform.localScale = Vector3.one;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(transform, false);
            _glow = glowGo.AddComponent<SpriteRenderer>();
            _glow.sprite = ShipArt.Get("bullet_glow_soft", 0.95f) ?? RuntimeSprites.Disc(16);
            _glow.sortingOrder = 8;
            _glow.transform.localScale = Vector3.one * 0.9f;

            FireCooldown = 0.25f + slot * 0.08f;
        }

        void Update()
        {
            if (_owner == null) { Destroy(gameObject); return; }
            var root = GameRoot.Instance;
            if (root != null && root.State != GameState.Playing) return;
            float dt = Time.deltaTime;

            // 阻尼跟随：临界阻尼弹簧，避免绕着母舰转圈
            Vector2 target = (Vector2)_owner.transform.position + Offsets[Slot];
            Vector2 cur = transform.position;
            Vector2 desired = (target - cur) * 7.5f;
            _vel = Vector2.Lerp(_vel, desired, 1f - Mathf.Exp(-12f * dt));
            transform.position = cur + _vel * dt;

            var line = GameConfig.Line(_owner.Weapons.ActiveId);
            if (_glow != null)
                _glow.color = new Color(line.Tint.r, line.Tint.g, line.Tint.b,
                    0.16f + Mathf.Sin(Time.time * 4.2f + Slot) * 0.04f);

            // 小幅侧倾，跟随机动而不是整圈乱转
            if (_vel.sqrMagnitude > 0.0004f)
            {
                float ang = Mathf.Clamp(Mathf.Atan2(-_vel.x, _vel.y) * Mathf.Rad2Deg, -26f, 26f);
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.Euler(0f, 0f, ang), 1f - Mathf.Exp(-8f * dt));
            }

            FireCooldown -= dt;
            if (FireCooldown <= 0f)
            {
                Fire();
                FireCooldown = 0.34f + Slot * 0.05f;
            }
        }

        void Fire()
        {
            var line = GameConfig.Line(_owner.Weapons.ActiveId);
            var target = Director.Instance != null ? Director.Instance.NearestEnemy(transform.position) : null;
            Vector2 dir = target != null
                ? ((Vector2)target.position - (Vector2)transform.position).normalized
                : Vector2.up;

            switch (Type)
            {
                case DroneType.Seeker:   // 弧线寻的
                {
                    var p = BulletPool.Instance.Spawn(Team.Player, transform.position,
                        dir * line.Speed * 0.75f, 0.9f, 0.06f, new Color(1f, 0.85f, 0.55f));
                    p.Homing = true;
                    p.HomingTarget = target;
                    break;
                }
                case DroneType.Cannon:   // 低速重击
                {
                    BulletPool.Instance.Spawn(Team.Player, transform.position,
                        dir * line.Speed * 0.55f, 2.2f, 0.11f, new Color(0.95f, 0.6f, 0.75f));
                    break;
                }
                default:                 // 高速细弹
                {
                    BulletPool.Instance.Spawn(Team.Player, transform.position,
                        dir * line.Speed * 1.15f, 0.5f, 0.05f, new Color(0.8f, 0.95f, 1f));
                    break;
                }
            }
        }

        public void Explode()
        {
            Destroy(gameObject);
        }
    }
}
