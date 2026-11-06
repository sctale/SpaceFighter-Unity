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

        // ---------------------------------------------------------------- 左上分数块排版
        // 全部是 1920 参考坐标。门禁 beh.hudScore.* 直接读这几个常量，改任何一项都会先被断言拦下。
        public const float ScoreTop = 40f;      // 50 号分数的排版框顶（整体上移 22.8，贴近安全区）
        public const float ScoreBoxH = 52f;     // 排版框高＝视觉行距（比字体行盒矮，靠 FitLine 补齐）
        public const float BestTop = 98f;       // BEST 行框顶
        public const float GrazeTop = 124f;     // GRAZE 行框顶
        public const float DigitInk = 55.2f;    // 实测：数字墨迹底 = ScoreTop + 55.2
        public const float BestInk = 7.6f;      // 实测：BEST 行墨迹顶 = BestTop + 7.6

        // ---------------------------------------------------------------- 设计令牌
        static Color Ink0 => GameConfig.Hex("04060F");
        static Color Paper => GameConfig.Hex("F5F9FF");
        static Color InkText => GameConfig.Hex("E6EEFF");
        static Color Muted => GameConfig.Hex("97A9CC");
        static Color Dimc => GameConfig.Hex("6F81A6");
        static Color Teal => GameConfig.Hex("69F5DC");
        static Color TealDim => GameConfig.Hex("36C7B4");
        static Color Violet => GameConfig.Hex("9B8CFF");
        static Color Rose => GameConfig.Hex("FF6F91");
        static Color Gold => GameConfig.Hex("FFD27A");
        static Color Line(float a) => new Color(0.63f, 0.71f, 1f, a);

        Font _font, _boldFont;
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
            // 标题/数字用随包带的 Noto Sans SC Bold 子集；缺失时回退到 Regular + 叠画
            _boldFont = Resources.Load<Font>("Fonts/AuroraCJK-Bold");
            if (_boldFont == null) _boldFont = _font;
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
                // 无法真的做降饱和，用「压暗 + 泛色 + 一条故障扫描线」模拟受创的失真感
                var wash = GUI.color;
                GUI.color = new Color(_hitTint.r * 0.35f, _hitTint.g * 0.35f, _hitTint.b * 0.35f, 0.10f * k);
                GUI.DrawTexture(new Rect(0, 0, w, RefH), UiKit.White);
                GUI.color = wash;
                UiKit.Vignette(new Rect(0, 0, w, RefH), 0.68f * k, _hitTint);
                float gy = RefH * (0.30f + Mathf.Repeat(Time.unscaledTime * 1.9f, 0.42f));
                UiKit.Line(R(0, gy, w, 2f), new Color(_hitTint.r, _hitTint.g, _hitTint.b, 0.16f * k));
            }
            // 低血量：边缘一圈玫红脉冲
            var p = root.Player;
            if (root.State == GameState.Playing && p != null && p.Hull <= 1)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f);
                UiKit.Vignette(new Rect(0, 0, w, RefH), 0.18f + pulse * 0.16f,
                    new Color(Rose.r, Rose.g, Rose.b, 1f));
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
            _num = Label(40, Paper); _num.font = _boldFont;
            _numBig = Label(50, Paper); _numBig.font = _boldFont;   // 原版 24.8 CSS；62 会溢出 52 的框、压住 BEST 行
            _title = Label(110, Paper); _title.font = _boldFont;    // 真粗体比 118 号假粗更紧凑

            _h1 = Label(58, Paper, TextAnchor.MiddleLeft); _h1.font = _boldFont;
            _h1c = Label(58, Paper, TextAnchor.MiddleCenter); _h1c.font = _boldFont;
            _h2 = Label(23, Muted, TextAnchor.MiddleLeft);
            _tag = Label(16, Rose, TextAnchor.MiddleCenter);
            _kbd = Label(14, Dimc, TextAnchor.MiddleCenter);
            _btn = Label(26, InkText, TextAnchor.MiddleCenter); _btn.font = _boldFont;
            _btnBig = Label(29, new Color(0.02f, 0.06f, 0.11f), TextAnchor.MiddleCenter); _btnBig.font = _boldFont;

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
            GUI.Label(FitLine(r, style.fontSize, style.alignment), s, style);
            GUI.color = prev;
        }

        /// <summary>
        /// AuroraCJK 的行盒约 1.5em，而排版框是按"视觉行距"给的（比行盒矮）。
        /// 样式是 TextClipping.Clip，IMGUI 就把超出排版框的部分直接切掉——
        /// 数字下半截被切出一条横线，看起来就像"分数被最高分挡住了"（用户反馈三次，真因在这里）。
        /// 这里只把裁剪框补到行盒高度，字形位置一律不动：
        /// 上对齐时行盒本来就贴顶；居中对齐时把框上下各扩一半，中心不变。
        /// </summary>
        public const float LineBox = 1.5f;

        public static Rect FitLine(Rect r, int fontSize, TextAnchor anchor)
        {
            float need = Mathf.Max(1, fontSize) * LineBox;
            if (r.height >= need) return r;
            float add = need - r.height;
            bool upper = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.UpperCenter || anchor == TextAnchor.UpperRight;
            return new Rect(r.x, upper ? r.y : r.y - add * 0.5f, r.width, need);
        }

        /// <summary>
        /// 粗体：优先用随包带的 Noto Sans SC Bold；字体缺失时才回退到「同字叠画偏移」。
        /// </summary>
        void Bold(GUIStyle st, Rect r, string s, Color c, float px = 2.4f)
        {
            if (_boldFont != null)
            {
                var bs = st.font == _boldFont ? st : new GUIStyle(st) { font = _boldFont };
                Txt(bs, r, s, c);
                return;
            }
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
            float t = Time.unscaledTime - _stateAt;
            float cx = w * 0.5f;

            // 背景：主光 + 对侧冷光 + 底部暖光，给菜单一点纵深
            var scrim = new Rect(0, 0, w, RefH);
            GUI.color = new Color(0.016f, 0.024f, 0.059f, 0.93f);
            GUI.DrawTexture(scrim, UiKit.White);
            GUI.color = Color.white;
            UiKit.Glow(R(-w * 0.18f, -260f, w * 0.95f, 880f), new Color(0.41f, 0.96f, 0.86f, 0.11f));
            UiKit.Glow(R(w * 0.55f, 640f, w * 0.8f, 820f), new Color(Violet.r, Violet.g, Violet.b, 0.07f));
            UiKit.Glow(Center(w, top + 420f, 560f, 560f), new Color(Gold.r, Gold.g, Gold.b, 0.035f));

            // 品牌标记从 86.4px 起（原版 .brand margin-top），菱形 46px
            float y = top + 172.8f;

            // --- 品牌标记：双层菱形环 + 金色核心（嵌套菱形做环）---
            {
                float mc = cx, my = y + 46f;
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 1.7f) * 0.035f;
                UiKit.Glow(Cx(mc, my, 220f, 220f), new Color(Gold.r, Gold.g, Gold.b, 0.12f));
                // 四个方向的准星刻度：让标记更像"瞄准"而不是徽章
                var tick = new Color(Teal.r, Teal.g, Teal.b, 0.28f);
                UiKit.Line(R(mc - 1f, my - 78f, 2f, 14f), tick);
                UiKit.Line(R(mc - 1f, my + 64f, 2f, 14f), tick);
                UiKit.Line(R(mc - 78f, my - 1f, 14f, 2f), tick);
                UiKit.Line(R(mc + 64f, my - 1f, 14f, 2f), tick);
                DiamondRing(mc, my, 96f * pulse, Teal);
                DiamondRing(mc, my, 68f * pulse, Violet);
                UiKit.Sprite(Cx(mc, my, 28f * pulse, 28f * pulse), UiKit.Diamond, Gold);
                y += 140f;
            }

            float rise1 = Rise(t, 0.02f);
            Txt(_eyebrow, Center(w, y + rise1, 700, 24), Sp("DEEP SPACE ARCADE"), TealDim, TextAnchor.MiddleCenter);
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
            Txt(_small, Center(w, y, 700, 30), "穿过裂隙，在弹幕之间起舞。", Muted, TextAnchor.MiddleCenter);
            y += 62f;

            // --- 特性胶囊（两列）---
            string[] chips = { "12 种敌机 · 3 词缀", "22 种 Boss 招式 · 破防", "相位冲刺", "3 条武器线 ×5 级", "4 类僚机 · 3 层护盾", "10 个星区" };
            float chipW = 300f, chipH = 38f;
            for (int i = 0; i < chips.Length; i++)
            {
                int col = i % 2, row = i / 2;
                var r = R(cx - chipW - 6f + col * (chipW + 12f), y + row * 44f, chipW, chipH);
                UiKit.Slab(r, new Color(0.035f, 0.055f, 0.12f, 0.72f), Line(0.18f), 9f);
                UiKit.Line(R(r.x + 12f, r.y + 11f, 3f, r.height - 22f), new Color(Teal.r, Teal.g, Teal.b, 0.55f));
                Txt(_tiny, R(r.x + 24f, r.y, r.width - 34f, r.height), chips[i],
                    new Color(InkText.r, InkText.g, InkText.b, 0.86f), TextAnchor.MiddleLeft);
            }
            y += 3 * 44f + 14f;

            // --- 战绩卡 ---
            {
                var card = Center(w, y, 640f, 132f);
                UiKit.SlabGrad(card, new Color(0.075f, 0.122f, 0.216f, 0.90f),
                    new Color(0.020f, 0.036f, 0.082f, 0.90f), Line(0.22f), 18f);
                UiKit.Line(R(card.x + 18f, card.y + 1f, card.width - 36f, 2f),
                    new Color(Teal.r, Teal.g, Teal.b, 0.45f));
                Txt(_tiny, R(card.x + 40f, card.y + 26f, 260f, 22f), Sp("BEST SCORE"), TealDim);
                Bold(Label(44, Paper, TextAnchor.UpperLeft), R(card.x + 40f, card.y + 54f, 300f, 56f),
                    root.Best.ToString("N0"), Paper, 2f);
                UiKit.Line(R(card.center.x, card.y + 28f, 1f, 76f), Line(0.20f));
                Txt(_tiny, R(card.center.x + 40f, card.y + 26f, 260f, 22f), "最远星区", Gold);
                Bold(Label(44, Paper, TextAnchor.UpperLeft), R(card.center.x + 40f, card.y + 54f, 300f, 56f),
                    root.BestZone.ToString("00"), Paper, 2f);
                y += 170f;
            }

            // --- 难度三选 ---
            var diffs = Difficulties.All;
            float pickW = 640f, segW = (pickW - 12f) / 3f;
            var picker = Center(w, y, pickW, 92f);
            UiKit.SlabGrad(picker, new Color(0.055f, 0.085f, 0.165f, 0.86f),
                new Color(0.016f, 0.030f, 0.070f, 0.86f), Line(0.18f), 18f);
            for (int i = 0; i < diffs.Length; i++)
            {
                var r = R(picker.x + 6f + i * segW, picker.y + 6f, segW - 4f, 80f);
                bool on = root.Difficulty.Id == diffs[i].Id;
                var accent = diffs[i].Id == "abyss" ? Rose : Teal;
                if (on)
                {
                    UiKit.SlabGrad(r, new Color(accent.r, accent.g, accent.b, 0.30f),
                        new Color(accent.r, accent.g, accent.b, 0.08f),
                        new Color(accent.r, accent.g, accent.b, 0.65f), 12f);
                    UiKit.Line(R(r.x + 22f, r.y + 1f, r.width - 44f, 2f),
                        new Color(accent.r, accent.g, accent.b, 0.9f));
                }
                string en = diffs[i].Id == "cruise" ? "CRUISE" : diffs[i].Id == "abyss" ? "ABYSS" : "STANDARD";
                Txt(Label(21, on ? accent : InkText, TextAnchor.MiddleCenter), R(r.x, r.y + 12f, r.width, 28f),
                    diffs[i].Name, on ? accent : new Color(InkText.r, InkText.g, InkText.b, 0.82f));
                Txt(Label(11, on ? new Color(accent.r, accent.g, accent.b, 0.75f) : Dimc, TextAnchor.MiddleCenter),
                    R(r.x, r.y + 44f, r.width, 18f), Sp(en), on ? new Color(accent.r, accent.g, accent.b, 0.75f) : Dimc);
                if (GUI.Button(r, GUIContent.none, _invisible)) root.SetDifficulty(diffs[i]);
            }
            y += 108f;
            Txt(_tiny, Center(w, y, 700, 26), root.Difficulty.Note, new Color(Muted.r, Muted.g, Muted.b, 0.95f), TextAnchor.MiddleCenter);
            // 20:9 的大屏上「难度 → 开始键」之间会空出一大截（1080×1920 实测）：
            // 把这段余量分给标题下方与开始键上方，让整屏呼吸均匀，而不是全部堆在底部。
            float hintsTopEarly = RefH - bottom - 232.8f;
            float spare = Mathf.Max(0f, hintsTopEarly - 300f - (y + 54f));
            y += Mathf.Min(spare * 0.55f, 230f);
            y += 54f;

            Txt(_eyebrow, Center(w, y - 32f, 700, 22f), Sp("READY FOR LAUNCH"), TealDim, TextAnchor.MiddleCenter);

            // --- 开始按钮 ---
            {
                var btn = Center(w, y, 560f, 110f);
                var prev = GUI.color;
                UIButtonFrame(btn, true);
                UiKit.Line(R(btn.x + 18f, btn.y + 1f, btn.width - 36f, 2f), new Color(1f, 1f, 1f, 0.35f));
                Txt(_btnBig, btn, "开始巡航", new Color(0.02f, 0.06f, 0.11f, 1f));
                Txt(_kbd, R(btn.xMax - 64f, btn.y + 45f, 52f, 20f), "ENTER", new Color(0.02f, 0.06f, 0.11f, 0.5f));
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
            float colW = 330f, rowH = 62f;
            for (int i = 0; i < hints.Length; i++)
            {
                int col = i % 2, row = i / 2;
                var r = R(cx - colW - 6f + col * (colW + 12f), hintsTop + row * rowH, colW, rowH - 10f);
                UiKit.Slab(r, new Color(0.035f, 0.055f, 0.12f, 0.52f), Line(0.14f), 10f);
                var key = R(r.x + 10f, r.y + (r.height - 32f) * 0.5f, 68f, 32f);
                UiKit.Slab(key, new Color(0.04f, 0.067f, 0.14f, 0.78f), new Color(Teal.r, Teal.g, Teal.b, 0.30f), 5f);
                Txt(_kbd, key, hints[i][0], new Color(Teal.r, Teal.g, Teal.b, 0.95f));
                Txt(_small, R(key.xMax + 12f, r.y, r.width - key.width - 22f, r.height), hints[i][1],
                    new Color(InkText.r, InkText.g, InkText.b, 0.78f), TextAnchor.MiddleLeft);
            }

            Txt(_tiny, Center(w, RefH - bottom - 34f, 900f, 24f),
                Sp("v0.21.13") + "   ·   精准模式 / 擦弹经济 / 过关奖励 / 蓄能换线", Dimc, TextAnchor.MiddleCenter);
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

            // ---- 左上：分数块 ----
            // 结构固定为「SCORE → 大数字 → 分隔线 → BEST / GRAZE 双列」：
            // 标签在上、数值在下，两列共用同一条基线，避免旧版把 label/value 挤成一行。
            // 50 号数字的墨迹底落在 ScoreTop+55.2，BEST 行墨迹顶在 BestTop+7.6，
            // 两者之间留 CSS 折算的 10.4，分隔线放在这段空隙里。
            float panelY = topPad + 6f;
            UiKit.SlabGrad(R(pad - 10f, panelY, 320f, 150f),
                new Color(0.065f, 0.105f, 0.185f, 0.72f), new Color(0.006f, 0.010f, 0.028f, 0.72f),
                Line(0.18f), 18f);
            UiKit.Line(R(pad - 10f, panelY, 320f, 1f), new Color(Teal.r, Teal.g, Teal.b, 0.35f));
            UiKit.Line(R(pad - 10f, panelY + 18f, 2f, 124f), new Color(Teal.r, Teal.g, Teal.b, 0.65f));
            UiKit.Sprite(Cx(pad + 3f, panelY + 21f, 6f, 6f), UiKit.Diamond,
                new Color(Teal.r, Teal.g, Teal.b, 0.65f));
            Txt(Label(15, TealDim, TextAnchor.UpperLeft), R(pad + 16f, panelY + 14f, 280f, 22f), Sp("SCORE"), TealDim);
            Bold(_numBig, R(pad, topPad + ScoreTop, 292f, ScoreBoxH),
                Mathf.RoundToInt(root.Score).ToString("N0"), Paper, 2f);

            UiKit.Line(R(pad, topPad + 97.5f, 292f, 1f), Line(0.10f));
            float bestY = topPad + BestTop;
            float grazeY = topPad + GrazeTop;
            Txt(Label(14, Gold, TextAnchor.UpperLeft), R(pad, bestY, 136f, 18f), Sp("BEST"),
                new Color(Gold.r, Gold.g, Gold.b, 0.62f));
            Txt(Label(14, Teal, TextAnchor.UpperRight), R(pad + 164f, bestY, 136f, 18f), Sp("GRAZE"),
                new Color(Teal.r, Teal.g, Teal.b, 0.62f));
            UiKit.Line(R(pad + 152f, bestY + 2f, 1f, 24f), Line(0.12f));
            Bold(Label(19, Gold, TextAnchor.UpperLeft), R(pad, grazeY - 4f, 140f, 24f),
                root.Best.ToString("N0"), new Color(Gold.r, Gold.g, Gold.b, 0.92f), 1.2f);
            Bold(Label(19, Teal, TextAnchor.UpperRight), R(pad + 164f, grazeY - 4f, 136f, 24f),
                root.Stats.Graze.ToString(), Teal, 1.2f);

            // ---- 星区：独立成行，放在分数框下方，不再和分数抢同一条顶部横线 ----
            if (dir != null)
            {
                var strip = Cx(w * 0.5f, topPad + 187f, 360f, 42f);
                UiKit.SlabGrad(strip, new Color(0.055f, 0.095f, 0.175f, 0.78f),
                    new Color(0.010f, 0.018f, 0.048f, 0.78f), Line(0.20f), 12f);
                UiKit.Line(R(strip.x + 14f, strip.y + 1f, strip.width - 28f, 1f),
                    new Color(Teal.r, Teal.g, Teal.b, 0.35f));
                UiKit.Sprite(Cx(strip.x + 18f, strip.center.y, 6f, 6f), UiKit.Diamond,
                    new Color(Teal.r, Teal.g, Teal.b, 0.70f));
                Txt(Label(13, TealDim, TextAnchor.MiddleLeft), R(strip.x + 30f, strip.y, 82f, strip.height),
                    Sp("SECTOR"), TealDim);
                Bold(Label(26, Teal, TextAnchor.MiddleLeft), R(strip.x + 118f, strip.y, 52f, strip.height),
                    dir.Zone.ToString("00"), Teal, 1.2f);
                UiKit.Line(R(strip.x + 178f, strip.y + 9f, 1f, strip.height - 18f), Line(0.16f));
                Txt(Label(17, Muted, TextAnchor.MiddleLeft), R(strip.x + 192f, strip.y, strip.width - 202f, strip.height),
                    dir.ZoneName, new Color(InkText.r, InkText.g, InkText.b, 0.72f));
            }

            // ---- 右上：暂停（.hud-top 第三列，顶到 18.4）----
            var pause = Controls.PauseRect;
            UiKit.Slab(pause, new Color(0.039f, 0.067f, 0.14f, 0.72f), Line(0.22f), 14f);
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
            var card = R(pad, top + 226f, w - pad * 2f, 136f);   // 让开下方独立的星区行
            UiKit.SlabGrad(card, new Color(0.105f, 0.060f, 0.145f, 0.82f),
                new Color(0.016f, 0.014f, 0.045f, 0.82f), new Color(Rose.r, Rose.g, Rose.b, 0.30f), 16f);
            UiKit.Line(R(card.x + 20f, card.y + 1f, card.width - 40f, 2f),
                new Color(Rose.r, Rose.g, Rose.b, 0.55f));
            float inner = 26f;                       // 1px 边框 + 11px 内缩
            float ix = card.x + inner, iw = card.width - inner * 2f;

            // THREAT 标签（font 8、padding 2×5、边框）
            var tagStyle = Label(16, Rose, TextAnchor.MiddleCenter);
            float tagW = tagStyle.CalcSize(new GUIContent(Sp("THREAT"))).x + 20f;
            var tag = R(ix, card.y + 20f, tagW, 27f);
            UiKit.Slab(tag, new Color(Rose.r, Rose.g, Rose.b, 0.12f), new Color(Rose.r, Rose.g, Rose.b, 0.55f), 5f);
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
            UiKit.Slab(phase, new Color(phaseCol.r, phaseCol.g, phaseCol.b, b.Critical ? 0.18f : 0.05f),
                new Color(phaseCol.r, phaseCol.g, phaseCol.b, b.Critical ? 0.65f : 0.18f), 6f);
            Txt(Label(16, phaseCol, TextAnchor.MiddleCenter), phase,
                b.Critical ? Sp("CRIT") : "P" + (b.Phase + 1), phaseCol);

            // 血条（6px 内高 + 1px 边框 = 8px）+ 25% 分段刻线
            var track = R(ix, card.y + 58f, iw, 16f);
            UiKit.Slab(track, new Color(1f, 1f, 1f, 0.07f), new Color(Rose.r, Rose.g, Rose.b, 0.22f), 6f);
            float frac = Mathf.Clamp01(b.Hp / Mathf.Max(1f, b.MaxHp));
            if (frac > 0.001f)
                UiKit.Gradient(R(track.x + 2f, track.y + 2f, (track.width - 4f) * frac, track.height - 4f), UiKit.GradRose, Color.white);
            for (int i = 1; i < 4; i++)
                UiKit.Line(R(track.x + track.width * (i * 0.25f), track.y + 1f, 2f, track.height - 2f), new Color(0.016f, 0.024f, 0.059f, 0.75f));

            // 破防槽（3px，紧贴血条下方）
            var brk = R(ix, card.y + 80f, iw, 6f);
            UiKit.Slab(brk, new Color(Gold.r, Gold.g, Gold.b, 0.12f), new Color(Gold.r, Gold.g, Gold.b, 0.20f), 3f);
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
                UiKit.Line(R(ix, card.y + 98f, 2f, 20f), new Color(Gold.r, Gold.g, Gold.b, 0.55f));
                Txt(Label(15, Gold, TextAnchor.MiddleLeft), R(ix + 10f, card.y + 98f, iw - 10f, 20f),
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

            // 整条仪表带的底板：给五个元件一个共同重心，而不是散在渐变上
            var band = R(pad - 12f, bandTop + 12f, w - (pad - 12f) * 2f, 96f);
            UiKit.SlabGrad(band, new Color(0.045f, 0.075f, 0.145f, 0.55f),
                new Color(0.008f, 0.014f, 0.035f, 0.55f), Line(0.14f), 16f);
            UiKit.Line(R(band.x + 16f, band.y + 1f, band.width - 32f, 1f),
                new Color(Teal.r, Teal.g, Teal.b, 0.22f));

            // ---- 左：生存状态（上排舰体 / 下排护盾，两排以 58 为共同中心）----
            float x = pad;
            for (int i = 0; i < 5; i++)
            {
                var r = R(x + i * 26f, bandTop + 32f, 20f, 16f);
                bool on = i < p.Hull;
                UiKit.Sprite(r, UiKit.TriDown, on ? Teal : new Color(Muted.r, Muted.g, Muted.b, 0.16f));
            }
            for (int i = 0; i < GameConfig.MaxShields; i++)
            {
                var r = R(x + i * 20f, bandTop + 60f, 14f, 14f);
                bool on = i < p.Shields;
                UiKit.Sprite(r, UiKit.Hex, on ? Violet : new Color(Violet.r, Violet.g, Violet.b, 0.26f));
            }

            // ---- 左中：武器胶囊（26px 充能环 + 名字 + 5 格等级）----
            float chipX = x + 124f + 18f;            // dash-status 宽 62 + gap 9（×2）
            float ringCY = bandTop + 58f;            // 与动作环同一水平中心线
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
            Txt(Label(17, nameCol, TextAnchor.UpperLeft), R(copyX, bandTop + 24f, 84f, 22f), wname, nameCol);
            for (int i = 0; i < 5; i++)
            {
                // 名字与等级条之间留足字高，避免笔画压到格子上
                var pip = R(copyX + i * 17.5f, bandTop + 52f, 14f, 6f);
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
                    var rail = R(railX, bandTop + 55f, railW, 6f);
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

            Txt(Label(18, lit ? color : InkText, TextAnchor.MiddleCenter), R(r.x, r.y + 12f, r.width, 24f), label,
                lit ? color : InkText);
            Txt(Label(11, lit ? new Color(color.r, color.g, color.b, 0.9f) : Dimc, TextAnchor.MiddleCenter),
                R(r.x, r.y + 39f, r.width, 16f), sub, lit ? new Color(color.r, color.g, color.b, 0.9f) : Dimc);
        }

        static Rect Ring(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - d * 2f, r.height - d * 2f);

        // ---------------------------------------------------------------- 公告 / 提示 / 吐司

        void DrawAnnounce(GameRoot root, float w, float top)
        {
            if (string.IsNullOrEmpty(root.AnnounceTitle)) return;
            float age = Time.unscaledTime - root.AnnounceAt;
            float dur = Mathf.Max(0.2f, root.AnnounceDur);
            if (age > dur) return;

            float fadeIn = Mathf.Clamp01(age / 0.18f);
            float fadeOut = Mathf.Clamp01((dur - age) / 0.3f);
            float k = Mathf.Min(fadeIn, fadeOut);
            float slide = (1f - fadeIn) * 16f;
            float pop = 1f + (1f - fadeIn) * 0.10f;      // 入场 0.94→1 的轻微弹入

            var col = root.AnnounceAlert ? Rose : Paper;
            var subCol = root.AnnounceAlert ? Gold : Teal;
            var accent = root.AnnounceAlert ? Rose : Teal;

            int fs = Mathf.RoundToInt(46f * pop);
            var tmp = Label(fs, col, TextAnchor.MiddleCenter);
            float tw = Mathf.Min(tmp.CalcSize(new GUIContent(root.AnnounceTitle)).x + 90f, w - 120f);
            // 原版公告中心在 38% 高度（364.8px / 960），底部变体在 837.6px
            float y = root.AnnounceBottom ? 1675.2f : 729.6f;
            var box = Cx(w * 0.5f, y + slide, tw, (string.IsNullOrEmpty(root.AnnounceSub) ? 74f : 112f) * pop);

            UiKit.Glow(Cx(box.center.x, box.center.y, box.width * 1.35f, box.height * 1.7f),
                new Color(accent.r, accent.g, accent.b, 0.12f * k));
            UiKit.Slab(box, new Color(0.016f, 0.024f, 0.059f, 0.62f * k),
                new Color(accent.r, accent.g, accent.b, 0.30f * k), 14f);
            UiKit.Line(R(box.x + 22f, box.y + 1f, box.width - 44f, 2f),
                new Color(accent.r, accent.g, accent.b, 0.55f * k));
            Bold(tmp, R(box.x, box.y + 8f * pop, box.width, 56f * pop), root.AnnounceTitle,
                new Color(col.r, col.g, col.b, k), 2.2f);
            if (!string.IsNullOrEmpty(root.AnnounceSub))
                Txt(Label(16, subCol, TextAnchor.MiddleCenter), R(box.x, box.y + 64f * pop, box.width, 26f * pop),
                    Sp(root.AnnounceSub), new Color(subCol.r, subCol.g, subCol.b, k * 0.95f));

            // 两侧细线：原版每条 54px 长、离胶囊 12px
            float lx = box.x - 24f;
            if (lx > 20f)
            {
                UiKit.Line(R(lx - 120f, box.center.y - 1f, 120f, 2f), new Color(accent.r, accent.g, accent.b, 0.30f * k));
                UiKit.Line(R(box.xMax + 24f, box.center.y - 1f, 120f, 2f), new Color(accent.r, accent.g, accent.b, 0.30f * k));
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
                UiKit.Slab(r, new Color(0.027f, 0.043f, 0.10f, 0.72f * k),
                    new Color(Teal.r, Teal.g, Teal.b, 0.18f * k), 12f);
                Txt(st, r, root.Hint, new Color(InkText.r, InkText.g, InkText.b, 0.82f * k));
            }

            float ageT = Time.unscaledTime - root.ToastAt;
            if (!string.IsNullOrEmpty(root.Toast) && ageT < 1.9f)
            {
                float k = Mathf.Min(Mathf.Clamp01(ageT / 0.2f), Mathf.Clamp01((1.9f - ageT) / 0.3f));
                var st = Label(22, InkText, TextAnchor.MiddleCenter);
                float tw = Mathf.Min(st.CalcSize(new GUIContent(root.Toast)).x + 60f, w - 100f);
                // 飘字再高一档，贴在 695px（原版）
                var r = Cx(w * 0.5f, bandTop - 406f, tw, 58f);
                UiKit.Slab(r, new Color(0.027f, 0.043f, 0.10f, 0.92f * k),
                    new Color(Gold.r, Gold.g, Gold.b, 0.22f * k), 12f);
                Txt(st, r, root.Toast, new Color(Paper.r, Paper.g, Paper.b, k));
            }
        }

        // ---------------------------------------------------------------- 暂停

        void DrawPause(GameRoot root, float w, float top, float bottom)
        {
            Dim();
            float cardW = Mathf.Min(680f, w - 80f);
            float cardH = 1080f;
            var card = Cx(w * 0.5f, RefH * 0.5f, cardW, cardH);
            UiKit.Glow(Cx(card.center.x, card.center.y, cardW * 1.25f, cardH * 1.12f),
                new Color(Teal.r, Teal.g, Teal.b, 0.06f));
            UiKit.SlabGrad(card, new Color(0.075f, 0.115f, 0.20f, 0.96f),
                new Color(0.014f, 0.026f, 0.060f, 0.96f), Line(0.24f), 20f);
            UiKit.Line(R(card.x + 30f, card.y + 1f, card.width - 60f, 2f),
                new Color(Teal.r, Teal.g, Teal.b, 0.55f));

            float x = card.x + 44f, y = card.y + 40f;
            Txt(_eyebrow, R(x, y, 400f, 24f), Sp("SYSTEM HOLD"), TealDim);
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
            y = SettingRow(root, x, y, card.width - 88f, "触感震动", "受击 / 冲刺 / 星爆的短震", root.HapticsEnabled, () => root.ToggleHaptics());
            y += 24f;

            var resume = R(x, y, card.width - 88f, 100f);
            UIButtonFrame(resume, true);
            Txt(_btn, resume, "继续巡航", new Color(0.02f, 0.06f, 0.11f));
            Txt(_kbd, R(resume.xMax - 66f, resume.y + 40f, 52f, 20f), "ESC", new Color(0.02f, 0.06f, 0.11f, 0.45f));
            if (GUI.Button(resume, GUIContent.none, _invisible)) root.TogglePause();
            y += 118f;

            var again = R(x, y, card.width - 88f, 92f);
            UIButtonFrame(again, false);
            Txt(_btn, again, "重新开始", InkText);
            if (GUI.Button(again, GUIContent.none, _invisible)) root.Restart();
            y += 110f;

            var quit = R(x, y, card.width - 88f, 88f);
            UIButtonFrame(quit, false);
            Txt(Label(20, Muted, TextAnchor.MiddleCenter), quit, "返回基地", Muted);
            if (GUI.Button(quit, GUIContent.none, _invisible)) root.BackToMenu();
            y += 106f;

            Txt(Label(15, Dimc, TextAnchor.MiddleCenter), R(x, y, card.width - 88f, 22f),
                Sp("SPACE 冲刺   ·   SHIFT 精准   ·   X 星爆   ·   M 静音"),
                new Color(InkText.r, InkText.g, InkText.b, 0.55f));
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
                UiKit.Slab(r, new Color(0.41f, 0.96f, 0.86f, 0.07f), Line(0.18f), 8f);
                UiKit.Line(R(r.x + 9f, r.y + 9f, 2f, r.height - 18f), new Color(Teal.r, Teal.g, Teal.b, 0.45f));
                Txt(st, r, texts[i], new Color(InkText.r, InkText.g, InkText.b, 0.82f));
                x += tw + 8f;
            }
            return y + h + 8f;
        }

        float SettingRow(GameRoot root, float x, float y, float width, string title, string sub, bool on, System.Action toggle)
        {
            var row = R(x, y, width, 92f);
            Txt(Label(24, Paper, TextAnchor.MiddleLeft), R(row.x, row.y + 12f, row.width - 160f, 30f), title, Paper);
            Txt(Label(17, Muted, TextAnchor.MiddleLeft), R(row.x, row.y + 44f, row.width - 160f, 24f), sub,
                new Color(Muted.r, Muted.g, Muted.b, 0.95f));

            Txt(Label(13, on ? Teal : Dimc, TextAnchor.MiddleRight), R(row.xMax - 156f, row.y + 34f, 44f, 22f),
                on ? "ON" : "OFF", on ? Teal : Dimc);
            var t = R(row.xMax - 100f, row.y + 26f, 84f, 40f);
            UiKit.Pill(t, on ? new Color(Teal.r, Teal.g, Teal.b, 0.20f) : new Color(0.63f, 0.71f, 1f, 0.10f),
                on ? new Color(Teal.r, Teal.g, Teal.b, 0.65f) : Line(0.30f));
            var knob = Cx(on ? t.xMax - 22f : t.x + 22f, t.center.y, 30f, 30f);
            UiKit.Glow(knob, on ? new Color(Teal.r, Teal.g, Teal.b, 0.40f) : new Color(0f, 0f, 0f, 0f));
            UiKit.Sprite(knob, UiKit.Disc, on ? Teal : Muted);
            if (GUI.Button(row, GUIContent.none, _invisible)) toggle();
            UiKit.Line(R(row.x, row.yMax, row.width, 1f), Line(0.10f));
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
            float cardH = 920f;
            var card = Cx(cx, RefH * 0.5f, cardW, cardH);
            UiKit.Glow(Cx(card.center.x, card.center.y, cardW * 1.25f, cardH * 1.15f),
                new Color(Rose.r, Rose.g, Rose.b, 0.055f));
            UiKit.SlabGrad(card, new Color(0.085f, 0.075f, 0.145f, 0.96f),
                new Color(0.014f, 0.020f, 0.048f, 0.96f), Line(0.22f), 20f);
            UiKit.Line(R(card.x + 30f, card.y + 1f, card.width - 60f, 2f),
                new Color(Rose.r, Rose.g, Rose.b, 0.60f));

            float y = card.y + 36f;
            Txt(Label(16, Rose, TextAnchor.MiddleCenter), R(card.x, y, card.width, 22f),
                Sp("RUN TERMINATED"), Rose);
            y += 30f;
            Bold(Label(64, Paper, TextAnchor.MiddleCenter), R(card.x, y, card.width, 76f), "巡航终止", Paper, 2f);
            y += 80f;
            Txt(Label(18, Muted, TextAnchor.MiddleCenter), R(card.x, y, card.width, 26f), "信号消失在裂隙深处。", Muted);
            y += 40f;

            // 分数 + 菱形评级章
            var scoreBox = R(card.x + 44f, y, card.width - 220f, 112f);
            Txt(Label(14, TealDim, TextAnchor.UpperLeft), R(scoreBox.x, scoreBox.y, scoreBox.width, 20f),
                Sp("SCORE"), TealDim);
            GlowText(Label(64, Paper, TextAnchor.MiddleLeft), R(scoreBox.x, scoreBox.y + 22f, scoreBox.width, 84f),
                Mathf.RoundToInt(root.Score).ToString("N0"), Paper, Teal, TextAnchor.MiddleLeft);
            var rank = Cx(card.xMax - 96f, y + 58f, 108f, 108f);
            var rc = root.Grade switch { 4 => Teal, 3 => Violet, 2 => Gold, _ => Muted };
            UiKit.Glow(Cx(rank.center.x, rank.center.y, 150f, 150f), new Color(rc.r, rc.g, rc.b, 0.16f));
            UiKit.Slab(rank, new Color(rc.r, rc.g, rc.b, 0.10f), new Color(rc.r, rc.g, rc.b, 0.65f), 52f);
            Bold(Label(46, rc, TextAnchor.MiddleCenter), R(rank.x, rank.y + 22f, rank.width, 52f), root.GradeText, rc, 1.6f);
            Txt(Label(10, rc, TextAnchor.MiddleCenter), R(rank.x, rank.y + 72f, rank.width, 16f), Sp("RANK"),
                new Color(rc.r, rc.g, rc.b, 0.75f));
            y += 122f;

            UiKit.Line(R(card.x + 44f, y, card.width - 88f, 1f), Line(0.14f));
            UiKit.Sprite(Cx(card.center.x, y, 7f, 7f), UiKit.Diamond, new Color(Teal.r, Teal.g, Teal.b, 0.55f));
            y += 20f;

            if (root.Score >= root.Best && root.Score > 0)
            {
                var rec = Cx(card.center.x, y + 12f, 170f, 30f);
                UiKit.Slab(rec, new Color(Gold.r, Gold.g, Gold.b, 0.14f), new Color(Gold.r, Gold.g, Gold.b, 0.55f), 8f);
                Txt(Label(15, Gold, TextAnchor.MiddleCenter), rec, Sp("NEW RECORD"), Gold);
            }
            else
            {
                Txt(Label(15, Dimc, TextAnchor.MiddleCenter), R(card.x, y, card.width, 24f),
                    root.NextGradeGap > 0f ? "距上一档还差 " + Mathf.RoundToInt(root.NextGradeGap) + " 分"
                                           : "评级：按得分与最远星区综合判定", Dimc);
            }
            y += 38f;

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
                { "最高倍率", "×" + (1 + Mathf.Min(GameConfig.ComboMaxMult, s.MaxCombo / GameConfig.ComboStep)) },
            };
            float gw = (card.width - 88f - 12f) * 0.5f;
            for (int i = 0; i < 10; i++)
            {
                int col = i % 2, row = i / 2;
                var r = R(card.x + 44f + col * (gw + 12f), y + row * 58f, gw, 52f);
                UiKit.Slab(r, new Color(0.022f, 0.036f, 0.082f, 0.72f), Line(0.11f), 7f);
                UiKit.Line(R(r.x + 12f, r.y + 8f, 2f, r.height - 16f),
                    new Color(Teal.r, Teal.g, Teal.b, i % 2 == 0 ? 0.35f : 0.18f));
                Txt(Label(13, Dimc, TextAnchor.MiddleLeft), R(r.x + 22f, r.y + 4f, r.width - 32f, 18f), grid[i, 0], Dimc);
                Bold(Label(23, InkText, TextAnchor.MiddleLeft), R(r.x + 22f, r.y + 22f, r.width - 32f, 26f), grid[i, 1],
                    InkText, 1.2f);
            }
            y += 5 * 58f + 10f;

            if (root.LastClearLines != null && root.LastClearLines.Length > 0)
            {
                Txt(Label(12, Dimc, TextAnchor.MiddleCenter), R(card.x + 24f, y, card.width - 48f, 20f),
                    string.Join("  ·  ", root.LastClearLines), Dimc);
                y += 24f;
            }

            var retry = Cx(cx, y + 57f, card.width - 88f, 94f);
            UIButtonFrame(retry, true);
            UiKit.Line(R(retry.x + 18f, retry.y + 1f, retry.width - 36f, 2f), new Color(1f, 1f, 1f, 0.35f));
            Txt(_btn, retry, "再次出击", new Color(0.02f, 0.06f, 0.11f));
            Txt(_kbd, R(retry.xMax - 52f, retry.y + 37f, 40f, 20f), "R", new Color(0.02f, 0.06f, 0.11f, 0.5f));
            if (GUI.Button(retry, GUIContent.none, _invisible)) root.Restart();

            var menu = Cx(cx, y + 146f, card.width - 88f, 60f);
            UIButtonFrame(menu, false);
            Txt(Label(18, Muted, TextAnchor.MiddleCenter), menu, "返回基地", Muted);
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
            GUI.color = new Color(0.008f, 0.014f, 0.034f, 0.74f);
            GUI.DrawTexture(R(0, 0, w(), RefH), UiKit.White);
            GUI.color = prev;
        }

        float w() => Screen.height > 0 ? Screen.width / (Screen.height / RefH) : 1080f;

        // 主按钮：渐变填充 + 圆角描边（网页版 .btn--primary）
        void UIButtonFrame(Rect r, bool primary)
        {
            if (primary)
                UiKit.SlabGrad(r, new Color(0.60f, 0.98f, 0.92f), new Color(0.45f, 0.53f, 1f),
                    new Color(1f, 1f, 1f, 0.34f), 10f);
            else
                UiKit.SlabGrad(r, new Color(0.10f, 0.16f, 0.28f, 0.82f),
                    new Color(0.025f, 0.045f, 0.10f, 0.82f), Line(0.30f), 10f);
        }

        // 透明按钮样式（只吃输入，不画）
        GUIStyle _invisible;
    }
}
