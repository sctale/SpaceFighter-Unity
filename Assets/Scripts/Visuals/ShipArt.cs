using System.Collections.Generic;
using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 舰船精灵加载器。PNG 由 artgen/gen_ships.py 生成到 Assets/Resources/Ships/，
    /// 运行期按“画布世界高度”建 Sprite（保持每架机体的视觉半径 = 判定半径）。
    /// 找不到资源时返回 null，调用方回退到 RuntimeSprites，保证构建不缺图也能跑。
    /// </summary>
    public static class ShipArt
    {
        static readonly Dictionary<string, Sprite> Cache = new();
        static readonly HashSet<string> Warned = new();

        public static Sprite Get(string name, float worldHeight)
        {
            if (string.IsNullOrEmpty(name) || worldHeight <= 0f) return null;
            string key = name + "@" + worldHeight.ToString("F3");
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var tex = Resources.Load<Texture2D>("Ships/" + name);
            if (tex == null)
            {
                if (Warned.Add(name))
                    Debug.LogWarning("[Aurora] missing ship art: Resources/Ships/" + name + ".png");
                return null;
            }

            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 1;

            float ppu = tex.height / worldHeight;
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>命中闪白用的白剪影（同尺寸、同 pivot）。</summary>
        public static Sprite Mask(string name, float worldHeight) => Get(name + "_mask", worldHeight);

        /// <summary>美术画布的有效半径比例：绘制时最大半径 = 0.46 × 画布边长。</summary>
        public const float ArtExtent = 0.46f;

        /// <summary>按“机体半径”取精灵：视觉外缘正好落在 radius 上。</summary>
        public static Sprite ForRadius(string name, float radius, float pad = 1.0f)
            => Get(name, radius / ArtExtent * pad);
    }
}
