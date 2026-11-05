using System.Collections.Generic;
using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 触屏控件布局：Hud 画的就是这里算的矩形，输入判定也用同一份，
    /// 彻底避免 v0.15.0「画得小、摸得大」和「手机上换武器/星爆/冲刺根本按不到」的问题。
    /// 对应网页版 index.html:80-89 的三枚动作环（精准 / 冲刺 / 星爆）+ 武器胶囊 + 暂停键。
    /// </summary>
    public static class Controls
    {
        public enum Kind { Precision, Dash, Nova, Weapon, Pause }

        public static Rect PrecisionRect { get; private set; }
        public static Rect DashRect { get; private set; }
        public static Rect NovaRect { get; private set; }
        public static Rect WeaponRect { get; private set; }
        public static Rect PauseRect { get; private set; }

        public static bool PrecisionHeld { get; private set; }
        static int _precisionFinger = -1;
        static bool _built;

        /// <summary>参考分辨率高度（与 Hud 一致）。</summary>
        public const float RefH = 1920f;
        public static float Scale => Screen.height > 0 ? Screen.height / RefH : 1f;
        /// <summary>参考空间宽度：随设备比例变化（9:16 → 1080，20:9 → 864）。</summary>
        public static float RefW => Screen.width / Mathf.Max(0.0001f, Scale);
        /// <summary>仪表带顶边（参考空间 y，向下增大）。</summary>
        public static float BandTop => RefH - 124f - SafeBottomRef;
        public static float SafeBottomRef => Screen.height > 0 ? (Screen.safeArea.yMin / Mathf.Max(0.0001f, Scale)) : 0f;
        public static float SafeTopRef => Screen.height > 0 ? ((Screen.height - Screen.safeArea.yMax) / Mathf.Max(0.0001f, Scale)) : 0f;

        /// <summary>
        /// 布局放在参考空间里（和 Hud 同一坐标系），命中判定再把触点换算过来，
        /// 这样「画出来的环」和「点得到的环」永远是同一块矩形。
        /// 对齐网页版：底部左端是生存状态 + 武器胶囊，中间是进度轨，右端三枚 40px 动作环。
        /// </summary>
        public static void Layout()
        {
            float w = RefW;
            const float ring = 80f;                    // 网页版 40px 直径 → 80 参考单位
            float cy = BandTop + 58f;

            NovaRect = C(w - 80f, cy, ring, ring);
            DashRect = C(NovaRect.center.x - 98f, cy, ring, ring);
            PrecisionRect = C(DashRect.center.x - 98f, cy, ring, ring);
            // 武器胶囊热区对齐 HUD 画出来的 chip（--pad 18.4 + dash-status 62 + gap 9，全部 ×2）
            WeaponRect = new Rect(36.8f + 124f + 18f, cy - 34f, 160f, 68f);
            // 暂停键贴在 .hud-top 第一行（top = pad = 18.4px），不是再往下 32px
            PauseRect = C(w - 36.8f - 42f, SafeTopRef + 36.8f + 42f, 84f, 84f);
            _built = true;
        }

        static Rect C(float cx, float cy, float w, float h) => new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);

        /// <summary>触点（屏幕像素）→ 参考空间。</summary>
        public static Vector2 ToRef(Vector2 screenPos)
        {
            float sc = Scale;
            return new Vector2(screenPos.x / sc, (Screen.height - screenPos.y) / sc);
        }

        public static bool Built => _built;

        static bool Hit(Rect rect, Vector2 p) => rect.Contains(p);

        /// <summary>
        /// 按下时先问控件：命中就吃掉这次按下（不驱动机体），返回 true。
        /// 冲刺/星爆/换武器/暂停在按下瞬间生效。
        /// </summary>
        public static bool ConsumePress(Vector2 screenPos, int fingerId = -1)
        {
            if (!_built) Layout();
            Vector2 p = ToRef(screenPos);
            if (Hit(PrecisionRect, p))
            {
                _precisionFinger = fingerId;
                PrecisionHeld = true;
                return true;
            }
            if (Hit(DashRect, p)) { GameRoot.Instance?.Player?.TryDash(); return true; }
            if (Hit(NovaRect, p)) { GameRoot.Instance?.Player?.TryNova(); return true; }
            if (Hit(WeaponRect, p)) { GameRoot.Instance?.Player?.Weapons?.CycleWeapon(); return true; }
            if (Hit(PauseRect, p)) { GameRoot.Instance?.TogglePause(); return true; }
            return false;
        }

        /// <summary>每帧调用：维护精准长按状态（多指时只要有一根压在精准环上就算按住）。</summary>
        public static void Tick()
        {
            if (!_built) return;
            bool held = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.touches[i];
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                if (Hit(PrecisionRect, ToRef(t.position))) held = true;
            }
            if (Input.GetMouseButton(0) && Hit(PrecisionRect, ToRef(Input.mousePosition))) held = true;
            if (_precisionFinger >= 0) held = true;
            PrecisionHeld = held;
        }

        public static void ReleaseFinger(int fingerId)
        {
            if (_precisionFinger == fingerId) _precisionFinger = -1;
        }
    }
}
