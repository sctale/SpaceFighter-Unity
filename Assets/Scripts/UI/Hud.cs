using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 界面层：照着网页版 web/www（index.html + css/game.css + ui.js）重做的四层界面。
    /// 参考分辨率 1080×1920，按屏幕高度等比缩放；所有字号、间距、色值来自网页版设计令牌。
    ///
    /// 结构：
    ///   开始菜单  —— 品牌标记 / 大标题 / 特性胶囊 / 战绩卡 / 三档难度 / 开始按钮 / 6 条操作提示
    ///   战斗 HUD  —— 顶栏（分数·最佳·擦弹 / 星区 / 暂停）、连锁盒、Boss 卡、底部仪表带、公告、提示、吐司
    ///   暂停页    —— 装配摘要 + 4 行设置 + 继续/重开/返回基地
    ///   结算页    —— 分数卡 + 评级圆章 + 10 项数据 + 装配摘要 + 再次出击/返回基地
    ///
    /// 注意：IMGUI 的 GUI.skin 在裁剪包里可能为空，所以样式全部自建（不继承内置样式）。
    /// 触屏控件的热区在 Controls 里，这里只负责画——不要再挂 GUI.Button，否则一次触摸会触发两次。
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public static Hud Instance;

        const float RefH = 1920f;

        // ---------------------------------------------------------------- 设计令牌
        static Color Ink0 => GameConfig.Hex("04060F");
        static Color Paper => GameConfig.Hex("F3F8FF");
        static Color InkText => GameConfig.Hex("E8EEFF");
        static Color Muted => GameConfig.Hex("8D9BBD");
        static Color Dimc => GameConfig.Hex("5E6B8C");
        static Color Teal => GameConfig.Hex("69F5DC");
        static Color TealDim => GameConfig.Hex("36C7B4");
        static Color Violet => GameConfig.Hex("9B8CFF");
        static Color Rose => GameConfig.Hex("FF6F91");
        static Color Gold => GameConfig.Hex("FFD27A");
        static Color Line(float a) => new Color(0.63f, 0.71f, 1f, a);

        Font _font, _bold;
        GUIStyle _eyebrow, _tiny, _small, _body, _num, _numBig, _title, _h1, _h1c, _h2, _center,
                 _right, _btn, _btnBig, _tag, _kbd, _wrap;

        GameState _lastState = GameState.Menu;
        float _stateAt;
        float _hitFx;
        Color _hitTint = Color.white;

        // ---------------------------------------------------------------- 生命周期

        void Awake()
        {
            Instance = this;
            UiKit.Init();

            // 安卓上 CreateDynamicFontFromOSFont 只有 Roboto，中文会整段不显示：
            // 随包带 Noto Sans CJK SC 子集字体（拉丁字母 + UI 用到的全部汉字）。
            _font = Resources.Load<Font>("Fonts/AuroraCJK");
            if (_font == null)
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Droid Sans Fallback", "sans-serif" }, 28);
            // 没有粗体字重：用同一字体叠画偏移代替（见 BoldText）
            _bold = _font;
        }

        void OnGUI()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            EnsureStyles();

            float sc = Screen.height / RefH;
            if (sc <= 0.01f) sc = 1f;
            float w = Screen.width / sc;

            var safe = Screen.safeArea;
            float top = (Screen.height - safe.yMax) / sc;
            float bottom = safe.yMin / sc;

            if (root.State != _lastState) { _lastState = root.State; _stateAt = Time.unscaledTime; }

            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(sc, sc, 1f));
            var prevColor = GUI.color;

            DrawStageFx(w, root);

            switch (root.State)
            {
                case GameState.Menu: DrawMenu(root, w, top, bottom); break;
                case GameState.Paused:
                    DrawCombat(root, w, top, bottom);
                    DrawPause(root, w, top, bottom);
                    break;
                case GameState.Dead:
                    DrawCombat(root, w, top, bottom);
                    DrawDead(root, w, top, bottom);
                    break;
                default: DrawCombat(root, w, top, bottom); break;
            }

            GUI.color = prevColor;
            GUI.matrix = prev;
        }

        /// <summary>受击反馈：屏幕边缘短促泛色（护盾紫 / 舰体玫红 / 力场金）。</summary>
        public void HitFx(Color tint, float strength = 1f)
        {
            _hitTint = tint;
            _hitFx = Mathf.Max(_hitFx, strength);
        }

        /// <summary>扫描线 + 暗角：网页版 .stage-scan / .stage-vignette 的效果。</summary>
        void DrawStageFx(float w, GameRoot root)
        {
            if (root.LowFx) { UiKit.Vignette(new Rect(0, 0, w, RefH), 0.35f); return; }
            UiKit.Scanlines(new Rect(0, 0, w, RefH), 0.055f);
            UiKit.Vignette(new Rect(0, 0, w, RefH), 0.55f);
            if (_hitFx > 0.002f)
            {
                _hitFx = Mathf.Max(0f, _hitFx - Time.unscaledDeltaTime * 5.5f);
                float k = _hitFx * _hitFx;
                UiKit.Vignette(new Rect(0, 0, w, RefH), 0.62f * k, _hitTint);
            }
            // 低血量：边缘一圈玫红脉冲
            var p = root.Player;
            if (root.State == GameState.Playing && p != null && p.Hull <= 1)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f);
                UiKit.Vignette(new Rect(0, 0, w, RefH), 0.18f + pulse * 0.16f);
            }
        }

        // ---------------------------------------------------------------- 样式

        void EnsureStyles()
        {
            if (_body != null) return;
            _eyebrow = Label(17, Muted);
            _tiny = Label(15, Dimc);
            _small = Label(19, Muted);
            _body = Label(21, InkText);
            _num = Label(40, Paper);
            _numBig = Label(50, Paper);   // 原版 24.8 CSS；62 会溢出 52 的框、压住 BEST 行
            _title = Label(118, Paper);

            _h1 = Label(58, Paper, TextAnchor.MiddleLeft);
            _h1c = Label(58, Paper, TextAnchor.MiddleCenter);
            _h2 = Label(23, Dimc, TextAnchor.MiddleLeft);
            _tag = Label(16, Rose, TextAnchor.MiddleCenter);
            _kbd = Label(14, Dimc, TextAnchor.MiddleCenter);
            _btn = Label(26, InkText, TextAnchor.MiddleCenter);
            _btnBig = Label(29, new Color(0.02f, 0.06f, 0.11f), TextAnchor.MiddleCenter);

            var c = Label(21, InkText, TextAnchor.MiddleCenter); _center = c;
            _right = Label(19, Muted, TextAnchor.MiddleRight);
            _wrap = Label(20, Muted, TextAnchor.UpperLeft);
            _wrap.wordWrap = true;

            // 只吃输入、不画背景的按钮样式（HUD 里所有可点元素都用它）
            _invisible = new GUIStyle { normal = { background = null }, hover = { background = null }, active = { background = null } };
        }

        GUIStyle Label(int size, Color color, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            return new GUIStyle
            {
                font = _font,
                fontSize = size,
                alignment = anchor,
                wordWrap = false,
                clipping = TextClipping.Clip,
                richText = false,
                // 颜色一律由 GUI.color 在绘制时给：样式自带颜色会和它相乘，导致字被"暗两次"
                normal = { textColor = Color.white },
            };
        }

        // ---------------------------------------------------------------- 文字

        void Txt(GUIStyle st, Rect r, string s, Color c) => Txt(st, r, s, c, null);

        void Txt(GUIStyle st, Rect r, string s, Color c, TextAnchor? anchor)
        {
            if (string.IsNullOrEmpty(s)) return;
            var prev = GUI.color;
            GUI.color = c;
            var style = st;
            if (anchor.HasValue && st.alignment != anchor.Value)
                style = new GUIStyle(st) { alignment = anchor.Value };
            GUI.Label(r, s, style);
            GUI.color = prev;
        }

        /// <summary>粗体感：同一串字叠画一次微小偏移（Noto 只有 Regular 字重）。</summary>
        void Bold(GUIStyle st, Rect r, string s, Color c, float px = 2.4f)
        {
            Txt(st, new Rect(r.x + px * 0.5f, r.y, r.width + px, r.height), s, c);
            Txt(st, new Rect(r.x - px * 0.5f, r.y, r.width + px, r.height), s, c);
        }

        /// <summary>带辉光的标题（菜单大标题 / 结算分数）。</summary>
        void GlowText(GUIStyle st, Rect r, string s, Color c, Color glow, TextAnchor anchor)
        {
            var g = st;
            if (g.alignment != anchor) g = new GUIStyle(st) { alignment = anchor };
            Txt(g, new Rect(r.x - 2, r.y, r.width, r.height), s, new Color(glow.r, glow.g, glow.b, 0.25f));
            Txt(g, new Rect(r.x + 2, r.y + 2, r.width, r.height), s, new Color(glow.r, glow.g, glow.b, 0.18f));
            Bold(g, r, s, c, 2.6f);
        }

        static Rect R(float x, float y, float w, float h) => new Rect(x, y, w, h);
        static Rect Center(float w, float y, float width, float height) => new Rect(w * 0.5f - width * 0.5f, y, width, height);
        static Rect Cx(float cx, float cy, float w, float h) => new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);

        // ---------------------------------------------------------------- 开始菜单

        void DrawMenu(GameRoot root, float w, float top, float bottom)
        {
            var dir = Director.Instance;
            float t = Time.unscaledTime - _stateAt;
            float pad = 36.8f;                       // --pad @540
            float cx = w * 0.5f;
            float colMax = 680f;                     // 原版正文列宽 340px

            // 背景压暗 + 顶部网格光
            var scrim = new Rect(0, 0, w, RefH);
            GUI.color = new Color(0.016f, 0.024f, 0.059f, 0.93f);
            GUI.DrawTexture(scrim, UiKit.White);
            GUI.color = Color.white;
            UiKit.Glow(R(0, -220, w * 1.4f, 900), new Color(0.41f, 0.96f, 0.86f, 0.10f));

            // 品牌标记从 86.4px 起（原版 .brand margin-top），菱形 46px
            float y = top + 172.8f;

            // --- 品牌标记：双层菱形环 + 金色核心（嵌套菱形做环）---
            {
                float mc = cx, my = y + 46f;
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 1.7f) * 0.035f;
                UiKit.Glow(Cx(mc, my, 190f, 190f), new Color(Gold.r, Gold.g, Gold.b, 0.10f));
                DiamondRing(mc, my, 92f * pulse, Teal);
                DiamondRing(mc, my, 65f * pulse, Violet);
                UiKit.Sprite(Cx(mc, my, 26f * pulse, 26f * pulse), UiKit.Diamond, Gold);
                y += 136f;
            }

            float rise1 = Rise(t, 0.02f);
            Txt(_eyebrow, Center(w, y + rise1, 700, 24), Sp("DEEP SPACE ARCADE"), TealDim);
            y += 44f;

            float rise2 = Rise(t, 0.06f);
            // 标题按实测字高推进：写死行距会让 118 号字溢出文本框、压住下面的副标题
            var titleContent = new GUIContent("太空战斗机");
            float titleH = _title.CalcSize(titleContent).y;
            GlowText(_title, Center(w, y + rise2, w - 80, titleH + 6f), "太空战斗机", Paper,
                new Color(Teal.r, Teal.g, Teal.b, 1f), TextAnchor.MiddleCenter);
            y += titleH + 30f;

            TextWithRules(Center(w, y, 620, 26), "AURORA RIFT   //   星穹涅槃", Muted, Line(0.28f));
            y += 46f;
            Txt(_small, Center(w, y, 700, 30), "穿过裂隙，在弹幕之间起舞。", Muted);
            y += 62f;

            // --- 特性胶囊（两列）---
            string[] chips = { "12 种敌机 · 3 词缀", "22 种 Boss 招式 · 破防", "相位冲刺", "3 条武器线 ×5 级", "4 类僚机 · 3 层护盾", "10 个星区" };
            float chipW = 300f;
            for (int i = 0; i < chips.Length; i++)
            {
                int col = i % 2, row = i / 2;
                var r = R(cx - chipW - 6f + col * (chipW + 12f), y + row * 40f, chipW, 34f);
                UiKit.Pill(r, new Color(0.027f, 0.043f, 0.10f, 0.55f), Line(0.16f));
                Txt(_tiny, r, chips[i], new Color(Muted.r, Muted.g, Muted.b, 1f));
            }
            y += 3 * 40f + 18f;

            // --- 战绩卡 ---
            {
                var card = Center(w, y, 640f, 132f);
                UiKit.Panel(card, new Color(0.027f, 0.043f, 0.10f, 0.52f), Line(0.13f), 16f);
                Txt(_tiny, R(card.x + 40f, card.y + 26f, 260f, 24f), Sp("BEST SCORE"), Dimc);
                Bold(_num, R(card.x + 40f, card.y + 54f, 300f, 56f), root.Best.ToString(), Paper, 2f);
                UiKit.Line(R(card.center.x, card.y + 30f, 2f, 72f), Line(0.16f));
                Txt(_tiny, R(card.center.x + 40f, card.y + 26f, 260f, 24f), "最远星区", Dimc);
                Bold(_num, R(card.center.x + 40f, card.y + 54f, 300f, 56f),
                    (dir != null ? dir.Zone : 1).ToString("00"), Paper, 2f);
                y += 168f;
            }

            // --- 难度三选 ---
            var diffs = Difficulties.All;
            float pickW = 640f, segW = (pickW - 12f) / 3f;
            var picker = Center(w, y, pickW, 88f);
            UiKit.Pill(picker, new Color(0.027f, 0.043f, 0.10f, 0.55f), Line(0.13f));
            for (int i = 0; i < diffs.Length; i++)
            {
                var r = R(picker.x + 6f + i * segW, picker.y + 6f, segW - 4f, 76f);
                bool on = root.Difficulty.Id == diffs[i].Id;
                var accent = diffs[i].Id == "abyss" ? Rose : Teal;
                if (on)
                    UiKit.Pill(r, new Color(accent.r, accent.g, accent.b, 0.14f), new Color(accent.r, accent.g, accent.b, 0.5f));
                // 只留中文名并垂直居中：原来在 42px 高的格子里塞两行，EN 副标必然压在框沿上
                Txt(Label(22, on ? accent : Muted, TextAnchor.MiddleCenter), r, diffs[i].Name,
                    new Color(on ? accent.r : Muted.r, on ? accent.g : Muted.g, on ? accent.b : Muted.b,
                              on ? 1f : 0.9f));
                if (GUI.Button(r, GUIContent.none, _invisible)) root.SetDifficulty(diffs[i]);
            }
            y += 104f;
            Txt(_tiny, Center(w, y, 700, 26), root.Difficulty.Note, new Color(Muted.r, Muted.g, Muted.b, 0.85f));
            // 20:9 的大屏上「难度 → 开始键」之间会空出一大截（1080×1920 实测）：
            // 把这段余量分给标题下方与开始键上方，让整屏呼吸均匀，而不是全部堆在底部。
            float hintsTopEarly = RefH - bottom - 232.8f;
            float spare = Mathf.Max(0f, hintsTopEarly - 300f - (y + 54f));
            y += Mathf.Min(spare * 0.55f, 230f);
            y += 54f;

            // --- 开始按钮 ---
            {
                var btn = Center(w, y, 560f, 108f);
                var prev = GUI.color;
                UIButtonFrame(btn, true);
                Txt(_btnBig, btn, "开始巡航", new Color(0.02f, 0.06f, 0.11f, 1f));
                Txt(_kbd, R(btn.xMax - 56f, btn.y + 44f, 44f, 20f), "ENTER", new Color(0.02f, 0.06f, 0.11f, 0.45f));
                // 播放三角
                UiKit.Sprite(Cx(btn.x + 58f, btn.center.y, 22f, 26f), UiKit.TriRight, new Color(0.02f, 0.06f, 0.11f, 0.9f));
                if (GUI.Button(btn, GUIContent.none, _invisible)) root.StartRun();
                GUI.color = prev;
            }

            // --- 操作提示（原版 340px 列宽、两列 165px、行距 32px，末行底边贴在 919.6px）---
            float hintsTop = RefH - bottom - 232.8f;
            string[][] hints =
            {
                new[] { "拖动", "飞行 · 自动开火" },
                new[] { "武器卡", "点按切换武器线" },
                new[] { "精准", "慢速 + 收束 + 判定点" },
                new[] { "冲刺", "穿弹短暂无敌" },
                new[] { "星爆", "擦弹充能后释放" },
                new[] { "暂停", "右上角 II 或 P" },
            };
            float colW = 330f, rowH = 64f;
            for (int i = 0; i < hints.Length; i++)
            {
                int col = i % 2, row = i / 2;
                var r = R(cx - colW - 6f + col * (colW + 12f), hintsTop + row * rowH, colW, rowH - 10f);
                UiKit.Panel(r, new Color(0.027f, 0.043f, 0.10f, 0.34f), Line(0.09f), 8f);
                var key = R(r.x + 10f, r.y + (r.height - 32f) * 0.5f, 68f, 32f);
                UiKit.Panel(key, new Color(0.04f, 0.067f, 0.14f, 0.6f), Line(0.16f), 4f);
                Txt(_kbd, key, hints[i][0], Teal);
                Txt(_small, R(key.xMax + 12f, r.y, r.width - key.width - 22f, r.height), hints[i][1],
                    new Color(Muted.r, Muted.g, Muted.b, 0.95f), TextAnchor.MiddleLeft);
            }

            Txt(_tiny, Center(w, RefH - bottom - 24f, 900f, 24f),
                Sp("v0.21.2") + "   ·   精准模式 / 擦弹经济 / 过关奖励 / 蓄能换线", Dimc);
        }

        /// <summary>菱形环：外菱形 + 挖空内菱形（GUI.matrix 旋转在缩放坐标系下不可靠，改用嵌套）。</summary>
        void DiamondRing(float cx, float cy, float size, Color c)
        {
            UiKit.Sprite(Cx(cx, cy, size, size), UiKit.Diamond, new Color(c.r, c.g, c.b, 0.35f));
            float inner = size - 9f;
            UiKit.Sprite(Cx(cx, cy, inner, inner), UiKit.Diamond, new Color(0.016f, 0.024f, 0.059f, 1f));
            UiKit.Sprite(Cx(cx, cy, inner, inner), UiKit.Diamond, new Color(c.r, c.g, c.b, 0.10f));
        }

        static Rect Inset(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - d * 2f, r.height - d * 2f);

        static float Rise(float t, float delay)
        {
            float k = Mathf.Clamp01((t - delay) / 0.45f);
            return (1f - k) * 24f;
        }

        /// <summary>两端带横线的居中说明文字（网页版 .overline 的观感）。</summary>
        void TextWithRules(Rect r, string s, Color c, Color rule)
        {
            var tmp = Label(19, c, TextAnchor.MiddleCenter);
            float tw = tmp.CalcSize(new GUIContent(s)).x;
            tw = Mathf.Min(tw, r.width - 200f);
            var text = R(r.center.x - tw * 0.5f, r.y, tw, r.height);
            Txt(tmp, text, s, c);
            UiKit.Line(R(r.x, r.y + r.height * 0.5f - 1f, text.x - r.x - 22f, 2f), rule);
            UiKit.Line(R(text.xMax + 22f, r.y + r.height * 0.5f - 1f, r.xMax - text.xMax - 22f, 2f), rule);
        }

        /// <summary>字母加宽（网页版 .26em letter-spacing）——IMGUI 没有字距，用空格模拟。</summary>
        static string Sp(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 战斗 HUD

        void DrawCombat(GameRoot root, float w, float top, float bottom)
        {
            // 版式常量全部按网页版 CSS 实测值写（540×960 参照像素），再乘 2 进 IMGUI 坐标系。
            // .hud 的 padding = --pad = clamp(16px, 3.4vw, 26px)，540 宽时正好 18.4px。
            const float CssPad = 18.4f;
            float pad = CssPad * 2f;
            var p = root.Player;
            var dir = Director.Instance;

            // 顶/底渐变压暗：.hud::before 高 136px、::after 高 118px，两端都是渐隐不是硬边
            // 敌机会从顶栏背后掠过，压暗不够就会"把分数盖住"（用户反馈两次）：
            // 顶带不透明度提到 0.84，并给顶部留 12px 最小安全边（刘海机不贴边）。
            float topPad = top;   // 不再额外下移：下移会把分数块推进 Boss 面板
            var prev = GUI.color;
            GUI.color = new Color(0.016f, 0.024f, 0.059f, 0.84f);
            GUI.DrawTexture(R(0, 0, w, 136f * 2f + topPad), UiKit.VeilTop);
            GUI.color = new Color(0.016f, 0.024f, 0.059f, 0.62f);
            GUI.DrawTexture(R(0, RefH - bottom - 118f * 2f, w, 118f * 2f + bottom), UiKit.VeilBottom);
            GUI.color = prev;

            // ---- 左上：分数块（eyebrow 18.4 / 数字 31.4 / BEST 58.2 / GRAZE 71.2）----
            // 分数块下面垫一层很淡的底板：无论后面飞过什么，这四行始终可读
            // 底板包住 SCORE/数字/BEST/GRAZE 四行（到 162 ref），
            // Boss 面板从 168.8 ref 开始，两者留 6.8 ref 的缝
            // 硬边切角底板（低模语言）：不用圆角矩形，避免像"贴了张纸"。
            // 高度必须包住 GRAZE 行（底 160.4），又不能碰到 Boss 卡（顶 168.8）。
            UiKit.Slab(R(pad - 10f, topPad + 24f, 306f, 138f),
                new Color(0.008f, 0.012f, 0.032f, 0.34f), Line(0.12f), 18f);
            UiKit.Line(R(pad - 10f, topPad + 42f, 3f, 100f), new Color(Teal.r, Teal.g, Teal.b, 0.5f));
            Txt(Label(18, Muted, TextAnchor.UpperLeft), R(pad, topPad + 36.8f, 420f, 22f), Sp("SCORE"), Muted);
            Bold(_numBig, R(pad, topPad + 62.8f, 460f, 52f), Mathf.RoundToInt(root.Score).ToString("N0"), Paper, 2f);
            // BEST/GRAZE 跟着数字的实测字高走，但压到网页版 58.2 CSS 的上限，
            // 否则 50 号字的行高会把 GRAZE 推进 Boss 面板（v0.21.1 实测底边 178.8 > 168.8）。
            float numH = _numBig.CalcSize(new GUIContent("0")).y;
            float bestY = Mathf.Min(topPad + 66.8f + numH, topPad + 116.4f);
            float grazeY = Mathf.Max(bestY + 24f, topPad + 140.4f);
            Txt(_tiny, R(pad, bestY, 300f, 20f), "BEST ", Dimc);
            Txt(_small, R(pad + 58f, bestY - 3f, 300f, 22f), root.Best.ToString("N0"), new Color(Gold.r, Gold.g, Gold.b, 0.85f));
            Txt(_tiny, R(pad, grazeY, 300f, 18f), "GRAZE ", Dimc);
            Txt(_small, R(pad + 72f, grazeY - 2f, 300f, 20f), root.Stats.Graze.ToString(), Teal);

            // ---- 中上：星区（eyebrow 19.4 / 数字 31.4 / 区名 50.4）----
            if (dir != null)
            {
                Txt(Label(18, TealDim, TextAnchor.MiddleCenter), R(w * 0.5f - 220f, topPad + 38.8f, 440f, 22f), Sp("SECTOR"), TealDim);
                Bold(_num, R(w * 0.5f - 220f, topPad + 62.8f, 440f, 40f), dir.Zone.ToString("00"), Teal, 1.6f);
                Txt(Label(19, Dimc, TextAnchor.MiddleCenter), R(w * 0.5f - 220f, topPad + 100.8f, 440f, 24f),
                    dir.ZoneName, Muted);
            }

            // ---- 右上：暂停（.hud-top 第三列，顶到 18.4）----
            var pause = Controls.PauseRect;
            UiKit.Pill(pause, new Color(0.039f, 0.067f, 0.14f, 0.55f), Line(0.16f));
            UiKit.Line(R(pause.center.x - 7f, pause.center.y - 14f, 5f, 28f), new Color(Muted.r, Muted.g, Muted.b, 0.9f));
            UiKit.Line(R(pause.center.x + 2f, pause.center.y - 14f, 5f, 28f), new Color(Muted.r, Muted.g, Muted.b, 0.9f));

            // ---- 连锁（.combo-box top = pad+74，有 Boss 时 pad+158）----
            if (root.Combo > 0)
            {
                float bx = w - pad - 170f;
                float by = topPad + (dir != null && dir.Boss != null ? 176.4f * 2f : 92.4f * 2f);
                Txt(Label(54, Gold, TextAnchor.MiddleRight), R(bx, by, 170f, 64f), "×" + root.Multiplier, Gold);
                Txt(Label(16, Dimc, TextAnchor.MiddleRight), R(bx, by + 62f, 170f, 22f), "CHAIN " + root.Combo, Muted);
                var track = R(bx + 38f, by + 78f, 132f, 4f);
                UiKit.Line(track, new Color(Gold.r, Gold.g, Gold.b, 0.16f));
                UiKit.Gradient(R(track.x, track.y, track.width * root.ComboRatio, track.height), UiKit.GradGold, new Color(1, 1, 1, 0.9f));
            }

            if (dir != null && dir.Boss != null) DrawBossPanel(root, w, top, dir.Boss);
            if (p != null) DrawBottom(root, w, bottom);

            // 只在真正游玩时画公告/提示：暂停与结算页后面再飘一条教学胶囊会很乱
            if (root.State == GameState.Playing)
            {
                DrawAnnounce(root, w, top);
                DrawHintToast(root, w, bottom);
            }
        }

        void DrawBossPanel(GameRoot root, float w, float top, BossController b)
        {
            // .boss-panel：top = pad+66，左右 = pad，padding 9/11/8，行间 gap 6（CSS px ×2 进 GUI）
            float pad = 36.8f;
            var card = R(pad, top + 168.8f, w - pad * 2f, 136f);
            UiKit.Panel(card, new Color(0.047f, 0.031f, 0.086f, 0.72f), new Color(Rose.r, Rose.g, Rose.b, 0.18f), 16f);
            float inner = 26f;                       // 1px 边框 + 11px 内缩
            float ix = card.x + inner, iw = card.width - inner * 2f;

            // THREAT 标签（font 8、padding 2×5、边框）
            var tagStyle = Label(16, Rose, TextAnchor.MiddleCenter);
            float tagW = tagStyle.CalcSize(new GUIContent(Sp("THREAT"))).x + 20f;
            var tag = R(ix, card.y + 20f, tagW, 27f);
            UiKit.Panel(tag, new Color(Rose.r, Rose.g, Rose.b, 0.08f), new Color(Rose.r, Rose.g, Rose.b, 0.4f), 4f);
            Txt(tagStyle, tag, Sp("THREAT"), Rose);

            // 机名 + 副标题：原版是 flex + 6px gap，副标题紧跟实测宽度，不是硬写 x
            string name = b.BossLabel;
            var nameStyle = Label(26, Paper, TextAnchor.MiddleLeft);
            float nameW = nameStyle.CalcSize(new GUIContent(name)).x;
            Txt(nameStyle, R(tag.xMax + 12f, card.y + 16f, nameW + 8f, 34f), name, Paper);
            // 阶段徽章（临界换成金色脉冲）
            var phase = R(card.xMax - inner - 76f, card.y + 18f, 76f, 28f);
            // 副标题右对齐到徽章左边：机名长度不定，左对齐拼字符串必然出现压字
            Txt(Label(18, Dimc, TextAnchor.MiddleRight),
                R(tag.xMax + 12f, card.y + 20f, Mathf.Max(60f, phase.x - tag.xMax - 32f), 28f),
                b.BossTitle, new Color(Dimc.r, Dimc.g, Dimc.b, 0.95f));
            var phaseCol = b.Critical ? Gold : Rose;
            UiKit.Pill(phase, new Color(phaseCol.r, phaseCol.g, phaseCol.b, b.Critical ? 0.14f : 0.02f),
                new Color(phaseCol.r, phaseCol.g, phaseCol.b, b.Critical ? 0.5f : 0f));
            Txt(Label(16, phaseCol, TextAnchor.MiddleCenter), phase,
                b.Critical ? Sp("CRIT") : "P" + (b.Phase + 1), phaseCol);

            // 血条（6px 内高 + 1px 边框 = 8px）+ 25% 分段刻线
            var track = R(ix, card.y + 58f, iw, 16f);
            UiKit.Pill(track, new Color(1f, 1f, 1f, 0.07f), new Color(Rose.r, Rose.g, Rose.b, 0.14f));
            float frac = Mathf.Clamp01(b.Hp / Mathf.Max(1f, b.MaxHp));
            if (frac > 0.001f)
                UiKit.Gradient(R(track.x + 2f, track.y + 2f, (track.width - 4f) * frac, track.height - 4f), UiKit.GradRose, Color.white);
            for (int i = 1; i < 4; i++)
                UiKit.Line(R(track.x + track.width * (i * 0.25f), track.y + 1f, 2f, track.height - 2f), new Color(0.016f, 0.024f, 0.059f, 0.75f));

            // 破防槽（3px，紧贴血条下方）
            var brk = R(ix, card.y + 80f, iw, 6f);
            UiKit.Pill(brk, new Color(Gold.r, Gold.g, Gold.b, 0.10f), Color.clear);
            float bf = Mathf.Clamp01(b.BreakGauge / Mathf.Max(1f, b.BreakMax));
            if (bf > 0.001f)
                UiKit.Line(R(brk.x, brk.y, brk.width * bf, brk.height), new Color(Gold.r, Gold.g, Gold.b, 0.75f));

            // 招式行：原版整招期间都挂着招式名，预告期多一个「预告 · 」前缀并闪烁；
            // 没有招式时留空——原版这里不放血量数字，血量数字会抢读条的注意力。
            string cast = b.CastLabel;
            if (!string.IsNullOrEmpty(cast))
            {
                bool telling = b.TelegraphLeft > 0f;
                float pulse = telling ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 14f) : 0.9f;
                Txt(Label(16, Gold, TextAnchor.MiddleLeft), R(ix, card.y + 98f, iw, 20f),
                    Sp(telling ? "CAST" : "MOVE") + "   " + cast, new Color(Gold.r, Gold.g, Gold.b, pulse));
            }
        }

        void DrawBottom(GameRoot root, float w, float bottom)
        {
            float pad = 36.8f;                       // --pad @540 宽 = 18.4 CSS px
            float bandTop = Controls.BandTop;
            var p = root.Player;
            var line = GameConfig.Line(p.Weapons.ActiveId);
            bool detuned = p.Weapons.Active.Detuned;

            // ---- 左：生存状态（.dash-status 62×32，上排舰体 y=915、下排护盾 y=930）----
            float x = pad;
            for (int i = 0; i < 5; i++)
            {
                var r = R(x + i * 26f, bandTop + 34f, 20f, 16f);
                bool on = i < p.Hull;
                UiKit.Sprite(r, UiKit.TriDown, on ? Teal : new Color(Muted.r, Muted.g, Muted.b, 0.16f));
            }
            for (int i = 0; i < GameConfig.MaxShields; i++)
            {
                var r = R(x + i * 20f, bandTop + 64f, 14f, 14f);
                bool on = i < p.Shields;
                UiKit.Sprite(r, UiKit.Hex, on ? Violet : new Color(Violet.r, Violet.g, Violet.b, 0.26f));
            }

            // ---- 左中：武器胶囊（chip 从 89.4px 起；26px 充能环 + 42px 名 + 5 格等级）----
            float chipX = x + 124f + 18f;            // dash-status 宽 62 + gap 9（×2）
            float ringCY = bandTop + 56f;            // 带内垂直居中（926 - 898 = 28 CSS）
            var ring = Cx(chipX + 6f + 26f, ringCY, 52f, 52f);
            UiKit.Arc(ring, Mathf.Clamp01(p.Weapons.Active.Charge / 100f),
                detuned ? Rose : line.Tint, 5f);
            UiKit.Arc(Ring(ring, -6f), 1f, new Color(1f, 1f, 1f, 0.06f), 1f);
            if (p.Weapons.Resonance > 0.05f)
                Txt(Label(15, Gold, TextAnchor.MiddleCenter), ring, "×" + Mathf.RoundToInt(p.Weapons.Resonance), Gold);

            // 失谐时原版直接把名字换成「失谐排气」并染成 rose，不再另起一行
            string wname = detuned ? "失谐排气" : GameConfig.WeaponName(p.Weapons.ActiveId, p.Weapons.Level);
            var nameCol = detuned ? Rose : InkText;
            float copyX = ring.xMax + 14f;
            Txt(Label(18, nameCol, TextAnchor.UpperLeft), R(copyX, bandTop + 26f, 84f, 22f), wname, nameCol);
            for (int i = 0; i < 5; i++)
            {
                // 名字与等级条之间留足字高：以前 19 号字挤在 20 高的框里，笔画直接压到格子上
                var pip = R(copyX + i * 17.5f, bandTop + 58f, 14f, 6f);
                bool on = i < p.Weapons.Level;
                UiKit.Line(pip, on ? line.Tint : new Color(0.63f, 0.71f, 1f, 0.18f));
            }

            // ---- 中：星区进度轨 + 终点菱形（.dash-progress 最长 132px，右侧留 38px 给动作环）----
            {
                var dir = Director.Instance;
                float railX = copyX + 84f + 16f;
                float railW = Mathf.Min(264f, w - railX - 380f);
                if (railW > 60f)
                {
                    var rail = R(railX, bandTop + 52f, railW, 6f);
                    UiKit.Pill(rail, new Color(0.63f, 0.71f, 1f, 0.12f), Color.clear);
                    float prog = dir != null ? dir.RailProgress : 0f;
                    var col = dir != null && dir.State == Director.RunState.Boss ? Rose
                            : dir != null && dir.State == Director.RunState.Clearing ? Gold : Teal;
                    if (prog > 0.002f)
                        UiKit.Line(R(rail.x, rail.y, rail.width * prog, rail.height), new Color(col.r, col.g, col.b, 0.9f));
                    var mark = Cx(rail.xMax + 8f, rail.y + rail.height * 0.5f, 12f, 12f);
                    UiKit.Sprite(mark, UiKit.Diamond,
                        dir != null && dir.State == Director.RunState.Boss ? Rose : new Color(0.63f, 0.71f, 1f, 0.3f));
                }
            }

            // ---- 右：三枚动作环 ----
            DrawActionRing(Controls.PrecisionRect, "精准", p.FocusHeld ? "慢速" : "慢速",
                p.FocusHeld ? 1f : 0f, Teal, p.FocusHeld, false);
            DrawActionRing(Controls.DashRect, "冲刺",
                p.DashCd > 0f ? p.DashCd.ToString("0.0") + "s" : "闪避",
                1f - Mathf.Clamp01(p.DashCd / GameConfig.DashCooldown), Teal, p.DashCd <= 0f, false);
            DrawActionRing(Controls.NovaRect, "星爆",
                p.Nova >= 100f ? "READY" : Mathf.RoundToInt(p.Nova) + "%",
                Mathf.Clamp01(p.Nova / 100f), p.Nova >= 100f ? Gold : Teal, p.Nova >= 100f, p.Nova >= 100f);
        }

        void DrawActionRing(Rect r, string label, string sub, float pct, Color color, bool lit, bool hot)
        {
            float n = Mathf.Clamp01(pct);
            if (n > 0.001f) UiKit.Arc(Ring(r, -6f), n, new Color(color.r, color.g, color.b, lit ? 0.95f : 0.7f), 5f);
            else UiKit.Arc(Ring(r, -6f), 1f, new Color(1f, 1f, 1f, 0.07f), 5f);

            UiKit.Pill(r, hot ? new Color(color.r, color.g, color.b, 0.12f) : new Color(0.039f, 0.067f, 0.14f, 0.85f),
                lit ? new Color(color.r, color.g, color.b, 0.65f) : new Color(Violet.r, Violet.g, Violet.b, 0.26f));
            if (hot) UiKit.Glow(Cx(r.center.x, r.center.y, r.width * 1.9f, r.height * 1.9f),
                new Color(color.r, color.g, color.b, 0.16f));

            Txt(Label(21, lit ? color : InkText, TextAnchor.MiddleCenter), R(r.x, r.y + 10f, r.width, 28f), label,
                lit ? color : InkText);
            Txt(Label(14, lit ? new Color(color.r, color.g, color.b, 0.9f) : Dimc, TextAnchor.MiddleCenter),
                R(r.x, r.y + 40f, r.width, 20f), sub, lit ? new Color(color.r, color.g, color.b, 0.9f) : Dimc);
        }

        static Rect Ring(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - d * 2f, r.height - d * 2f);

        // ---------------------------------------------------------------- 公告 / 提示 / 吐司

        void DrawAnnounce(GameRoot root, float w, float top)
        {
            if (string.IsNullOrEmpty(root.AnnounceTitle)) return;
            float age = Time.unscaledTime - root.AnnounceAt;
            float dur = Mathf.Max(0.2f, root.AnnounceDur);
            if (age > dur) return;

            float fadeIn = Mathf.Clamp01(age / 0.22f);
            float fadeOut = Mathf.Clamp01((dur - age) / 0.3f);
            float k = Mathf.Min(fadeIn, fadeOut);
            float slide = (1f - fadeIn) * 14f;

            var col = root.AnnounceAlert ? Rose : Paper;
            var subCol = root.AnnounceAlert ? Gold : Teal;

            var tmp = Label(46, col, TextAnchor.MiddleCenter);
            float tw = Mathf.Min(tmp.CalcSize(new GUIContent(root.AnnounceTitle)).x + 90f, w - 120f);
            // 原版公告中心在 38% 高度（364.8px / 960），底部变体在 837.6px
            float y = root.AnnounceBottom ? 1675.2f : 729.6f;
            var box = Cx(w * 0.5f, y + slide, tw, string.IsNullOrEmpty(root.AnnounceSub) ? 74f : 112f);

            UiKit.Pill(box, new Color(0.016f, 0.024f, 0.059f, 0.55f * k), new Color(0.63f, 0.71f, 1f, 0.12f * k));
            Bold(tmp, R(box.x, box.y + 8f, box.width, 56f), root.AnnounceTitle, new Color(col.r, col.g, col.b, k), 2.2f);
            if (!string.IsNullOrEmpty(root.AnnounceSub))
                Txt(Label(17, subCol, TextAnchor.MiddleCenter), R(box.x, box.y + 64f, box.width, 26f),
                    Sp(root.AnnounceSub), new Color(subCol.r, subCol.g, subCol.b, k * 0.95f));

            // 两侧细线：原版每条 54px 长、离胶囊 12px
            float lx = box.x - 24f;
            if (lx > 20f)
            {
                UiKit.Line(R(lx - 108f, box.center.y - 1f, 108f, 2f), new Color(Violet.r, Violet.g, Violet.b, 0.28f * k));
                UiKit.Line(R(box.xMax + 24f, box.center.y - 1f, 108f, 2f), new Color(Violet.r, Violet.g, Violet.b, 0.28f * k));
            }
        }

        void DrawHintToast(GameRoot root, float w, float bottom)
        {
            float bandTop = Controls.BandTop;
            float ageH = Time.unscaledTime - root.HintAt;
            if (!string.IsNullOrEmpty(root.Hint) && ageH < 4.6f)
            {
                float k = Mathf.Min(Mathf.Clamp01(ageH / 0.3f), Mathf.Clamp01((4.6f - ageH) / 0.4f));
                var st = Label(21, Muted, TextAnchor.MiddleCenter);
                float tw = Mathf.Min(st.CalcSize(new GUIContent(root.Hint)).x + 60f, w - 100f);
                // 教学胶囊贴在仪表带上方 155px（原版 742.6 vs 带顶 898）
                var r = Cx(w * 0.5f, bandTop - 310.8f, tw, 56f);
                UiKit.Pill(r, new Color(0.027f, 0.043f, 0.10f, 0.6f * k), new Color(0.63f, 0.71f, 1f, 0.13f * k));
                Txt(st, r, root.Hint, new Color(Muted.r, Muted.g, Muted.b, k));
            }

            float ageT = Time.unscaledTime - root.ToastAt;
            if (!string.IsNullOrEmpty(root.Toast) && ageT < 1.9f)
            {
                float k = Mathf.Min(Mathf.Clamp01(ageT / 0.2f), Mathf.Clamp01((1.9f - ageT) / 0.3f));
                var st = Label(22, InkText, TextAnchor.MiddleCenter);
                float tw = Mathf.Min(st.CalcSize(new GUIContent(root.Toast)).x + 60f, w - 100f);
                // 飘字再高一档，贴在 695px（原版）
                var r = Cx(w * 0.5f, bandTop - 406f, tw, 58f);
                UiKit.Pill(r, new Color(0.027f, 0.043f, 0.10f, 0.88f * k), new Color(0.63f, 0.71f, 1f, 0.14f * k));
                Txt(st, r, root.Toast, new Color(InkText.r, InkText.g, InkText.b, k));
            }
        }

        // ---------------------------------------------------------------- 暂停

        void DrawPause(GameRoot root, float w, float top, float bottom)
        {
            Dim();
            float cardW = Mathf.Min(680f, w - 80f);
            float cardH = 1000f;
            var card = Cx(w * 0.5f, RefH * 0.5f, cardW, cardH);
            UiKit.Panel(card, new Color(0.063f, 0.094f, 0.188f, 0.94f), Line(0.14f), 16f);
            UiKit.Line(R(card.x + 28f, card.y + 1f, card.width - 56f, 2f), new Color(1f, 1f, 1f, 0.05f));

            float x = card.x + 44f, y = card.y + 40f;
            Txt(_eyebrow, R(x, y, 400f, 24f), Sp("SYSTEM HOLD"), Muted);
            y += 34f;
            Bold(_h1c, R(x, y, card.width - 88f, 70f), "已暂停", Paper, 2f);
            y += 74f;
            Txt(_h2, R(x, y, card.width - 88f, 30f), "引擎待命。深空仍在呼吸。", Muted);
            y += 46f;

            // 装配摘要
            var p = root.Player;
            if (p != null)
            {
                var w0 = p.Weapons;
                string[] chips =
                {
                    $"脉冲 LV.{w0.Pulse.Level}",
                    w0.Prism.Unlocked ? $"棱镜 LV.{w0.Prism.Level}" : "棱镜 未解锁",
                    w0.Swarm.Unlocked ? $"蜂群 LV.{w0.Swarm.Level}" : "蜂群 未解锁",
                    $"护盾 {p.Shields}/{GameConfig.MaxShields}",
                    $"僚机 {p.DroneCount}/{GameConfig.MaxDrones}",
                    p.ReflectT > 0f ? "力场 生效" : "力场 无",
                };
                y = Chips(R(x, y, card.width - 88f, 30f), chips, y);
            }
            y += 10f;

            // 设置
            y = SettingRow(root, x, y, card.width - 88f, "音乐与音效", "程序化合成器音轨", !root.Muted, () => root.ToggleMute());
            y = SettingRow(root, x, y, card.width - 88f, "轻柔特效", "降低闪光与粒子密度", root.LowFx, () => root.ToggleLowFx());
            y = SettingRow(root, x, y, card.width - 88f, "高对比敌弹", "敌弹加白圈，深空更清楚", root.HighContrast, () => root.ToggleHighContrast());
            y += 24f;

            var resume = R(x, y, card.width - 88f, 100f);
            UIButtonFrame(resume, true);
            Txt(_btn, resume, "继续巡航", new Color(0.02f, 0.06f, 0.11f));
            Txt(_kbd, R(resume.xMax - 66f, resume.y + 40f, 52f, 20f), "ESC", new Color(0.02f, 0.06f, 0.11f, 0.45f));
            if (GUI.Button(resume, GUIContent.none, _invisible)) root.TogglePause();
            y += 118f;

            var again = R(x, y, card.width - 88f, 92f);
            UiKit.Panel(again, new Color(0.047f, 0.075f, 0.149f, 0.65f), Line(0.26f), 6f);
            Txt(_btn, again, "重新开始", InkText);
            if (GUI.Button(again, GUIContent.none, _invisible)) root.Restart();
            y += 110f;

            var quit = R(x, y, card.width - 88f, 96f);
            UiKit.Panel(quit, new Color(0.047f, 0.075f, 0.149f, 0.45f), Line(0.2f), 6f);
            Txt(Label(19, Muted, TextAnchor.MiddleCenter), quit, "返回基地", Muted);
            if (GUI.Button(quit, GUIContent.none, _invisible)) root.BackToMenu();
            y += 112f;

            Txt(Label(14, Dimc, TextAnchor.MiddleCenter), R(x, y, card.width - 88f, 22f),
                Sp("SPACE 冲刺   ·   SHIFT 精准   ·   X 星爆   ·   M 静音"), new Color(Dimc.r, Dimc.g, Dimc.b, 0.9f));
        }

        float Chips(Rect area, string[] texts, float y)
        {
            float x = area.x;
            float h = 34f;
            for (int i = 0; i < texts.Length; i++)
            {
                var st = Label(16, Muted, TextAnchor.MiddleCenter);
                float tw = st.CalcSize(new GUIContent(texts[i])).x + 28f;
                if (x + tw > area.xMax) { x = area.x; y += h + 8f; }
                var r = R(x, y, tw, h);
                UiKit.Pill(r, new Color(0.41f, 0.96f, 0.86f, 0.05f), Line(0.12f));
                Txt(st, r, texts[i], Muted);
                x += tw + 8f;
            }
            return y + h + 8f;
        }

        float SettingRow(GameRoot root, float x, float y, float width, string title, string sub, bool on, System.Action toggle)
        {
            var row = R(x, y, width, 92f);
            Txt(Label(25, InkText, TextAnchor.MiddleLeft), R(row.x, row.y + 12f, row.width - 140f, 30f), title, InkText);
            Txt(Label(18, Muted, TextAnchor.MiddleLeft), R(row.x, row.y + 44f, row.width - 140f, 24f), sub, new Color(Muted.r, Muted.g, Muted.b, 0.9f));

            var t = R(row.xMax - 100f, row.y + 26f, 84f, 40f);
            UiKit.Pill(t, on ? new Color(Teal.r, Teal.g, Teal.b, 0.16f) : new Color(0.63f, 0.71f, 1f, 0.08f),
                on ? new Color(Teal.r, Teal.g, Teal.b, 0.55f) : Line(0.26f));
            var knob = Cx(on ? t.xMax - 22f : t.x + 22f, t.center.y, 30f, 30f);
            UiKit.Glow(knob, on ? new Color(Teal.r, Teal.g, Teal.b, 0.35f) : new Color(0f, 0f, 0f, 0f));
            UiKit.Sprite(knob, UiKit.Disc, on ? Teal : Muted);
            if (GUI.Button(row, GUIContent.none, _invisible)) toggle();
            UiKit.Line(R(row.x, row.yMax, row.width, 1f), Line(0.07f));
            return y + 92f;
        }

        // ---------------------------------------------------------------- 结算

        void DrawDead(GameRoot root, float w, float top, float bottom)
        {
            Dim();
            float cx = w * 0.5f;
            var s = root.Stats;

            // 原版结算是一张 340px 宽的面板卡，整张垂直居中（不是贴顶排一条长列）
            const float cardW = 680f;
            float cardH = 980f;
            var card = Cx(cx, RefH * 0.5f, cardW, cardH);
            UiKit.Panel(card, new Color(0.035f, 0.055f, 0.117f, 0.96f), Line(0.18f), 20f);
            UiKit.Line(R(card.x + 56f, card.y + 1f, card.width - 112f, 2f), new Color(1f, 1f, 1f, 0.06f));

            float y = card.y + 40f;
            Txt(Label(17, Rose, TextAnchor.MiddleCenter), R(card.x, y, card.width, 24f), Sp("RUN TERMINATED"), Rose);
            y += 34f;
            Bold(_h1c, R(card.x, y, card.width, 72f), "巡航终止", Paper, 2f);
            y += 76f;
            Txt(Label(19, Muted, TextAnchor.MiddleCenter), R(card.x, y, card.width, 28f), "信号消失在裂隙深处。",
                new Color(Muted.r, Muted.g, Muted.b, 0.9f));
            y += 44f;

            // 分数 + 评级圆章
            var scoreBox = R(card.x + 44f, y, card.width - 232f, 110f);
            Txt(Label(15, Dimc, TextAnchor.UpperLeft), R(scoreBox.x, scoreBox.y, scoreBox.width, 22f),
                Sp("SCORE"), Dimc);
            GlowText(_numBig, R(scoreBox.x, scoreBox.y + 24f, scoreBox.width, 76f),
                Mathf.RoundToInt(root.Score).ToString("N0"), Paper, Teal, TextAnchor.MiddleLeft);
            var rank = Cx(card.xMax - 96f, y + 60f, 104f, 104f);
            var rc = root.Grade switch { 4 => Teal, 3 => Violet, 2 => Gold, _ => Muted };
            UiKit.Pill(rank, new Color(rc.r, rc.g, rc.b, 0.08f), new Color(rc.r, rc.g, rc.b, 0.5f));
            Bold(Label(48, rc, TextAnchor.MiddleCenter), R(rank.x, rank.y + 20f, rank.width, 54f), root.GradeText, rc, 1.6f);
            Txt(Label(11, rc, TextAnchor.MiddleCenter), R(rank.x, rank.y + 72f, rank.width, 18f), Sp("RANK"),
                new Color(rc.r, rc.g, rc.b, 0.7f));
            y += 122f;

            if (root.Score >= root.Best && root.Score > 0)
            {
                Txt(Label(17, Gold, TextAnchor.MiddleCenter), R(card.x, y, card.width, 24f), "◆ 新纪录 ◆", Gold);
                y += 30f;
            }
            Txt(Label(16, Dimc, TextAnchor.MiddleCenter), R(card.x, y, card.width, 24f),
                root.NextGradeGap > 0f ? "距上一档还差 " + Mathf.RoundToInt(root.NextGradeGap) + " 分" : "评级：按得分与最远星区综合判定",
                Dimc);
            y += 40f;

            // 十项数据（卡内两列五行）
            string[,] grid =
            {
                { "抵达星区", Director.Instance != null ? Director.Instance.Zone.ToString("00") : "01" },
                { "击落敌机", s.Kills.ToString() },
                { "最高连锁", s.MaxCombo.ToString() },
                { "巡航时长", Fmt(s.Time) },
                { "擦弹", s.Graze.ToString() },
                { "星爆释放", s.Novas.ToString() },
                { "相位穿越", s.Phases.ToString() },
                { "核心破防", s.Breaks.ToString() },
                { "受创", s.Hits.ToString() },
                { "最高倍率", "×" + Mathf.Max(1, root.Multiplier) },
            };
            float gw = (card.width - 88f - 12f) * 0.5f;
            for (int i = 0; i < 10; i++)
            {
                int col = i % 2, row = i / 2;
                var r = R(card.x + 44f + col * (gw + 12f), y + row * 66f, gw, 58f);
                UiKit.Panel(r, new Color(0.02f, 0.031f, 0.075f, 0.6f), Line(0.09f), 8f);
                Txt(Label(15, Dimc, TextAnchor.MiddleLeft), R(r.x + 14f, r.y + 6f, r.width - 28f, 20f), grid[i, 0], Dimc);
                Bold(Label(26, InkText, TextAnchor.MiddleLeft), R(r.x + 14f, r.y + 26f, r.width - 28f, 28f), grid[i, 1],
                    InkText, 1.3f);
            }
            y += 5 * 66f + 14f;

            if (root.LastClearLines != null && root.LastClearLines.Length > 0)
            {
                Txt(Label(15, Dimc, TextAnchor.MiddleCenter), R(card.x + 24f, y, card.width - 48f, 22f),
                    string.Join("  ·  ", root.LastClearLines), Dimc);
                y += 30f;
            }

            var retry = Cx(cx, y + 48f, card.width - 88f, 92f);
            UIButtonFrame(retry, true);
            Txt(_btn, retry, "再次出击", new Color(0.02f, 0.06f, 0.11f));
            Txt(_kbd, R(retry.xMax - 52f, retry.y + 36f, 40f, 20f), "R", new Color(0.02f, 0.06f, 0.11f, 0.45f));
            if (GUI.Button(retry, GUIContent.none, _invisible)) root.Restart();

            var menu = Cx(cx, y + 140f, card.width - 88f, 48f);
            UiKit.Panel(menu, new Color(0.047f, 0.075f, 0.149f, 0.5f), Line(0.18f), 6f);
            Txt(Label(17, Muted, TextAnchor.MiddleCenter), menu, "返回基地", Muted);
            if (GUI.Button(menu, GUIContent.none, _invisible)) root.BackToMenu();
        }

        static string Fmt(float sec)
        {
            int t = Mathf.Max(0, Mathf.RoundToInt(sec));
            return (t / 60).ToString("00") + ":" + (t % 60).ToString("00");
        }

        // ---------------------------------------------------------------- 通用件

        void Dim()
        {
            var prev = GUI.color;
            GUI.color = new Color(0.012f, 0.02f, 0.047f, 0.62f);
            GUI.DrawTexture(R(0, 0, w(), RefH), UiKit.White);
            GUI.color = prev;
        }

        float w() => Screen.height > 0 ? Screen.width / (Screen.height / RefH) : 1080f;

        // 主按钮：渐变填充 + 圆角描边（网页版 .btn--primary）
        void UIButtonFrame(Rect r, bool primary)
        {
            if (primary)
                UiKit.PanelGrad(r, new Color(0.57f, 0.97f, 0.91f), new Color(0.66f, 0.61f, 1f),
                    new Color(1f, 1f, 1f, 0.22f), 6f);
            else
                UiKit.Panel(r, new Color(0.047f, 0.075f, 0.149f, 0.6f), Line(0.26f), 6f);
        }

        // 透明按钮样式（只吃输入，不画）
        GUIStyle _invisible;
    }
}
