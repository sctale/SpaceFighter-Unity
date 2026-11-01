using System.Collections.Generic;
using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 弹体池。批量子弹必须有池，否则每帧 new GameObject 会把手机拖死。
    /// </summary>
    public class BulletPool : MonoBehaviour
    {
        public static BulletPool Instance;

        readonly Stack<Projectile> _free = new Stack<Projectile>();
        readonly List<Projectile> _active = new List<Projectile>();
        Sprite _dot;

        void Awake()
        {
            Instance = this;
            _dot = RuntimeSprites.Disc(32);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var p = _active[i];
                if (p == null) { _active.RemoveAt(i); continue; }
                p.Tick(dt);
            }
        }

        public Projectile Spawn(Team team, Vector2 pos, Vector2 vel, float damage, float radius, Color tint)
        {
            Projectile p = _free.Count > 0 ? _free.Pop() : NewBullet();
            p.gameObject.SetActive(true);
            p.transform.SetPositionAndRotation(pos, Quaternion.identity);
            p.Life = GameConfig.BulletLifetime;
            p.Homing = false;
            p.HomingTarget = null;
            p.Piercing = false;
            p.name = team == Team.Player ? "PBullet" : "EBullet";
            p.Init(team, vel, damage, radius, tint);
            _active.Add(p);
            return p;
        }

        public void Release(Projectile p)
        {
            if (p == null) return;
            _active.Remove(p);
            p.gameObject.SetActive(false);
            _free.Push(p);
        }

        Projectile NewBullet()
        {
            var go = new GameObject("Bullet");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _dot;
            sr.sortingOrder = 20;
            var p = go.AddComponent<Projectile>();
            return p;
        }

        /// <summary>供玩家/敌人做圆形判定用（弹幕不吃物理）。</summary>
        public IReadOnlyList<Projectile> Active => _active;
    }

    /// <summary>运行时生成 sprite，避免依赖美术资源导入。</summary>
    public static class RuntimeSprites
    {
        static Sprite _disc, _square, _ring;
        static Sprite _arrow, _diamond, _hex, _rock, _capsule, _softRect;

        public static Sprite Disc(int px = 48) => _disc ? _disc : (_disc = Make(px, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(px / 2f, px / 2f)) / (px / 2f);
            return Mathf.Clamp01(1f - d * 1.15f);
        }));

        public static Sprite Square(int px = 8) => _square ? _square : (_square = Make(px, (x, y) => 1f));

        public static Sprite Ring(int px = 64) => _ring ? _ring : (_ring = Make(px, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(px / 2f, px / 2f)) / (px / 2f);
            return Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) * 14f);
        }));

        // ---- 机体/弹体轮廓（单位多边形，归一化到 [-1,1]）----

        static readonly Vector2[] ArrowPoly =
        {
            new Vector2(0f, 1f), new Vector2(0.85f, -0.75f), new Vector2(0f, -0.35f), new Vector2(-0.85f, -0.75f),
        };

        static readonly Vector2[] DiamondPoly =
        {
            new Vector2(0f, 1f), new Vector2(0.62f, 0f), new Vector2(0f, -1f), new Vector2(-0.62f, 0f),
        };

        static readonly Vector2[] HexPoly =
        {
            new Vector2(0f, 1f), new Vector2(0.87f, 0.5f), new Vector2(0.87f, -0.5f),
            new Vector2(0f, -1f), new Vector2(-0.87f, -0.5f), new Vector2(-0.87f, 0.5f),
        };

        static readonly Vector2[] CapsulePoly =
        {
            new Vector2(-0.35f, 0.85f), new Vector2(0.35f, 0.85f), new Vector2(0.5f, 0.55f), new Vector2(0.5f, -0.55f),
            new Vector2(0.35f, -0.85f), new Vector2(-0.35f, -0.85f), new Vector2(-0.5f, -0.55f), new Vector2(-0.5f, 0.55f),
        };

        static Vector2[] RockPoly
        {
            get
            {
                var rnd = new System.Random(20261101);
                var pts = new Vector2[9];
                for (int i = 0; i < pts.Length; i++)
                {
                    float a = i / (float)pts.Length * Mathf.PI * 2f;
                    float r = 0.62f + (float)rnd.NextDouble() * 0.38f;
                    pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                }
                return pts;
            }
        }

        public static Sprite Arrow(int px = 64) => _arrow ? _arrow : (_arrow = Poly(px, ArrowPoly));
        public static Sprite Diamond(int px = 48) => _diamond ? _diamond : (_diamond = Poly(px, DiamondPoly));
        public static Sprite Hex(int px = 64) => _hex ? _hex : (_hex = Poly(px, HexPoly));
        public static Sprite Rock(int px = 64) => _rock ? _rock : (_rock = Poly(px, RockPoly));
        public static Sprite Capsule(int px = 32) => _capsule ? _capsule : (_capsule = Poly(px, CapsulePoly));

        /// <summary>上下渐隐的软边矩形：极光/光纱这类氛围条带用。</summary>
        public static Sprite SoftRect(int px = 64) => _softRect ? _softRect : (_softRect = Make(px, (x, y) =>
        {
            float v = Mathf.Abs(y / (float)px - 0.5f) * 2f;   // 0 中心 → 1 边缘
            return Mathf.Clamp01(1f - v * v);
        }));

        /// <summary>把凸/凹多边形栅格化成 sprite，2×2 超采样做边缘抗锯齿。</summary>
        public static Sprite Poly(int size, Vector2[] poly)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float acc = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float u = (x + 0.25f + sx * 0.5f) / size * 2f - 1f;
                            float v = (y + 0.25f + sy * 0.5f) / size * 2f - 1f;
                            if (Inside(poly, new Vector2(u, v))) acc += 1f;
                        }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(acc / 4f * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        static Sprite Make(int size, System.Func<int, int, float> alphaAt)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = alphaAt(x, y);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
