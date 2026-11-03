using System.Collections.Generic;
using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// IMGUI 画不出圆角、发光、渐变、环形充能，这里用运行期生成的小贴图补上，
    /// 让 Unity 版界面能贴近网页版（web/www/css/game.css）的观感。
    /// 所有贴图按需生成并缓存，分辨率在游戏中是固定的，所以缓存命中率接近 100%。
    /// </summary>
    public static class UiKit
    {
        static bool _ready;
        static Texture2D _white, _disc, _hex, _triDown, _triRight, _diamond, _scan, _vignette;
        static Texture2D _gradTeal, _gradRose, _gradGold;

        static readonly Dictionary<long, Texture2D> _rounds = new Dictionary<long, Texture2D>();
        static readonly Dictionary<int, Texture2D> _arcs = new Dictionary<int, Texture2D>();

        public static Texture2D White => _white != null ? _white : (_white = Solid());
        public static Texture2D Disc => _disc != null ? _disc : (_disc = MakeDisc(64));
        public static Texture2D Hex => _hex != null ? _hex : (_hex = MakeHex(32));
        public static Texture2D TriDown => _triDown != null ? _triDown : (_triDown = MakeTri(24, true));
        public static Texture2D TriRight => _triRight != null ? _triRight : (_triRight = MakeTri(24, false));
        public static Texture2D Diamond => _diamond != null ? _diamond : (_diamond = MakeDiamond(16));
        public static Texture2D ScanTex => _scan != null ? _scan : (_scan = MakeScan(8));
        public static Texture2D VignetteTex => _vignette != null ? _vignette : (_vignette = MakeVignette(128));
        public static Texture2D GradTeal => _gradTeal != null ? _gradTeal : (_gradTeal = MakeGrad(new Color(0.57f, 0.97f, 0.91f), new Color(0.66f, 0.61f, 1f)));
        public static Texture2D GradRose => _gradRose != null ? _gradRose : (_gradRose = MakeGrad(new Color(1f, 0.44f, 0.57f), new Color(1f, 0.82f, 0.48f)));
        public static Texture2D GradGold => _gradGold != null ? _gradGold : (_gradGold = MakeGrad(new Color(1f, 0.82f, 0.48f), new Color(1f, 0.44f, 0.57f)));

        public static void Init()
        {
            if (_ready) return;
            _ready = true;
            _white = Solid();
        }

        static Texture2D _veilTop, _veilBottom;
        /// <summary>顶栏压暗带：最暗在顶边、向下渐隐（原版 .hud::before 的那条 136px 渐变）。</summary>
        public static Texture2D VeilTop => _veilTop != null ? _veilTop : (_veilTop = MakeVeil(false));
        /// <summary>底栏压暗带：最暗在底边、向上渐隐（原版 .hud::after 的那条 118px 渐变）。</summary>
        public static Texture2D VeilBottom => _veilBottom != null ? _veilBottom : (_veilBottom = MakeVeil(true));

        static Texture2D MakeVeil(bool darkAtBottom)
        {
            const int H = 96;
            var t = new Texture2D(2, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < H; y++)
            {
                float k = y / (float)(H - 1);                 // Texture2D 的 y=0 在底部
                // 顶带要「上不透明、下透明」，底带相反。之前方向写反，
                // 结果不是渐变而是一块边缘硬切的暗带（"把分数挡住"的元凶）。
                float a = darkAtBottom ? 1f - k : k;
                a = a * a * (3f - 2f * a);                    // 平滑折点，接近 CSS 的 55% 停点
                var c = new Color(1f, 1f, 1f, a);
                for (int x = 0; x < 2; x++) t.SetPixel(x, y, c);
            }
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------ 基础贴图

        static Texture2D Solid()
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
            t.Apply();
            return t;
        }

        static Texture2D MakeDisc(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = n * 0.5f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f - c, y + 0.5f - c).magnitude / c;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);            // 柔化边缘
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D MakeHex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = n * 0.5f, r = c - 0.5f;
            var px = new Color32[n * n];
            var pts = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * (60f * i - 90f);
                pts[i] = new Vector2(c + Mathf.Cos(a) * r, c + Mathf.Sin(a) * r);
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = true;
                    for (int i = 0; i < 6 && inside; i++)
                    {
                        Vector2 a = pts[i], b = pts[(i + 1) % 6];
                        Vector2 e = b - a, d = p - a;
                        inside = e.x * d.y - e.y * d.x >= 0f;   // 逆时针
                    }
                    px[y * n + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D MakeTri(int n, bool down)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fy = down ? 1f - (y + 0.5f) / n : (y + 0.5f) / n;
                    float half = 0.5f * fy;
                    float fx = (x + 0.5f) / n - 0.5f;
                    bool inside = Mathf.Abs(fx) <= half;
                    px[y * n + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D MakeDiamond(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = n * 0.5f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Abs(x + 0.5f - c) + Mathf.Abs(y + 0.5f - c);
                    px[y * n + x] = new Color32(255, 255, 255, d <= c - 0.4f ? (byte)255 : (byte)0);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D MakeScan(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    byte a = (byte)(y == 0 ? 255 : 0);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D MakeVignette(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = n * 0.5f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2((x + 0.5f - c) / c, (y + 0.5f - c) / c).magnitude;
                    float a = Mathf.Clamp01((d - 0.55f) / 0.75f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D MakeGrad(Color a, Color b)
        {
            const int n = 128;
            var t = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                var c = Color.Lerp(a, b, i / (n - 1f));
                px[i] = c;
            }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        // ------------------------------------------------------------ 圆角面板

        static Texture2D RoundedTex(float fw, float fh, float radius, bool fill)
        {
            int w = Mathf.Max(4, Mathf.RoundToInt(fw));
            int h = Mathf.Max(4, Mathf.RoundToInt(fh));
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(w, h) * 0.5f);
            long key = ((long)w << 40) ^ ((long)h << 20) ^ (long)(radius * 10f) ^ (fill ? 0x4000000000000000L : 0L);
            if (_rounds.TryGetValue(key, out var tex) && tex != null) return tex;
            if (_rounds.Count > 120) _rounds.Clear();            // 分辨率变了就重来，别无限涨

            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            float r = radius;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fx = Mathf.Min(x + 0.5f, w - x - 0.5f);
                    float fy = Mathf.Min(y + 0.5f, h - y - 0.5f);
                    float a;
                    if (fx >= r || fy >= r) a = Mathf.Clamp01(Mathf.Min(fx, fy) + 0.5f);
                    else
                    {
                        float dx = r - fx, dy = r - fy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) - (r - 0.5f);
                        a = Mathf.Clamp01(0.5f - d);
                    }

                    byte alpha;
                    if (fill) alpha = (byte)(a * 255f);
                    else
                    {
                        // 描边：只在边缘 1.6px 内不透明
                        float edge = 0f;
                        if (fx >= r || fy >= r) edge = Mathf.Clamp01(1f - (Mathf.Min(fx, fy) - 0f) / 1.6f);
                        else
                        {
                            float dx = r - fx, dy = r - fy;
                            float d = Mathf.Sqrt(dx * dx + dy * dy) - (r - 0.5f);
                            edge = Mathf.Clamp01(1f - Mathf.Abs(d) / 1.6f);
                        }
                        alpha = (byte)(edge * a * 255f);
                    }
                    px[y * w + x] = new Color32(255, 255, 255, alpha);
                }
            tex.SetPixels32(px); tex.Apply();
            _rounds[key] = tex;
            return tex;
        }

        /// <summary>圆角面板：填充 + 描边（各一次 draw call）。</summary>
        public static void Panel(Rect r, Color fill, Color border, float radius = 8f)
        {
            if (r.width < 2f || r.height < 2f) return;
            var prev = GUI.color;
            if (fill.a > 0.001f)
            {
                GUI.color = fill;
                GUI.DrawTexture(r, RoundedTex(r.width, r.height, radius, true));
            }
            if (border.a > 0.001f)
            {
                GUI.color = border;
                GUI.DrawTexture(r, RoundedTex(r.width, r.height, radius, false));
            }
            GUI.color = prev;
        }

        /// <summary>胶囊（全圆角）。</summary>
        public static void Pill(Rect r, Color fill, Color border) => Panel(r, fill, border, r.height * 0.5f);

        /// <summary>环形充能：从 12 点顺时针填充 pct（0..1）。</summary>
        public static void Arc(Rect r, float pct, Color color, float thickness = 3f)
        {
            int key = Mathf.RoundToInt(Mathf.Clamp01(pct) * 200f);
            if (key <= 0) return;
            int size = Mathf.Max(24, Mathf.RoundToInt(r.width));
            int cacheKey = key * 4096 + Mathf.RoundToInt(thickness * 10f);
            if (!_arcs.TryGetValue(cacheKey, out var tex) || tex == null || tex.width != size)
            {
                if (_arcs.Count > 240) _arcs.Clear();
                tex = MakeArc(size, key / 200f, thickness);
                _arcs[cacheKey] = tex;
            }
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
        }

        static Texture2D MakeArc(int n, float pct, float thickness)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = n * 0.5f;
            float rad = c - thickness * 0.5f - 1f;
            float end = pct * 360f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - rad) / (thickness * 0.5f));
                    if (a <= 0f) { px[y * n + x] = new Color32(255, 255, 255, 0); continue; }
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 90f;   // 12 点 = 0
                    if (ang < 0f) ang += 360f;
                    bool on = ang <= end;
                    px[y * n + x] = new Color32(255, 255, 255, on ? (byte)(a * 255f) : (byte)0);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        /// <summary>柔和光晕（径向渐变，用来做按钮/标题的辉光）。</summary>
        public static void Glow(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Disc);
            GUI.color = prev;
        }

        public static void Sprite(Rect r, Texture2D tex, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
        }

        /// <summary>横向渐变矩形（用来做主按钮）。</summary>
        public static void Gradient(Rect r, Texture2D grad, Color tint)
        {
            var prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, grad);
            GUI.color = prev;
        }

        /// <summary>扫描线（每 8 参考像素一条），alpha 很低，只加质感。</summary>
        public static void Scanlines(Rect r, float alpha)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            var uv = new Rect(0f, 0f, r.width / 8f, r.height / 8f);
            GUI.DrawTextureWithTexCoords(r, ScanTex, uv);
            GUI.color = prev;
        }

        public static void Vignette(Rect r, float alpha)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(r, VignetteTex);
            GUI.color = prev;
        }

        public static void Line(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }

        // ------------------------------------------------------------ 渐变圆角面板

        static readonly Dictionary<long, Texture2D> _gradPanels = new Dictionary<long, Texture2D>();

        /// <summary>带垂直渐变的圆角面板（主按钮 / Boss 血条这类少量元素用）。</summary>
        public static void PanelGrad(Rect r, Color top, Color bottom, Color border, float radius = 8f)
        {
            if (r.width < 2f || r.height < 2f) return;
            int w = Mathf.Max(4, Mathf.RoundToInt(r.width));
            int h = Mathf.Max(4, Mathf.RoundToInt(r.height));
            float rad = Mathf.Clamp(radius, 0f, Mathf.Min(w, h) * 0.5f);
            long key = ((long)w << 44) ^ ((long)h << 24) ^ (long)(rad * 10f)
                       ^ ((long)(top.GetHashCode() & 0xFFFFF) << 2) ^ (long)(bottom.GetHashCode() & 0x3);
            if (!_gradPanels.TryGetValue(key, out var tex) || tex == null)
            {
                if (_gradPanels.Count > 40) _gradPanels.Clear();
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    var rowColor = Color.Lerp(bottom, top, h <= 1 ? 0f : (float)y / (h - 1));   // 贴图 y 向上
                    for (int x = 0; x < w; x++)
                    {
                        float fx = Mathf.Min(x + 0.5f, w - x - 0.5f);
                        float fy = Mathf.Min(y + 0.5f, h - y - 0.5f);
                        float a;
                        if (fx >= rad || fy >= rad) a = Mathf.Clamp01(Mathf.Min(fx, fy) + 0.5f);
                        else
                        {
                            float dx = rad - fx, dy = rad - fy;
                            float d = Mathf.Sqrt(dx * dx + dy * dy) - (rad - 0.5f);
                            a = Mathf.Clamp01(0.5f - d);
                        }
                        var c = rowColor;
                        c.a *= a;
                        px[y * w + x] = c;
                    }
                }
                tex.SetPixels32(px); tex.Apply();
                _gradPanels[key] = tex;
            }
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
            if (border.a > 0.001f) Panel(r, new Color(0, 0, 0, 0), border, radius);
        }
    }
}
