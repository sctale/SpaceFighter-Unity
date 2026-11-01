using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// HUD 用 IMGUI 绘制：不依赖 uGUI 的 UI/Default 着色器（构建时会被剥离，界面会变品红）。
    /// v0.16.0 起按网页版补齐四层界面：开始菜单 / 战斗仪表带 / 暂停设置 / 结算页，
    /// 并且手机上全部可点（网页版是 DOM 按钮，这里用同一套 Controls 矩形保证画得出=点得到）。
    /// 参考分辨率 1080×1920，按屏幕高度等比缩放。
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public static Hud Instance;

        const float RefH = 1920f;

        GUIStyle _body, _small, _center, _boss, _clear, _big, _btn, _btnPrimary, _empty;
        Texture2D _pad, _ringTex, _clearTex;
        Font _font;

        void Awake()
        {
            Instance = this;

            // 安卓上 CreateDynamicFontFromOSFont 只有 Roboto，中文会整段不显示：
            // 随包带 Noto Sans CJK SC 的子集字体（仅 UI 用到的 942 个字形）。
            _font = Resources.Load<Font>("Fonts/AuroraCJK");
            if (_font == null)
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Droid Sans Fallback", "sans-serif" }, 28);

            const int px = 96;
            _pad = new Texture2D(px, px, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _ringTex = new Texture2D(px, px, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var cols = new Color32[px * px];
            var ringCols = new Color32[px * px];
            for (int y = 0; y < px; y++)
            {
                for (int x = 0; x < px; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(px / 2f, px / 2f)) / (px / 2f);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) * 9f);
                    float fill = Mathf.Clamp01(1f - d) * 0.35f;
                    cols[y * px + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(ring, fill)) * 255f));
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) * 16f);
                    ringCols[y * px + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(edge) * 255f));
                }
            }
            _pad.SetPixels32(cols); _pad.Apply();
            _ringTex.SetPixels32(ringCols); _ringTex.Apply();
        }

        /// <summary>
        /// 样式全部自建（不继承 GUI.skin.label / GUI.skin.button）：内置样式的字号、字体、
        /// 背景贴图都不受控，手机上要的是自己定死字号。
        /// 这里顺手读一次 GUI.skin 只是把内置默认皮肤实例化出来（GUI 函数只能在 OnGUI 里调，
        /// 所以必须放在这里而不是 Awake）；真机上 HUD 整层不画的根因是 Strip Engine Code
        /// 把原生 MonoBehaviour 裁掉了，见 Assets/link.xml 与 BuildScript 的注释。
        /// </summary>
        void EnsureStyles()
        {
            if (_body != null) return;
            try { var _ = GUI.skin; } catch (System.Exception e) { Debug.LogWarning("[Aurora] GUI.skin: " + e.Message); }
            if (_clearTex == null)
            {
                _clearTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _clearTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                _clearTex.Apply();
            }
            _body = Label(30);
            _small = Label(22);
            _big = Label(64, TextAnchor.MiddleCenter);
            _center = Label(40, TextAnchor.MiddleCenter, true);
            _boss = Label(26, TextAnchor.MiddleCenter);
            _clear = Label(30, TextAnchor.MiddleCenter, true);
            _btn = Button(28);
            _btnPrimary = Button(34);
            _empty = new GUIStyle { normal = { background = _clearTex } };
        }

        GUIStyle Label(int size, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = false)
        {
            return new GUIStyle
            {
                font = _font,
                fontSize = size,
                alignment = anchor,
                wordWrap = wrap,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };
        }

        GUIStyle Button(int size)
        {
            var st = Label(size, TextAnchor.MiddleCenter);
            st.padding = new RectOffset(8, 8, 6, 6);
            st.normal.background = _clearTex;
            st.hover.background = _clearTex;
            st.active.background = _clearTex;
            st.focused.background = _clearTex;
            return st;
        }

        void OnGUI()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            EnsureStyles();

            float scale = Screen.height / RefH;
            if (scale <= 0.01f) scale = 1f;
            float w = Screen.width / scale;

            Rect safe = Screen.safeArea;
            float topInset = (Screen.height - safe.yMax) / scale;
            float bottomInset = safe.yMin / scale;

            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            var white = Texture2D.whiteTexture;
            var c = GUI.color;

            switch (root.State)
            {
                case GameState.Menu: DrawMenu(root, w, topInset); break;
                case GameState.Paused: DrawCombat(root, w, topInset, bottomInset); DrawPause(root, w); break;
                case GameState.Dead: DrawCombat(root, w, topInset, bottomInset); DrawDead(root, w); break;
                default: DrawCombat(root, w, topInset, bottomInset); break;
            }

            GUI.color = c;
            GUI.matrix = prev;
        }

        // ---------------------------------------------------------------- 开始菜单
        void DrawMenu(GameRoot root, float w, float topInset)
        {
            DrawText(_big, new Rect(0, 220 + topInset, w, 90), "AURORA RIFT", new Color(0.92f, 0.98f, 1f, 0.95f));
            DrawText(_small, new Rect(60, 320 + topInset, w - 120, 40), "穿过裂隙，在弹幕之间起舞",
                new Color(0.55f, 0.95f, 0.86f, 0.9f));

            var dir = Director.Instance;
            DrawText(_body, new Rect(60, 420 + topInset, w - 120, 50),
                $"最佳 {root.Best}   最远星区 {(dir != null ? dir.Zone : root.StartZone)}/10",
                new Color(0.85f, 0.9f, 1f, 0.9f));

            // 难度三选（手机上以前只能 F1/F2/F3）
            DrawText(_small, new Rect(60, 520 + topInset, w - 120, 36), "难度", new Color(0.7f, 0.8f, 0.95f, 0.9f));
            float bw = (w - 120) / 3f;
            var diffs = Difficulties.All;
            for (int i = 0; i < diffs.Length; i++)
            {
                var r = new Rect(60 + i * (bw + 15), 560 + topInset, bw, 96);
                bool on = root.Difficulty.Id == diffs[i].Id;
                Panel(r,
                    on ? new Color(0.16f, 0.62f, 0.56f, 0.85f) : new Color(0.10f, 0.14f, 0.22f, 0.75f),
                    on ? new Color(0.42f, 0.98f, 0.90f, 0.95f) : new Color(0.42f, 0.50f, 0.66f, 0.55f));
                DrawText(on ? _body : _small, new Rect(r.x + 6, r.y, r.width - 12, r.height), diffs[i].Name,
                    on ? new Color(0.94f, 1f, 0.99f, 1f) : new Color(0.78f, 0.84f, 0.95f, 0.9f),
                    TextAnchor.MiddleCenter);
                if (GUI.Button(new Rect(r.x, r.y, r.width, r.height), GUIContent.none, _empty))
                    root.SetDifficulty(diffs[i]);
            }
            DrawText(_small, new Rect(60, 668 + topInset, w - 120, 40), root.Difficulty.Note,
                new Color(0.7f, 0.8f, 0.95f, 0.85f));

            var startBtn = new Rect(w * 0.5f - 190, 780 + topInset, 380, 96);
            Panel(startBtn, new Color(0.16f, 0.62f, 0.56f, 0.9f), new Color(0.42f, 0.98f, 0.90f, 1f));
            if (GUI.Button(startBtn, "开始巡航", _btnPrimary))
                root.StartRun();

            DrawText(_small, new Rect(60, 940 + topInset, w - 120, 300),
                "拖动 / 鼠标 驾驶 · 自动开火\nSHIFT 或右下「精准」环 精准模式\nSPACE 或「冲刺」环 冲刺穿弹 · X 或「星爆」环 释放星爆\n1/2/3 或点武器胶囊 换线 · P/Esc 暂停\n护盾 3 层 · 力场可抵消一次致命 · 擦弹攒星爆\n敌机打空一条线会失谐，换线攒共鸣",
                new Color(0.62f, 0.7f, 0.85f, 0.9f));
            DrawText(_small, new Rect(60, 1290 + topInset, w - 120, 34), "v0.16.0",
                new Color(0.45f, 0.5f, 0.62f, 0.8f));
        }

        // ---------------------------------------------------------------- 战斗界面
        void DrawCombat(GameRoot root, float w, float topInset, float bottomInset)
        {
            var p = root.Player;
            var dir = Director.Instance;

            // ---- 顶栏：分数 / 最佳 / 擦弹 / 星区 ----
            DrawText(_body, new Rect(30, 24 + topInset, w * 0.62f, 56),
                $"分数 {Mathf.RoundToInt(root.Score)}   最佳 {root.Best}",
                new Color(0.92f, 0.95f, 1f, 0.95f));
            DrawText(_small, new Rect(30, 78 + topInset, w * 0.62f, 34),
                $"擦弹 {root.Stats.Graze}   连锁 ×{root.Multiplier}",
                new Color(0.55f, 0.95f, 0.86f, 0.9f));
            if (dir != null)
            {
                // 右边界停在暂停环左侧 18px：原来用 w*0.92 收尾，1080 宽的机器上会压到 II 上
                DrawText(_small, new Rect(w * 0.36f, 24 + topInset, w * 0.64f - 120f, 40),
                    $"{dir.WaveLabel} · {root.Difficulty.Name}", new Color(0.7f, 0.8f, 0.95f, 0.9f),
                    TextAnchor.UpperRight);
            }

            // ---- 右上暂停键（与 Controls.PauseRect 同一块矩形）----
            // 注意：这里只能画，不能挂 GUI.Button。触屏会被 Unity 合成成鼠标事件，
            // GUI.Button 和 Controls.ConsumePress 各调一次 TogglePause，正好抵消——
            // 真机上就是"点暂停毫无反应"。热区判定统一交给 Controls。
            var pause = Controls.PauseRect;
            GUI.color = new Color(1f, 1f, 1f, 0.22f);
            GUI.DrawTexture(pause, _ringTex);
            GUI.color = new Color(0.9f, 0.95f, 1f, 0.9f);
            DrawText(_small, pause, "II", new Color(0.9f, 0.95f, 1f, 0.9f), TextAnchor.MiddleCenter);

            if (p == null) return;
            var line = GameConfig.Line(p.Weapons.ActiveId);

            // ---- 底部仪表带（与 Controls 同一坐标系）----
            float bandY = Controls.BandTop;
            var hullCol = p.Hull <= 1 ? new Color(1f, 0.42f, 0.42f) : new Color(0.41f, 0.96f, 0.86f);
            for (int i = 0; i < Mathf.Max(3, p.Hull); i++)
            {
                var r = new Rect(40 + i * 26, bandY, 18, 12);
                GUI.color = i < p.Hull ? hullCol : new Color(1f, 1f, 1f, 0.14f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
            }
            for (int i = 0; i < GameConfig.MaxShields; i++)
            {
                var r = new Rect(40 + i * 22, bandY + 20, 16, 16);
                GUI.color = i < p.Shields ? new Color(0.61f, 0.55f, 1f, 0.95f) : new Color(0.61f, 0.55f, 1f, 0.22f);
                GUI.DrawTexture(r, _ringTex);
            }
            DrawText(_small, new Rect(40, bandY + 42, 320, 32),
                $"舰体 {p.Hull}   护盾 {p.Shields}/{GameConfig.MaxShields}",
                new Color(0.85f, 0.9f, 1f, 0.85f));

            // 蓄能环（圆心是共鸣层数）
            float cx = w * 0.5f + 40f, cy = bandY + 62f, rr = 34;
            GUI.color = new Color(1f, 1f, 1f, 0.16f);
            GUI.DrawTexture(new Rect(cx - rr, cy - rr, rr * 2, rr * 2), _ringTex);
            float charge = Mathf.Clamp01(p.Weapons.Active.Charge / 100f);
            GUI.color = p.Weapons.Active.Detuned ? new Color(1f, 0.5f, 0.4f, 0.95f) : line.Tint;
            DrawArc(cx, cy, rr - 3, -90f, -90f + 360f * charge, 5f);
            DrawText(_small, new Rect(cx - rr, cy - 16, rr * 2, 32),
                p.Weapons.Resonance > 0 ? "×" + Mathf.RoundToInt(p.Weapons.Resonance) : "",
                p.Weapons.Resonance >= 3 ? new Color(1f, 0.82f, 0.45f) : new Color(0.85f, 0.95f, 1f, 0.9f),
                TextAnchor.MiddleCenter);

            string wname = (p.Weapons.Active.Detuned ? "失谐排气" : line.Label) + " LV." + p.Weapons.Active.Level;
            DrawText(_small, new Rect(cx - 120, cy - rr - 34, 240, 28), wname, line.Tint, TextAnchor.MiddleCenter);
            for (int i = 0; i < 5; i++)
            {
                var r = new Rect(cx - 26 + i * 12, cy - rr - 6, 9, 4);
                GUI.color = i < p.Weapons.Active.Level ? line.Tint : new Color(1f, 1f, 1f, 0.18f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
            }

            // 星区进度轨 + Boss 节点
            float trackW = 150, trackX = w * 0.5f - trackW * 0.5f, trackY = bandY - 12;
            GUI.color = new Color(1f, 1f, 1f, 0.16f);
            GUI.DrawTexture(new Rect(trackX, trackY, trackW, 3), Texture2D.whiteTexture);
            float prog = dir != null ? dir.ZoneProgress * 0.86f : 0f;
            GUI.color = line.Tint;
            GUI.DrawTexture(new Rect(trackX, trackY, trackW * prog, 3), Texture2D.whiteTexture);
            GUI.color = dir != null && dir.Boss != null ? new Color(1f, 0.44f, 0.57f) : new Color(1f, 1f, 1f, 0.4f);
            GUI.DrawTexture(new Rect(trackX + trackW - 5, trackY - 3, 9, 9), Texture2D.whiteTexture);

            // ---- 右侧三枚动作环：精准 / 冲刺 / 星爆 ----
            DrawRing(Controls.PrecisionRect, "精准", p.FocusHeld ? 1f : 0f, line.Tint, p.FocusHeld);
            DrawRing(Controls.DashRect, p.DashCd > 0f ? p.DashCd.ToString("0.0") + "s" : "冲刺",
                1f - Mathf.Clamp01(p.DashCd / GameConfig.DashCooldown),
                new Color(0.55f, 0.95f, 0.86f), p.DashCd <= 0f);
            DrawRing(Controls.NovaRect, p.Nova >= 100f ? "星爆" : Mathf.RoundToInt(p.Nova) + "%",
                Mathf.Clamp01(p.Nova / 100f),
                p.Nova >= 100f && p.Overdrive <= 0f ? new Color(1f, 0.82f, 0.45f) : line.Tint,
                p.Nova >= 100f);
            // 武器胶囊（点一下循环换线）
            var cap = Controls.WeaponRect;
            Panel(cap, new Color(0.10f, 0.14f, 0.22f, 0.72f),
                new Color(line.Tint.r, line.Tint.g, line.Tint.b, 0.7f));
            DrawText(_small, new Rect(cap.x + 4, cap.y, cap.width - 8, cap.height),
                "换线 · " + line.Label, line.Tint, TextAnchor.MiddleCenter);

            // ---- 连锁倍率盒 ----
            if (root.Combo > 0)
            {
                var box = new Rect(w - 200, 150 + topInset, 170, 86);
                DrawText(new GUIStyle(_big) { fontSize = 40 }, new Rect(box.x, box.y, box.width, 52),
                    "×" + root.Multiplier, new Color(1f, 0.82f, 0.45f, 0.95f), TextAnchor.MiddleCenter);
                GUI.color = new Color(1f, 0.82f, 0.45f, 0.8f);
                GUI.DrawTexture(new Rect(box.x + 52, box.y + 60, 66 * root.ComboRatio, 2), Texture2D.whiteTexture);
                DrawText(_small, new Rect(box.x, box.y + 64, box.width, 24), "CHAIN " + root.Combo,
                    new Color(0.85f, 0.9f, 1f, 0.75f), TextAnchor.MiddleCenter);
            }

            // ---- Boss 面板 ----
            var boss = dir != null ? dir.Boss : null;
            if (boss != null)
            {
                float pct = Mathf.Clamp01(boss.Hp / Mathf.Max(1f, boss.MaxHp));
                var barBg = new Rect(w * 0.5f - 320, 128 + topInset, 640, 14);
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
                GUI.DrawTexture(barBg, Texture2D.whiteTexture);
                GUI.color = boss.Critical ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.72f, 0.35f);
                GUI.DrawTexture(new Rect(barBg.x, barBg.y, barBg.width * pct, barBg.height), Texture2D.whiteTexture);
                // 相位刻度（66% / 30%）
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(new Rect(barBg.x + barBg.width * (1f - GameConfig.BossPhaseHigh), barBg.y, 2, barBg.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(barBg.x + barBg.width * (1f - GameConfig.BossPhaseLow), barBg.y, 2, barBg.height), Texture2D.whiteTexture);
                // 破防槽
                GUI.color = boss.BreakGauge >= boss.BreakMax ? Color.white : new Color(1f, 0.82f, 0.45f, 0.9f);
                GUI.DrawTexture(new Rect(barBg.x, barBg.y + barBg.height + 3,
                    barBg.width * Mathf.Clamp01(boss.BreakGauge / Mathf.Max(1f, boss.BreakMax)), 3), Texture2D.whiteTexture);

                string flag = boss.Critical ? " · 临界" : "";
                string tele = boss.TelegraphLeft > 0f ? "   蓄招 · " + BossMoves.Name(boss.CurrentMove) : "";
                DrawText(_boss, new Rect(w * 0.5f - 400, barBg.yMax + 10, 800, 40),
                    $"BOSS {Mathf.RoundToInt(pct * 100f)}%   破防 {Mathf.RoundToInt(boss.BreakGauge / Mathf.Max(1f, boss.BreakMax) * 100f)}%{flag}{tele}",
                    boss.TelegraphLeft > 0f ? new Color(1f, 0.6f, 0.45f) : new Color(1f, 0.82f, 0.6f));
            }

            // ---- 击破结算明细（Boss 死后 5 秒）----
            if (root.LastClearLines != null)
            {
                float age = Time.unscaledTime - root.LastClearAt;
                if (age < 5f)
                {
                    float a = Mathf.Clamp01(5f - age);
                    DrawText(_clear, new Rect(w * 0.5f - 400, RefH * 0.26f, 800, 300),
                        "核心击破结算\n" + string.Join("\n", root.LastClearLines),
                        new Color(0.55f, 1f, 0.85f, a));
                }
            }

            // ---- 公告 / 教学 / 吐司 ----
            DrawTimed(root.Banner, BannerAt(root), new Rect(w * 0.5f - 400, RefH * 0.38f, 800, 80),
                _center, new Color(1f, 0.45f, 0.35f), 2.6f);
            DrawTimed(root.Hint, HintAt(root), new Rect(60, Controls.BandTop - 60, w - 120, 40),
                _small, new Color(0.85f, 0.95f, 1f, 0.85f), 4.5f);
            DrawTimed(root.Toast, ToastAt(root), new Rect(60, Controls.BandTop - 100, w - 120, 36),
                _small, new Color(1f, 0.82f, 0.45f, 0.9f), 1.8f);

            // 一命以下：屏幕边缘红色渐晕
            if (p.Hull <= 1)
            {
                GUI.color = new Color(1f, 0.25f, 0.25f, 0.10f + 0.05f * Mathf.Sin(Time.unscaledTime * 3f));
                GUI.DrawTexture(new Rect(0, 0, w, RefH), _ringTex);
            }
        }

        float BannerAt(GameRoot r) => r.BannerAt;
        float HintAt(GameRoot r) => r.HintAt;
        float ToastAt(GameRoot r) => r.ToastAt;

        void DrawTimed(string text, float at, Rect box, GUIStyle style, Color col, float life)
        {
            if (string.IsNullOrEmpty(text)) return;
            float age = Time.unscaledTime - at;
            if (age > life) return;
            float a = col.a * Mathf.Clamp01((life - age) / 0.6f);
            DrawText(style, box, text, new Color(col.r, col.g, col.b, a));
        }

        void DrawRing(Rect rect, string sub, float fill, Color col, bool ready)
        {
            var r = rect;
            float cx = r.x + r.width * 0.5f, cy = r.y + r.height * 0.5f, rr = r.width * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, ready ? 0.3f : 0.16f);
            GUI.DrawTexture(r, _pad);
            GUI.color = new Color(1f, 1f, 1f, 0.22f);
            GUI.DrawTexture(new Rect(cx - rr, cy - rr, rr * 2, rr * 2), _ringTex);
            GUI.color = col;
            DrawArc(cx, cy, rr - 2, -90f, -90f + 360f * Mathf.Clamp01(fill), 4f);
            DrawText(_small, new Rect(r.x, cy - 14, r.width, 28), sub, new Color(col.r, col.g, col.b, 0.95f),
                TextAnchor.MiddleCenter);
        }

        /// <summary>用一小段扇形贴图近似充能环（IMGUI 没有原生圆弧）。</summary>
        void DrawArc(float cx, float cy, float radius, float from, float to, float thickness)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(to - from) / 12f));
            for (int i = 0; i < steps; i++)
            {
                float a = Mathf.Lerp(from, to, (i + 0.5f) / steps);
                float rad = a * Mathf.Deg2Rad;
                float x = cx + Mathf.Sin(rad) * radius;
                float y = cy - Mathf.Cos(rad) * radius;
                GUI.DrawTexture(new Rect(x - thickness * 0.5f, y - thickness * 0.5f, thickness, thickness),
                    Texture2D.whiteTexture);
            }
        }

        // ---------------------------------------------------------------- 暂停
        void DrawPause(GameRoot root, float w)
        {
            GUI.color = new Color(0.02f, 0.03f, 0.07f, 0.82f);
            GUI.DrawTexture(new Rect(0, 0, w, RefH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            DrawText(_center, new Rect(0, 300, w, 80), "已暂停", new Color(0.9f, 0.95f, 1f));
            var p = root.Player;
            if (p != null)
            {
                string chips = $"脉冲 LV.{p.Weapons.Pulse.Level}{(p.Weapons.Pulse.Unlocked ? "" : " ·未解锁")}   " +
                               $"棱镜 LV.{p.Weapons.Prism.Level}{(p.Weapons.Prism.Unlocked ? "" : " ·未解锁")}   " +
                               $"蜂群 LV.{p.Weapons.Swarm.Level}{(p.Weapons.Swarm.Unlocked ? "" : " ·未解锁")}\n" +
                               $"护盾 {p.Shields}/{GameConfig.MaxShields}   僚机 {p.DroneCount}/{GameConfig.MaxDrones}   " +
                               $"力场 {(p.ReflectT > 0f ? Mathf.CeilToInt(p.ReflectT) + "s" : "无")}";
                DrawText(_small, new Rect(0, 400, w, 90), chips, new Color(0.7f, 0.8f, 0.95f, 0.95f),
                    TextAnchor.MiddleCenter);
            }

            ToggleRow(root, 520, "音乐音效", !root.Muted, () => root.ToggleMute());
            ToggleRow(root, 590, "轻柔特效", root.LowFx, () => root.ToggleLowFx());
            ToggleRow(root, 660, "高对比敌弹", root.HighContrast, () => root.ToggleHighContrast());

            float bw = 300;
            var resumeBtn = new Rect(w * 0.5f - bw * 0.5f, 780, bw, 80);
            Panel(resumeBtn, new Color(0.16f, 0.62f, 0.56f, 0.9f), new Color(0.42f, 0.98f, 0.90f, 1f));
            if (GUI.Button(resumeBtn, "继续出击", _btnPrimary)) root.TogglePause();
            var againBtn = new Rect(w * 0.5f - bw * 0.5f, 880, bw, 72);
            Panel(againBtn, new Color(0.10f, 0.14f, 0.22f, 0.8f), new Color(0.42f, 0.50f, 0.66f, 0.6f));
            if (GUI.Button(againBtn, "重新开始", _btn)) root.Restart();
            DrawText(_small, new Rect(0, 980, w, 34), "难度：F1 巡航 / F2 标准 / F3 深渊（或回开始菜单选）",
                new Color(0.6f, 0.68f, 0.8f, 0.85f), TextAnchor.MiddleCenter);
        }

        void ToggleRow(GameRoot root, float y, string label, bool on, System.Action toggle)
        {
            float w = Screen.width / (Screen.height / RefH);
            var r = new Rect(w * 0.5f - 220, y, 440, 56);
            if (GUI.Button(r, GUIContent.none, _empty)) toggle();
            DrawText(_body, new Rect(r.x, r.y + 8, 260, 44), label, new Color(0.85f, 0.9f, 1f, 0.95f));
            var knob = new Rect(r.xMax - 76, r.y + 12, 60, 32);
            GUI.color = on ? new Color(0.2f, 0.88f, 0.78f, 0.9f) : new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(knob, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(on ? knob.xMax - 26 : knob.x + 2, knob.y + 2, 22, 28), Texture2D.whiteTexture);
        }

        // ---------------------------------------------------------------- 结算
        void DrawDead(GameRoot root, float w)
        {
            GUI.color = new Color(0.02f, 0.03f, 0.07f, 0.86f);
            GUI.DrawTexture(new Rect(0, 0, w, RefH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var dir = Director.Instance;
            var s = root.Stats;
            float t = s.Time;
            string time = Mathf.FloorToInt(t / 60f).ToString("00") + ":" + Mathf.FloorToInt(t % 60f).ToString("00");
            string gap = root.Grade >= 4 ? "已是最高档" : $"距上一档还差 {Mathf.RoundToInt(root.NextGradeGap)} 分";

            DrawText(_big, new Rect(0, 220, w, 100), "任务结束 · 评级 " + root.GradeText,
                new Color(1f, 0.8f, 0.45f));
            DrawText(_small, new Rect(0, 330, w, 40), gap, new Color(0.7f, 0.8f, 0.95f, 0.9f),
                TextAnchor.MiddleCenter);

            // 十项统计（与网页版一致）
            string stats =
                $"星区 {(dir != null ? dir.Zone : 1)}   击落 {s.Kills}   最高连锁 {s.MaxCombo}   时长 {time}\n" +
                $"擦弹 {s.Graze}   星爆 {s.Novas}   相位穿越 {s.Phases}   核心破防 {s.Breaks}\n" +
                $"受创 {s.Hits}   最高倍率 ×{1 + Mathf.Min(GameConfig.ComboMaxMult, s.MaxCombo / GameConfig.ComboStep)}\n" +
                $"分数 {Mathf.RoundToInt(root.Score)}   最佳 {root.Best}";
            DrawText(_clear, new Rect(60, 420, w - 120, 300), stats, new Color(0.88f, 0.93f, 1f, 0.95f));

            float bw = 320;
            var retryBtn = new Rect(w * 0.5f - bw * 0.5f, 820, bw, 84);
            Panel(retryBtn, new Color(0.16f, 0.62f, 0.56f, 0.9f), new Color(0.42f, 0.98f, 0.90f, 1f));
            if (GUI.Button(retryBtn, "再次出击", _btnPrimary)) root.Restart();
            DrawText(_small, new Rect(0, 940, w, 34), "点击任意处或按 R 重新出击",
                new Color(0.6f, 0.68f, 0.8f, 0.8f), TextAnchor.MiddleCenter);
            if (Input.GetMouseButtonDown(0) && Screen.height > 0)
            {
                var mp = Input.mousePosition;
                // 别跟「再次出击」按钮抢：触屏会同时命中 GUI.Button 和这里，
                // 一帧里 Restart 两次等于把刚重建的世界再拆一次。
                float sc = Screen.height / RefH;
                var btnScreen = new Rect(retryBtn.x * sc, Screen.height - retryBtn.yMax * sc,
                    retryBtn.width * sc, retryBtn.height * sc);
                if (mp.y < Screen.height - 60 && !btnScreen.Contains(new Vector2(mp.x, mp.y)))
                    root.Restart();
            }
        }

        /// <summary>
        /// 半透明面板：一块填充 + 1.5px 描边。比整块白色贴图按 alpha 压出来干净，
        /// 也更接近网页版的扁平面板观感（原来三档难度是两块死灰的实心板）。
        /// </summary>
        void Panel(Rect r, Color fill, Color border)
        {
            const float t = 1.5f;
            GUI.color = fill;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = border;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y + t, t, r.height - t * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y + t, t, r.height - t * 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void DrawText(GUIStyle style, Rect r, string text, Color color, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var prev = GUI.color;
            var st = style;
            if (anchor != TextAnchor.UpperLeft)
            {
                st = new GUIStyle(style) { alignment = anchor };
            }
            GUI.color = color;
            GUI.Label(r, text, st);
            GUI.color = prev;
        }
    }
}
