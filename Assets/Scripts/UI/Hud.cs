using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// HUD 用 IMGUI 绘制：不依赖 uGUI 的 UI/Default 着色器（构建时会被剥离，界面会变品红），
    /// 也不需要在运行时创建材质，批处理构建下最稳。
    /// 参考分辨率 1080×1920，按屏幕高度等比缩放。
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public static Hud Instance;

        const float RefH = 1920f;

        GUIStyle _body, _small, _center, _boss, _clear;
        Texture2D _pad;
        Font _font;

        void Awake()
        {
            Instance = this;

            // 安卓上 CreateDynamicFontFromOSFont 只有 Roboto，中文会整段不显示。
            // 因此把 Noto Sans CJK SC 的子集字体随包带上（仅 UI 用到的 942 个字形，274KB）。
            _font = Resources.Load<Font>("Fonts/AuroraCJK");
            if (_font == null)
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Droid Sans Fallback", "sans-serif" }, 28);

            // 精准热区的圆形贴图（中心淡、边缘更淡）
            const int px = 96;
            _pad = new Texture2D(px, px, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var cols = new Color32[px * px];
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(px / 2f, px / 2f)) / (px / 2f);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) * 9f);
                    float fill = Mathf.Clamp01(1f - d) * 0.35f;
                    byte a = (byte)(Mathf.Clamp01(Mathf.Max(ring, fill)) * 255f);
                    cols[y * px + x] = new Color32(255, 255, 255, a);
                }
            _pad.SetPixels32(cols);
            _pad.Apply();
        }

        void EnsureStyles()
        {
            if (_body != null) return;
            _body = new GUIStyle(GUI.skin.label) { fontSize = 30, wordWrap = false };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            _center = new GUIStyle(GUI.skin.label)
            {
                fontSize = 40,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            _boss = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            _clear = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            if (_font != null)
            {
                _body.font = _font;
                _small.font = _font;
                _center.font = _font;
                _boss.font = _font;
                _clear.font = _font;
            }
        }

        void OnGUI()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            EnsureStyles();

            float scale = Screen.height / RefH;
            if (scale <= 0.01f) scale = 1f;
            float w = Screen.width / scale;

            // 安全区：Android 刘海/状态栏与 iOS 圆角，顶部内容整体下移
            Rect safe = Screen.safeArea;
            float topInset = (Screen.height - safe.yMax) / scale;
            float bottomInset = safe.yMin / scale;

            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            var white = Texture2D.whiteTexture;
            var c = GUI.color;

            DrawText(_body, new Rect(30, 20 + topInset, w - 60, 60),
                $"分数 {Mathf.RoundToInt(root.Score)}   最佳 {root.Best}",
                new Color(0.92f, 0.95f, 1f, 0.95f));

            var dir = Director.Instance;
            if (dir != null)
            {
                DrawText(_small, new Rect(w - 760, 26 + topInset, 730, 50),
                    $"{dir.WaveLabel} · {root.Difficulty.Name}", new Color(0.7f, 0.8f, 0.95f, 0.9f));
            }

            var p = root.Player;
            if (p != null)
            {
                DrawText(_body, new Rect(30, RefH - 210, 640, 60),
                    $"舰体 {p.Hull}   护盾 {p.Shields}/{GameConfig.MaxShields}   NOVA {Mathf.RoundToInt(p.Nova)}%",
                    new Color(0.85f, 0.95f, 1f, 0.95f));

                var line = GameConfig.Line(p.Weapons.ActiveId);
                var st = p.Weapons.Active;
                string det = st.Detuned ? " · 失谐" : "";
                DrawText(_body, new Rect(w * 0.5f - 300, RefH - 165, 600, 50),
                    $"{line.Label} Lv.{st.Level}   蓄能 {Mathf.RoundToInt(st.Charge)}%{det}",
                    st.Detuned ? new Color(1f, 0.55f, 0.4f) : line.Tint);

                // 蓄能条
                var bg = new Rect(w * 0.5f - 210, RefH - 105, 420, 10);
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
                GUI.DrawTexture(bg, white);
                GUI.color = line.Tint;
                GUI.DrawTexture(new Rect(bg.x, bg.y, bg.width * Mathf.Clamp01(st.Charge / 100f), bg.height), white);

                if (p.Weapons.Resonance > 0)
                {
                    DrawText(_small, new Rect(w * 0.5f - 300, RefH - 140, 600, 40),
                        $"共鸣 ×{Mathf.RoundToInt(p.Weapons.Resonance)}（+{Mathf.RoundToInt(p.Weapons.Resonance * 8)}% 伤害）",
                        new Color(1f, 0.85f, 0.6f, 0.95f));
                }
            }

            var boss = dir != null ? dir.Boss : null;
            if (boss != null)
            {
                float pct = Mathf.Clamp01(boss.Hp / Mathf.Max(1f, boss.MaxHp));
                var barBg = new Rect(w * 0.5f - 320, 120 + topInset, 640, 16);
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
                GUI.DrawTexture(barBg, white);
                GUI.color = boss.Critical ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.72f, 0.35f);
                GUI.DrawTexture(new Rect(barBg.x, barBg.y, barBg.width * pct, barBg.height), white);

                string flag = boss.Critical ? " · 临界" : "";
                string tele = boss.TelegraphLeft > 0f ? $"   预告 {BossMoves.Name(boss.CurrentMove)}" : "";
                DrawText(_boss, new Rect(w * 0.5f - 400, 145 + topInset, 800, 44),
                    $"BOSS {Mathf.RoundToInt(pct * 100f)}%   破防 {Mathf.RoundToInt(boss.BreakGauge)}%{flag}{tele}",
                    boss.TelegraphLeft > 0f ? new Color(1f, 0.6f, 0.45f) : new Color(1f, 0.82f, 0.6f));
            }

            DrawText(_small, new Rect(30, RefH - 50 - bottomInset, w - 60, 40),
                "拖动/方向键移动 · SHIFT/右下角 精准 · 1/2/3 换武器 · 空格 NOVA · Q 力场 · P 暂停",
                new Color(0.65f, 0.7f, 0.8f, 0.8f));

            if (root.Banner != null)
            {
                float age = Time.unscaledTime - root.BannerAt;
                if (age < 2.6f)
                {
                    float a = Mathf.Clamp01(2.6f - age);
                    DrawText(_center, new Rect(w * 0.5f - 400, RefH * 0.38f, 800, 80),
                        root.Banner, new Color(1f, 0.45f, 0.35f, a));
                }
            }

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

            // 精准热区（触屏）：右下角圆圈
            {
                float cx = w * 0.86f, cy = RefH * 0.87f, r = Mathf.Min(w, RefH) * 0.10f;
                bool active = p != null && p.TouchPrecision;
                GUI.color = new Color(1f, 1f, 1f, active ? 0.85f : 0.35f);
                GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2f, r * 2f), _pad);
                DrawText(_small, new Rect(cx - r, cy - 14, r * 2f, 32), "精准",
                    new Color(1f, 1f, 1f, active ? 0.95f : 0.55f));
            }

            if (root.State == GameState.Dead)
            {
                var s = root.Stats;
                string t = Mathf.FloorToInt(s.Time / 60f).ToString("00") + ":" +
                           Mathf.FloorToInt(s.Time % 60f).ToString("00");
                string gap = root.Grade >= 4
                    ? "已是最高档"
                    : $"距上一档还差 {Mathf.RoundToInt(root.NextGradeGap)} 分";
                DrawText(_center, new Rect(w * 0.5f - 480, RefH * 0.32f, 960, 420),
                    $"任务结束 · 评级 {root.GradeText}\n" +
                    $"分数 {Mathf.RoundToInt(root.Score)}   星区 {(dir != null ? dir.Zone : 1)}\n" +
                    $"击破 {s.Kills}   破核 {s.BossKills}   擦弹 {s.Graze}   受击 {s.Hits}   时长 {t}\n" +
                    $"{gap}\n\n点击或按 R 重新出击",
                    new Color(1f, 0.8f, 0.45f));
            }
            else if (root.State == GameState.Paused)
            {
                DrawText(_center, new Rect(w * 0.5f - 300, RefH * 0.45f, 600, 160),
                    "已暂停\n按 P 继续", new Color(0.85f, 0.9f, 1f));
            }

            GUI.color = c;
            GUI.matrix = prev;
        }

        static void DrawText(GUIStyle style, Rect r, string text, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = prev;
        }
    }
}
