using UnityEngine;

namespace AuroraRift
{
    public enum CrateKind { WeaponLevel, Drone, Shield, Charge }

    /// <summary>补给箱：缓慢下落，碰到玩家即拾取。保底逻辑在 Director（连续 3 次没出缺失项则第 4 次强制）。</summary>
    public class SupplyCrate : MonoBehaviour
    {
        public CrateKind Kind;
        public float Speed = 1.4f;

        SpriteRenderer _sr, _halo;
        float _age;

        public void Setup(CrateKind kind)
        {
            Kind = kind;
            string art = kind switch
            {
                CrateKind.WeaponLevel => "crate_weapon",
                CrateKind.Drone => "crate_drone",
                CrateKind.Shield => "crate_shield",
                _ => "crate_charge",
            };
            Color c = kind switch
            {
                CrateKind.WeaponLevel => GameConfig.Hex("FFD27A"),
                CrateKind.Drone => GameConfig.Hex("8FA4C9"),
                CrateKind.Shield => GameConfig.Hex("69F5DC"),
                _ => GameConfig.Hex("FF9AB4"),
            };

            _sr = gameObject.AddComponent<SpriteRenderer>();
            var sp = ShipArt.Get(art, 0.40f);
            if (sp != null)
            {
                _sr.sprite = sp;
                _sr.color = Color.white;
            }
            else
            {
                _sr.sprite = RuntimeSprites.Square(8);
                _sr.color = c;
            }
            _sr.sortingOrder = 12;
            transform.localScale = Vector3.one;

            var haloGo = new GameObject("Halo");
            haloGo.transform.SetParent(transform, false);
            _halo = haloGo.AddComponent<SpriteRenderer>();
            _halo.sprite = ShipArt.Get("bullet_aura_ring", 1.05f) ?? RuntimeSprites.Ring(64);
            _halo.sortingOrder = 11;
            _halo.transform.localScale = Vector3.one * 1.35f;
            _halo.color = new Color(c.r, c.g, c.b, 0.35f);
        }

        void Update()
        {
            var root = GameRoot.Instance;
            if (root == null || root.State != GameState.Playing) return;
            float dt = Time.deltaTime;
            _age += dt;
            transform.position += Vector3.down * (Speed * dt);
            transform.Rotate(0f, 0f, 90f * dt);
            _halo.transform.localScale = Vector3.one * (1.35f + Mathf.Sin(_age * 4f) * 0.10f);

            var p = root.Player;
            if (p != null && p.Alive &&
                Vector2.Distance(transform.position, p.transform.position) < 0.65f)
            {
                Collect(p);
                Destroy(gameObject);
                return;
            }

            if (transform.position.y < -GameConfig.OrthoSizeMin - 0.8f) Destroy(gameObject);
        }

        void Collect(PlayerShip p)
        {
            switch (Kind)
            {
                case CrateKind.WeaponLevel:
                    p.Weapons.GrantLevel();
                    break;
                case CrateKind.Drone:
                    p.RestoreDrone();
                    break;
                case CrateKind.Shield:
                    p.Shields = Mathf.Min(GameConfig.MaxShields, p.Shields + 1);
                    break;
                case CrateKind.Charge:
                    p.Weapons.RefillAll();
                    break;
            }
            p.AddNovaRaw(6f);
            AudioDirector.Instance?.PlayPickup();
            Director.Instance?.OnCrateCollected(Kind);
        }
    }
}
