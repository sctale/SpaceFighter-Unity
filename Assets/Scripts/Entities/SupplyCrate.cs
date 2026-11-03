using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 补给箱：与网页版同一套 9 类掉落（升级/护盾/僚机/棱镜线/蜂群线/力场/命/宝石/换线）。
    /// 拾取半径 30px，缓慢下落；换线箱是青色，专门治「一把武器骑到底」。
    /// </summary>
    public enum CrateKind { WeaponLevel, Shield, Drone, Prism, Swarm, Reflect, Life, Gem, Swap, Gems }

    public class SupplyCrate : MonoBehaviour
    {
        public CrateKind Kind;
        public float Speed = 1.4f;

        SpriteRenderer _sr, _halo;
        float _age;

        static string ArtName(CrateKind k) => k switch
        {
            CrateKind.WeaponLevel => "crate_weapon",
            CrateKind.Drone => "crate_drone",
            CrateKind.Shield => "crate_shield",
            CrateKind.Prism => "crate_prism",
            CrateKind.Swarm => "crate_swarm",
            CrateKind.Reflect => "crate_reflect",
            CrateKind.Life => "crate_life",
            CrateKind.Gem => "crate_gem",
            CrateKind.Swap => "crate_swap",
            _ => "crate_gem",
        };

        static Color TintOf(CrateKind k) => k switch
        {
            CrateKind.WeaponLevel => GameConfig.Hex("FFD27A"),
            CrateKind.Drone => GameConfig.Hex("8FA4C9"),
            CrateKind.Shield => GameConfig.Hex("9B8CFF"),
            CrateKind.Prism => GameConfig.Hex("FF6F91"),
            CrateKind.Swarm => GameConfig.Hex("FFC24B"),
            CrateKind.Reflect => GameConfig.Hex("FFD27A"),
            CrateKind.Life => GameConfig.Hex("FF6F91"),
            CrateKind.Gem => GameConfig.Hex("FFE9B0"),
            CrateKind.Swap => GameConfig.Hex("69F5DC"),
            _ => GameConfig.Hex("FFE9B0"),
        };

        public void Setup(CrateKind kind)
        {
            Kind = kind;
            var c = TintOf(kind);

            _sr = gameObject.AddComponent<SpriteRenderer>();
            float height = kind == CrateKind.Gem ? 0.30f : 0.40f;
            var sp = ShipArt.Get(ArtName(kind), height);
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
            _halo.sprite = ShipArt.Get("bullet_glow_soft", 1.1f) ?? RuntimeSprites.Ring(64);
            _halo.sortingOrder = 11;
            _halo.transform.localScale = Vector3.one;
            _halo.color = new Color(c.r, c.g, c.b, 0.30f);
        }

        void Update()
        {
            var root = GameRoot.Instance;
            if (root == null || root.State != GameState.Playing) return;
            float dt = Time.deltaTime;
            _age += dt;
            transform.position += Vector3.down * (Speed * dt);
            if (Kind != CrateKind.Gem) transform.Rotate(0f, 0f, 42f * dt);
            _halo.transform.localScale = Vector3.one * (0.9f + Mathf.Sin(_age * 4f) * 0.08f);

            var p = root.Player;
            if (p != null && p.Alive &&
                Vector2.Distance(transform.position, p.transform.position) < 30f * GameConfig.Px)
            {
                Collect(p);
                Destroy(gameObject);
                return;
            }

            if (transform.position.y < GameView.DespawnY) Destroy(gameObject);
        }

        void Collect(PlayerShip p)
        {
            var root = GameRoot.Instance;
            switch (Kind)
            {
                case CrateKind.Gem:
                    root?.AddScore(25 * (root.Multiplier));
                    p.AddNova(GameConfig.NovaOnGem);
                    break;
                case CrateKind.WeaponLevel:
                    if (!p.Weapons.GrantLevel()) root?.AddScore(800);
                    root?.ShowToast("POWER LV." + p.Weapons.Active.Level);
                    break;
                case CrateKind.Shield:
                    if (p.Shields < GameConfig.MaxShields) { p.Shields++; root?.ShowToast("SHIELD " + p.Shields + "/3"); }
                    else root?.AddScore(500);
                    break;
                case CrateKind.Drone:
                    // 原版满僚机时给 600 分（game.js:1170），不是 300
                    if (p.DroneCount < GameConfig.MaxDrones) { p.RestoreDrone(); root?.ShowToast("僚机归队"); }
                    else root?.AddScore(600);
                    break;
                case CrateKind.Prism:
                    // 已解锁且已满级时原版走 +1200 分支（game.js:1136-1139）
                    if (!p.Weapons.GrantLine("prism")) root?.AddScore(1200);
                    root?.ShowToast("棱镜线解锁/升级");
                    break;
                case CrateKind.Swarm:
                    if (!p.Weapons.GrantLine("swarm")) root?.AddScore(1200);
                    root?.ShowToast("蜂群线解锁/升级");
                    break;
                case CrateKind.Reflect:
                    p.GrantReflect();
                    root?.ShowToast("PRISM FIELD 8s");
                    break;
                case CrateKind.Life:
                    // 满命（5）时原版折成 1000 分（game.js:1183）
                    if (p.Hull < 5) { p.Hull++; root?.ShowToast("舰体 +1"); }
                    else root?.AddScore(1000);
                    break;
                case CrateKind.Swap:
                    bool ok = p.Weapons.SwapToBest(30f);
                    root?.ShowToast(ok ? "武器切换 · " + GameConfig.Line(p.Weapons.ActiveId).Label : "三线回充");
                    break;
            }
            AudioDirector.Instance?.PlayPickup();
            Director.Instance?.OnCrateCollected(Kind);
        }
    }
}
