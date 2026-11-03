using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 僚机：停在左翼/右翼/尾部的固定挂载点，跟随带阻尼而不绕圈。
    /// 四种型号（gunner 火力 / lance 双管 / aegis 点防御 / swarm 微型追踪），
    /// 但弹药形态跟着主机当前武器线走——换线时整个编队的弹道一起变。
    /// </summary>
    public class DroneShip : MonoBehaviour
    {
        public int Slot;
        public string Kind = "gunner";
        public float FireCooldown;

        PlayerShip _owner;
        SpriteRenderer _sr;
        Vector2 _vel;
        float _pdTimer;

        public static Vector2 SlotOffset(int index, int count)
        {
            // 原版 droneSlot()：两架分列左右翼尖，第三架挂尾
            if (count <= 1) return new Vector2(-64, 8);
            if (count == 2) return index == 0 ? new Vector2(-64, 8) : new Vector2(64, 8);
            if (index == 0) return new Vector2(-68, 2);
            if (index == 1) return new Vector2(68, 2);
            return new Vector2(0, 48);
        }

        public void Bind(PlayerShip owner, int slot, string kind)
        {
            _owner = owner;
            Slot = slot;
            Kind = string.IsNullOrEmpty(kind) ? "gunner" : kind;

            string art = Kind switch
            {
                "lance" => "drone_cannon",
                "aegis" => "drone_support",
                "swarm" => "drone_swarm",
                _ => "drone_seeker",
            };
            _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sprite = ShipArt.Get(art, 0.44f) ?? RuntimeSprites.Disc(32);
            _sr.color = Color.white;
            _sr.sortingOrder = 9;
            transform.localScale = Vector3.one;

            FireCooldown = Random.Range(0.15f, 0.5f);
        }

        void Update()
        {
            if (_owner == null) { Destroy(gameObject); return; }
            var root = GameRoot.Instance;
            float dt = Time.deltaTime;

            int count = _owner != null ? _owner.DroneCount : 1;
            Vector2 slot = SlotOffset(Slot, Mathf.Max(1, count));
            float halfW = GameView.HalfWidth;
            float tx = Mathf.Clamp(_owner.transform.position.x + slot.x * GameConfig.Px,
                                   -halfW + 18 * GameConfig.Px, halfW - 18 * GameConfig.Px);
            float ty = _owner.transform.position.y - slot.y * GameConfig.Px;   // 原版 y 向下为正
            Vector2 cur = transform.position;
            Vector2 want = new Vector2(tx, ty);
            // damp(a,b,13,dt)：临界阻尼，绕圈是不可能的
            transform.position = Vector2.Lerp(cur, want, 1f - Mathf.Exp(-13f * dt));

            // 菜单/暂停时也要摆位：以前这里整段 return，僚机就停在世界原点，
            // 开始界面中间凭空多出一架飞机（真机截图实测到的现象）。
            if (root != null && root.State != GameState.Playing) return;

            FireCooldown -= dt;
            if (FireCooldown > 0f) return;
            Fire();
        }

        void Fire()
        {
            var line = GameConfig.Line(_owner.Weapons.ActiveId);
            Vector2 pos = transform.position;
            // 原版 lean：僚机相对玩家的横向偏移换算成 0.18 以内的侧向分量，
            // 让侧翼僚机的弹道自然张开（entities.js:827）。之前完全没算，僚机弹全直着走。
            float lean = Mathf.Clamp((pos.x - _owner.transform.position.x) / (55f * GameConfig.Px),
                                     -0.18f, 0.18f);

            if (Kind == "aegis")
            {
                FireCooldown = GameConfig.AegisPdInterval;
                PointDefense();
                return;
            }

            if (_owner.Weapons.ActiveId == "swarm" || Kind == "swarm")
            {
                FireCooldown = Kind == "gunner" ? 0.86f : 1.02f;
                int n = Kind == "lance" ? 2 : 1;
                for (int k = 0; k < n; k++)
                {
                    float vx = (lean * 520f + (k - (n - 1) * 0.5f) * 140f) * GameConfig.Px;
                    var p = BulletPool.Instance.Spawn(Team.Player, pos + new Vector2(0f, 5f * GameConfig.Px),
                        new Vector2(vx, 700f * GameConfig.Px),
                        Mathf.Max(1, Mathf.Round(2 * _owner.Weapons.DamageMul())),
                        4f * GameConfig.Px, line.Tint, "micro", 2.1f);
                    p.Homing = true;
                    p.HomingTime = 1.6f;
                    p.TurnRate = 4.4f;
                    p.HomingDelay = 0.12f;
                    p.HomingTarget = Director.Instance != null ? Director.Instance.NearestEnemy(pos) : null;
                }
                return;
            }

            if (_owner.Weapons.ActiveId == "prism")
            {
                FireCooldown = Kind == "lance" ? 0.82f : 1.05f;
                // 原版棱镜线给僚机换的是 beam 弹（entities.js:851），不是 lance
                var b = BulletPool.Instance.Spawn(Team.Player, pos + new Vector2(0f, 9f * GameConfig.Px),
                    new Vector2(lean * 150f * GameConfig.Px, 600f * GameConfig.Px),
                    Mathf.Max(1, Mathf.Round((Kind == "lance" ? 4 : 3) * _owner.Weapons.DamageMul())),
                    6f * GameConfig.Px, line.Tint, "beam", 2.4f);
                b.Piercing = true;
                b.PierceLeft = Kind == "lance" ? 2 : 1;
                return;
            }

            FireCooldown = Kind == "gunner" ? 0.34f : 0.5f;
            BulletPool.Instance.Spawn(Team.Player, pos + new Vector2(0f, 7f * GameConfig.Px),
                new Vector2(lean * 260f * GameConfig.Px, 1180f * GameConfig.Px),
                Mathf.Max(1, Mathf.Round(1 * _owner.Weapons.DamageMul())),
                4f * GameConfig.Px, line.Tint, "drone", 1.9f);
            if (Kind == "lance")
            {
                BulletPool.Instance.Spawn(Team.Player, pos + new Vector2(-5f * GameConfig.Px, 4f * GameConfig.Px),
                    new Vector2((lean * 320f - 60f) * GameConfig.Px, 1140f * GameConfig.Px),
                    Mathf.Max(1, Mathf.Round(1 * _owner.Weapons.DamageMul())),
                    4f * GameConfig.Px, line.Tint, "drone", 1.9f);
            }
        }

        /// <summary>aegis 的点防御圈：每 0.7 秒清掉圈内最多 4 颗敌弹，并回充星爆。</summary>
        void PointDefense()
        {
            var pool = BulletPool.Instance;
            if (pool == null) return;
            int cleared = 0;
            for (int i = pool.Active.Count - 1; i >= 0 && cleared < GameConfig.AegisPdClearCount; i--)
            {
                var b = pool.Active[i];
                if (b == null || b.Team != Team.Enemy) continue;
                if (Vector2.Distance(b.transform.position, transform.position) <= GameConfig.AegisPdRadius)
                {
                    pool.Release(b);
                    cleared++;
                    _owner.AddNova(GameConfig.NovaOnPointDefense);
                }
            }
            if (cleared > 0)
            {
                var line = GameConfig.Line(_owner.Weapons.ActiveId);
                Impact.Spawn(transform.position, new Color(line.Tint.r, line.Tint.g, line.Tint.b, 0.5f),
                    GameConfig.AegisPdRadius * 2f, 0.22f);
            }
        }

        public void Explode()
        {
            Impact.Spawn(transform.position, new Color(1f, 0.82f, 0.48f, 0.9f), 0.6f, 0.28f);
            Destroy(gameObject);
        }
    }
}
