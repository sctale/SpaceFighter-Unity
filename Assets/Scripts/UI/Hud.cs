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

        /// <summary>扫描线 + 暗角：网页版 .stage-scan / .stage-vignette 的效果。</summary>
        void DrawStageFx(float w, GameRoot root)
        {
            if (root.LowFx) { UiKit.Vignette(new Rect(0, 0, w, RefH), 0.35f); return; }
            UiKit.Scanlines(new Rect(0, 0, w, RefH), 0.055f);
            UiKit.Vignette(new Rect(0, 0, w, RefH), 0.55f);
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
            _numBig = Label(62, Paper);
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
            float pad = 40f;
            float cx = w * 0.5f;

            // 背景压暗 + 顶部网格光
            var scrim = new Rect(0, 0, w, RefH);
            GUI.color = new Color(0.016f, 0.024f, 0.059f, 0.93f);
            GUI.DrawTexture(scrim, UiKit.White);
            GUI.color = Color.white;
            UiKit.Glow(R(0, -220, w * 1.4f, 900), new Color(0.41f, 0.96f, 0.86f, 0.10f));

            float y = top + 118f;

            // --- 品牌标记：双层菱形环 + 金色核心（嵌套菱形做环）---
            {
                float mc = cx, my = y + 54f;
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 1.7f) * 0.035f;
                UiKit.Glow(Cx(mc, my, 190f, 190f), new Color(Gold.r, Gold.g, Gold.b, 0.10f));
                DiamondRing(mc, my, 104f * pulse, Teal);
                DiamondRing(mc, my, 72f * pulse, Violet);
                UiKit.Sprite(Cx(mc, my, 30f * pulse, 30f * pulse), UiKit.Diamond, Gold);
                y += 136f;
            }

            float rise1 = Rise(t, 0.02f);
            Txt(_eyebrow, Center(w, y + rise1, 700, 24), Sp("DEEP SPACE ARCADE"), TealDim);
            y += 44f;

            float rise2 = Rise(t, 0.06f);
            GlowText(_title, Center(w, y + rise2, w - 80, 140), "太空战斗机", Paper,
                new Color(Teal.r, Teal.g, Teal.b, 1f), TextAnchor.MiddleCenter);
            y += 146f;

            TextWithRules(Center(w, y, 620, 26), "AURORA RIFT   //   星穹涅槃", Muted, Line(0.28f));
            y += 44f;
            Txt(_small, Center(w, y, 700, 30), "穿过裂隙，在弹幕之间起舞。", Muted);
            y += 56f;

            // --- 特性胶囊（两列）---
            string[] chips = { "12 种敌机 · 3 词缀", "22 种 Boss 招式 · 破防", "相位冲刺", "3 条武器线 ×5 级", "4 类僚机 · 3 层护盾", "10 个星区" };
            float chipW = 300f;
            for (int i = 0; i < chips.Length; i++)
            {
                int col = i % 2, row = i / 2;
                var r = R(cx - chipW - 6f + col * (chipW + 12f), y + row * 40f, chipW, 34f);
                UiKit.Pill(r, new Color(0.027f, 0.043f, 0.10f, 0.42f), Line(0.12f));
                Txt(_tiny, r, chips[i], new Color(Muted.r, Muted.g, Muted.b, 0.92f));
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
            var picker = Center(w, y, pickW, 96f);
            UiKit.Pill(picker, new Color(0.027f, 0.043f, 0.10f, 0.55f), Line(0.13f));
            for (int i = 0; i < diffs.Length; i++)
            {
                var r = R(picker.x + 6f + i * segW, picker.y + 6f, segW - 4f, 84f);
                bool on = root.Difficulty.Id == diffs[i].Id;
                if (on)
                {
                    var accent = diffs[i].Id == "abyss" ? Rose : Teal;
                    UiKit.Pill(r, new Color(accent.r, accent.g, accent.b, 0.14f), new Color(accent.r, accent.g, accent.b, 0.5f));
                }
                Txt(Label(21, on ? (diffs[i].Id == "abyss" ? Rose : Teal) : Dimc, TextAnchor.MiddleCenter),
                    R(r.x, r.y + 14f, r.width, 30f), diffs[i].Name, on ? (diffs[i].Id == "abyss" ? Rose : Teal) : Dimc);
                Txt(_tiny, R(r.x, r.y + 48f, r.width, 22f), Sp(diffs[i].Id.ToUpperInvariant()),
                    new Color(Dimc.r, Dimc.g, Dimc.b, 0.75f));
                if (GUI.Button(r, GUIContent.none, _invisible)) root.SetDifficulty(diffs[i]);
            }
            y += 110f;
            Txt(_tiny, Center(w, y, 700, 26), root.Difficulty.Note, Dimc);
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

            // --- 操作提示（贴底两列）---
            float hintsTop = RefH - bottom - 430f;
            string[][] hints =
            {
                new[] { "拖动", "飞行 · 自动开火" },
                new[] { "武器卡", "点按切换武器线" },
                new[] { "精准", "慢速 + 收束 + 判定点" },
                new[] { "冲刺", "穿弹短暂无敌" },
                new[] { "星爆", "擦弹充能后释放" },
                new[] { "暂停", "右上角 II 或 P" },
            };
            float colW = 300f, rowH = 74f;
            for (int i = 0; i < hints.Length; i++)
            {
                int col = i % 2, row = i / 2;
                var r = R(cx - colW - 6f + col * (colW + 12f), hintsTop + row * rowH, colW, rowH - 10f);
                UiKit.Panel(r, new Color(0.027f, 0.043f, 0.10f, 0.34f), Line(0.09f), 12f);
                var key = R(r.x + 12f, r.y + (r.height - 40f) * 0.5f, 84f, 40f);
                UiKit.Panel(key, new Color(0.04f, 0.067f, 0.14f, 0.6f), Line(0.16f), 4f);
                Txt(_kbd, key, hints[i][0], Teal);
                Txt(_small, R(key.xMax + 14f, r.y, r.width - key.width - 22f, r.height), hints[i][1],
                    new Color(Muted.r, Muted.g, Muted.b, 0.95f), TextAnchor.MiddleLeft);
            }

            Txt(_tiny, Center(w, RefH - bottom - 46f, 900f, 24f),
                Sp("v0.17.0") + "   ·   精准模式 / 擦弹经济 / 过关奖励 / 蓄能换线", Dimc);
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
            float pad = 40f;
            var p = root.Player;
            var dir = Director.Instance;

            // 顶/底渐变压暗，让白字在任何背景下都可读
            var prev = GUI.color;
            GUI.color = new Color(0.016f, 0.024f, 0.059f, 0.55f);
            GUI.DrawTexture(R(0, 0, w, 190f + top), UiKit.White);
            GUI.color = new Color(0.016f, 0.024f, 0.059f, 0.62f);
            GUI.DrawTexture(R(0, RefH - bottom - 150f, w, 150f + bottom), UiKit.White);
            GUI.color = prev;

            // ---- 左上：分数块 ----
            Txt(_eyebrow, R(pad, top + 22f, 420f, 24f), Sp("SCORE"), Muted);
            Bold(_numBig, R(pad, top + 44f, 460f, 70f), Mathf.RoundToInt(root.Score).ToString("N0"), Paper, 2f);
            Txt(_tiny, R(pad, top + 116f, 300f, 24f), "BEST ", Dimc);
            Txt(_small, R(pad + 58f, top + 113f, 300f, 26f), root.Best.ToString("N0"), new Color(Gold.r, Gold.g, Gold.b, 0.85f));
            Txt(_tiny, R(pad, top + 144f, 300f, 24f), "GRAZE ", Dimc);
            Txt(_small, R(pad + 72f, top + 141f, 300f, 26f), root.Stats.Graze.ToString(), Teal);

            // ---- 中上：星区 ----
            if (dir != null)
            {
                Txt(Label(17, TealDim, TextAnchor.MiddleCenter), R(w * 0.5f - 220f, top + 22f, 440f, 24f), Sp("SECTOR"), TealDim);
                Bold(_num, R(w * 0.5f - 220f, top + 44f, 440f, 52f), dir.Zone.ToString("00"), Teal, 1.6f);
                Txt(Label(19, Dimc, TextAnchor.MiddleCenter), R(w * 0.5f - 220f, top + 94f, 440f, 26f),
                    dir.ZoneName, Muted);
            }

            // ---- 右上：暂停 ----
            var pause = Controls.PauseRect;
            UiKit.Pill(pause, new Color(0.039f, 0.067f, 0.14f, 0.55f), Line(0.16f));
            UiKit.Line(R(pause.center.x - 7f, pause.center.y - 14f, 5f, 28f), new Color(Muted.r, Muted.g, Muted.b, 0.9f));
            UiKit.Line(R(pause.center.x + 2f, pause.center.y - 14f, 5f, 28f), new Color(Muted.r, Muted.g, Muted.b, 0.9f));

            // ---- 连锁 ----
            if (root.Combo > 0)
            {
                float bx = w - pad - 170f;
                float by = top + (dir != null && dir.Boss != null ? 236f : 150f);
                Txt(Label(54, Gold, TextAnchor.MiddleRight), R(bx, by, 170f, 64f), "×" + root.Multiplier, Gold);
                Txt(Label(16, Dimc, TextAnchor.MiddleRight), R(bx, by + 62f, 170f, 22f), "CHAIN " + root.Combo, Muted);
                var track = R(bx + 38f, by + 88f, 132f, 4f);
                UiKit.Line(track, new Color(Gold.r, Gold.g, Gold.b, 0.16f));
                UiKit.Gradient(R(track.x, track.y, track.width * root.ComboRatio, track.height), UiKit.GradGold, new Color(1, 1, 1, 0.9f));
            }

            if (dir != null && dir.Boss != null) DrawBossPanel(root, w, top, dir.Boss);
            if (p != null) DrawBottom(root, w, bottom);

            if (root.State != GameState.Dead)
            {
                DrawAnnounce(root, w, top);
                DrawHintToast(root, w, bottom);
            }
        }

        void DrawBossPanel(GameRoot root, float w, float top, BossController b)
        {
            float pad = 40f;
            var card = R(pad, top + 150f, w - pad * 2f, 132f);
            UiKit.Panel(card, new Color(0.047f, 0.031f, 0.086f, 0.72f), new Color(Rose.r, Rose.g, Rose.b, 0.18f), 12f);

            // THREAT 标签
            var tag = R(card.x + 20f, card.y + 16f, 96f, 26f);
            UiKit.Panel(tag, new Color(Rose.r, Rose.g, Rose.b, 0.08f), new Color(Rose.r, Rose.g, Rose.b, 0.4f), 4f);
            Txt(Label(15, Rose, TextAnchor.MiddleCenter), tag, Sp("THREAT"), Rose);

            string name = b.BossLabel;
            Txt(Label(26, Paper, TextAnchor.MiddleLeft), R(tag.xMax + 14f, card.y + 12f, 420f, 34f), name, Paper);
            Txt(Label(18, Dimc, TextAnchor.MiddleLeft), R(tag.xMax + 14f + 210f, card.y + 16f, 420f, 28f),
                b.BossTitle, new Color(Dimc.r, Dimc.g, Dimc.b, 0.95f));

            // 阶段徽章（临界换成金色脉冲）
            var phase = R(card.xMax - 96f, card.y + 14f, 76f, 28f);
            var phaseCol = b.Critical ? Gold : Rose;
            UiKit.Pill(phase, new Color(phaseCol.r, phaseCol.g, phaseCol.b, b.Critical ? 0.14f : 0.02f),
                new Color(phaseCol.r, phaseCol.g, phaseCol.b, b.Critical ? 0.5f : 0f));
            Txt(Label(16, phaseCol, TextAnchor.MiddleCenter), phase,
                b.Critical ? Sp("CRIT") : "P" + (b.Phase + 1), phaseCol);

            // 血条 + 分段刻线
            var track = R(card.x + 20f, card.y + 58f, card.width - 40f, 12f);
            UiKit.Pill(track, new Color(1f, 1f, 1f, 0.07f), new Color(Rose.r, Rose.g, Rose.b, 0.14f));
            float frac = Mathf.Clamp01(b.Hp / Mathf.Max(1f, b.MaxHp));
            if (frac > 0.001f)
                UiKit.Gradient(R(track.x + 2f, track.y + 2f, (track.width - 4f) * frac, track.height - 4f), UiKit.GradRose, Color.white);
            for (int i = 1; i < 4; i++)
                UiKit.Line(R(track.x + track.width * (i * 0.25f), track.y + 1f, 2f, track.height - 2f), new Color(0.016f, 0.024f, 0.059f, 0.75f));

            // 破防槽
            var brk = R(card.x + 20f, card.y + 78f, card.width - 40f, 6f);
            UiKit.Pill(brk, new Color(Gold.r, Gold.g, Gold.b, 0.10f), Color.clear);
            float bf = Mathf.Clamp01(b.BreakGauge / Mathf.Max(1f, b.BreakMax));
            if (bf > 0.001f)
                UiKit.Line(R(brk.x, brk.y, brk.width * bf, brk.height), new Color(Gold.r, Gold.g, Gold.b, 0.75f));

            // 招式名（蓄招中才亮）
            if (b.TelegraphLeft > 0f)
            {
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 14f);
                Txt(Label(17, Gold, TextAnchor.MiddleLeft), R(card.x + 20f, card.y + 96f, card.width - 40f, 24f),
                    Sp("CAST") + "   " + BossMoves.Name(b.CurrentMove), new Color(Gold.r, Gold.g, Gold.b, pulse));
            }
            else
            {
                string hp = Mathf.RoundToInt(b.Hp) + " / " + Mathf.RoundToInt(b.MaxHp);
                Txt(Label(17, Dimc, TextAnchor.MiddleRight), R(card.x + 20f, card.y + 96f, card.width - 40f, 24f), hp, Dimc);
            }
        }

        void DrawBottom(GameRoot root, float w, float bottom)
        {
            float pad = 40f;
            float bandTop = Controls.BandTop;
            var p = root.Player;
            var line = GameConfig.Line(p.Weapons.ActiveId);

            // ---- 左：生存状态（上排舰体、下排护盾）----
            float x = pad;
            float cy = bandTop + 54f;
            for (int i = 0; i < 5; i++)
            {
                var r = R(x + i * 26f, cy - 9f, 18f, 18f);
                bool on = i < p.Hull;
                UiKit.Sprite(r, UiKit.TriDown, on ? Teal : new Color(Muted.r, Muted.g, Muted.b, 0.16f));
            }
            for (int i = 0; i < GameConfig.MaxShields; i++)
            {
                var r = R(x + i * 26f + 2f, cy + 16f, 16f, 16f);
                bool on = i < p.Shields;
                UiKit.Sprite(r, UiKit.Hex, on ? Violet : new Color(Violet.r, Violet.g, Violet.b, 0.26f));
            }

            // ---- 左中：武器胶囊（充能环 + 共鸣角标 + 名称 + 5 格等级）----
            {
                var cap = Controls.WeaponRect;
                float ringSize = 52f;
                var ring = Cx(cap.x + ringSize * 0.5f + 4f, cap.center.y, ringSize, ringSize);
                UiKit.Arc(ring, Mathf.Clamp01(p.Weapons.Active.Charge / 100f),
                    p.Weapons.Active.Detuned ? Rose : line.Tint, 5f);
                UiKit.Arc(Ring(ring, -6f), 1f, new Color(1f, 1f, 1f, 0.06f), 1f);
                if (p.Weapons.Resonance > 0.05f)
                    Txt(Label(15, Gold, TextAnchor.MiddleCenter), ring, "×" + Mathf.RoundToInt(p.Weapons.Resonance), Gold);

                var nameCol = p.Weapons.Active.Detuned ? Rose : InkText;
                Txt(Label(20, nameCol, TextAnchor.MiddleLeft), R(ring.xMax + 12f, cap.y + 4f, 150f, 26f),
                    line.Label, nameCol);
                for (int i = 0; i < 5; i++)
                {
                    var pip = R(ring.xMax + 12f + i * 22f, cap.y + 36f, 16f, 6f);
                    bool on = i < p.Weapons.Level;
                    UiKit.Line(pip, on ? line.Tint : new Color(0.63f, 0.71f, 1f, 0.18f));
                }
                if (p.Weapons.Active.Detuned)
                    Txt(Label(15, Rose, TextAnchor.MiddleLeft), R(ring.xMax + 12f, cap.y + 46f, 160f, 20f), "失谐排气", Rose);
            }

            // ---- 中：星区进度轨 + 终点菱形 ----
            {
                var dir = Director.Instance;
                float railX = Controls.WeaponRect.xMax + 26f;
                float railW = w - railX - 330f;
                if (railW > 60f)
                {
                    var rail = R(railX, bandTop + 58f, railW, 6f);
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
            float y = root.AnnounceBottom ? RefH - 300f : RefH * 0.36f;
            var box = Cx(w * 0.5f, y + slide, tw, string.IsNullOrEmpty(root.AnnounceSub) ? 74f : 112f);

            UiKit.Pill(box, new Color(0.016f, 0.024f, 0.059f, 0.55f * k), new Color(0.63f, 0.71f, 1f, 0.12f * k));
            Bold(tmp, R(box.x, box.y + 8f, box.width, 56f), root.AnnounceTitle, new Color(col.r, col.g, col.b, k), 2.2f);
            if (!string.IsNullOrEmpty(root.AnnounceSub))
                Txt(Label(17, subCol, TextAnchor.MiddleCenter), R(box.x, box.y + 64f, box.width, 26f),
                    Sp(root.AnnounceSub), new Color(subCol.r, subCol.g, subCol.b, k * 0.95f));

            // 两侧细线
            float lx = box.x - 20f;
            if (lx > 20f)
            {
                UiKit.Line(R(lx - 54f, box.center.y - 1f, 54f, 2f), new Color(Violet.r, Violet.g, Violet.b, 0.28f * k));
                UiKit.Line(R(box.xMax + 20f, box.center.y - 1f, 54f, 2f), new Color(Violet.r, Violet.g, Violet.b, 0.28f * k));
            }
        }

        void DrawHintToast(GameRoot root, float w, float bottom)
        {
            float ageH = Time.unscaledTime - root.HintAt;
            if (!string.IsNullOrEmpty(root.Hint) && ageH < 4.6f)
            {
                float k = Mathf.Min(Mathf.Clamp01(ageH / 0.3f), Mathf.Clamp01((4.6f - ageH) / 0.4f));
                var st = Label(21, Muted, TextAnchor.MiddleCenter);
                float tw = Mathf.Min(st.CalcSize(new GUIContent(root.Hint)).x + 60f, w - 100f);
                var r = Cx(w * 0.5f, RefH - bottom - 372f, tw, 56f);
                UiKit.Pill(r, new Color(0.027f, 0.043f, 0.10f, 0.6f * k), new Color(0.63f, 0.71f, 1f, 0.13f * k));
                Txt(st, r, root.Hint, new Color(Muted.r, Muted.g, Muted.b, k));
            }

            float ageT = Time.unscaledTime - root.ToastAt;
            if (!string.IsNullOrEmpty(root.Toast) && ageT < 1.9f)
            {
                float k = Mathf.Min(Mathf.Clamp01(ageT / 0.2f), Mathf.Clamp01((1.9f - ageT) / 0.3f));
                var st = Label(22, InkText, TextAnchor.MiddleCenter);
                float tw = Mathf.Min(st.CalcSize(new GUIContent(root.Toast)).x + 60f, w - 100f);
                var r = Cx(w * 0.5f, RefH - bottom - 464f, tw, 58f);
                UiKit.Pill(r, new Color(0.027f, 0.043f, 0.10f, 0.88f * k), new Color(0.63f, 0.71f, 1f, 0.14f * k));
                Txt(st, r, root.Toast, new Color(InkText.r, InkText.g, InkText.b, k));
            }
        }

        // ---------------------------------------------------------------- 暂停

        void DrawPause(GameRoot root, float w, float top, float bottom)
        {
            Dim();
            float cardW = Mathf.Min(680f, w - 80f);
            float cardH = 960f;
            var card = Cx(w * 0.5f, RefH * 0.5f, cardW, cardH);
            UiKit.Panel(card, new Color(0.063f, 0.094f, 0.188f, 0.94f), Line(0.14f), 16f);
            UiKit.Line(R(card.x + 28f, card.y + 1f, card.width - 56f, 2f), new Color(1f, 1f, 1f, 0.05f));

            float x = card.x + 44f, y = card.y + 40f;
            Txt(_eyebrow, R(x, y, 400f, 24f), Sp("SYSTEM HOLD"), Dimc);
            y += 34f;
            Bold(_h1c, R(x, y, card.width - 88f, 70f), "已暂停", Paper, 2f);
            y += 74f;
            Txt(_h2, R(x, y, card.width - 88f, 30f), "引擎待命。深空仍在呼吸。", Dimc);
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

            var quit = R(x, y, card.width - 88f, 52f);
            Txt(Label(18, Dimc, TextAnchor.MiddleCenter), quit, "返回基地", Dimc);
            if (GUI.Button(quit, GUIContent.none, _invisible)) root.BackToMenu();
            y += 56f;

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
            float y = top + 150f;
            var s = root.Stats;

            Txt(Label(17, Rose, TextAnchor.MiddleCenter), R(cx - 300f, y, 600f, 24f), Sp("RUN TERMINATED"), Rose);
            y += 40f;
            Bold(_h1c, R(cx - 300f, y, 600f, 72f), "巡航终止", Paper, 2f);
            y += 64f;
            Txt(Label(21, Muted, TextAnchor.MiddleCenter), R(cx - 300f, y, 600f, 30f), "信号消失在裂隙深处。", new Color(Muted.r, Muted.g, Muted.b, 0.9f));
            y += 56f;

            // 分数卡
            var card = Cx(cx, y + 130f, Mathf.Min(700f, w - 80f), 260f);
            UiKit.Panel(card, new Color(0.027f, 0.043f, 0.10f, 0.55f), Line(0.12f), 16f);
            UiKit.Glow(Cx(card.center.x, card.y + 10f, card.width * 0.9f, 200f), new Color(Teal.r, Teal.g, Teal.b, 0.08f));
            Txt(Label(15, Dimc, TextAnchor.MiddleCenter), R(card.x, card.y + 22f, card.width, 22f), Sp("SCORE"), Dimc);
            GlowText(_title, R(card.x, card.y + 44f, card.width - 160f, 150f),
                Mathf.RoundToInt(root.Score).ToString("N0"), Paper, Teal, TextAnchor.MiddleCenter);

            // 评级圆章
            var rank = Cx(card.xMax - 100f, card.y + 112f, 112f, 112f);
            var rc = root.Grade switch { 4 => Teal, 3 => Violet, 2 => Gold, _ => Muted };
            UiKit.Pill(rank, new Color(rc.r, rc.g, rc.b, 0.08f), new Color(rc.r, rc.g, rc.b, 0.5f));
            Bold(Label(54, rc, TextAnchor.MiddleCenter), R(rank.x, rank.y + 22f, rank.width, 60f), root.GradeText, rc, 1.6f);
            Txt(Label(11, rc, TextAnchor.MiddleCenter), R(rank.x, rank.y + 78f, rank.width, 18f), Sp("RANK"),
                new Color(rc.r, rc.g, rc.b, 0.7f));

            if (root.Score >= root.Best && root.Score > 0)
                Txt(Label(17, Gold, TextAnchor.MiddleCenter), R(card.x, card.yMax - 34f, card.width, 24f), "◆ 新纪录 ◆", Gold);

            y = card.yMax + 16f;
            Txt(Label(16, Dimc, TextAnchor.MiddleCenter), R(cx - 320f, y, 640f, 24f),
                root.NextGradeGap > 0f ? "距上一档还差 " + Mathf.RoundToInt(root.NextGradeGap) + " 分" : "评级：按得分与最远星区综合判定",
                Dimc);
            y += 44f;

            // 十项数据（两列五行）
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
            float gw = Mathf.Min(700f, w - 80f) * 0.5f - 6f;
            for (int i = 0; i < 10; i++)
            {
                int col = i % 2, row = i / 2;
                var r = R(cx - gw - 6f + col * (gw + 12f), y + row * 84f, gw, 76f);
                UiKit.Panel(r, new Color(0.027f, 0.043f, 0.10f, 0.55f), Line(0.09f), 12f);
                Txt(Label(18, Dimc, TextAnchor.MiddleLeft), R(r.x + 18f, r.y + 8f, r.width - 36f, 24f), grid[i, 0], Dimc);
                Bold(Label(32, InkText, TextAnchor.MiddleLeft), R(r.x + 18f, r.y + 32f, r.width - 36f, 36f), grid[i, 1], InkText, 1.4f);
            }
            y += 5 * 84f + 16f;

            if (root.LastClearLines != null && root.LastClearLines.Length > 0)
            {
                Txt(Label(16, Dimc, TextAnchor.MiddleCenter), R(cx - 340f, y, 680f, 24f),
                    string.Join("  ·  ", root.LastClearLines), Dimc);
                y += 34f;
            }

            var retry = Cx(cx, y + 60f, Mathf.Min(560f, w - 120f), 100f);
            UIButtonFrame(retry, true);
            Txt(_btn, retry, "再次出击", new Color(0.02f, 0.06f, 0.11f));
            Txt(_kbd, R(retry.xMax - 56f, retry.y + 40f, 44f, 20f), "R", new Color(0.02f, 0.06f, 0.11f, 0.45f));
            if (GUI.Button(retry, GUIContent.none, _invisible)) root.Restart();

            var menu = Cx(cx, y + 168f, Mathf.Min(560f, w - 120f), 64f);
            Txt(Label(18, Dimc, TextAnchor.MiddleCenter), menu, "返回基地", Dimc);
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
