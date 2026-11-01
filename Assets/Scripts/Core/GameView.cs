using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 运行期视口：网页版的画布是「W 固定 540、H 随设备比例变化」，所以所有
    /// 边界都必须用相机实际半高算，不能用常量。20:9 的手机上半高约 11.1，
    /// 用固定 9 会导致玩家够不到屏幕底部、敌机在半空消失。
    /// </summary>
    public static class GameView
    {
        public static float HalfHeight { get; private set; } = GameConfig.OrthoSizeMin;
        public static float Aspect { get; private set; } = 9f / 16f;

        public static float HalfWidth => GameConfig.PlayHalfWidth;
        /// <summary>可活动左右边界（原版 x∈[31, W-31]）。</summary>
        public static float MoveHalfWidth => GameConfig.PlayHalfWidth - GameConfig.BoundSideInsetPx * GameConfig.Px;
        /// <summary>可活动上界（原版 y = H×0.18）。</summary>
        public static float MoveTopY => GameConfig.FromTopFrac(GameConfig.BoundTopFrac, HalfHeight);
        /// <summary>可活动下界（原版 y = H-104，给仪表带留位）。</summary>
        public static float MoveBottomY => -HalfHeight + GameConfig.BoundBottomInsetPx * GameConfig.Px;
        /// <summary>掉命后的复活点（原版 H-120）。</summary>
        public static float RespawnY => -HalfHeight + GameConfig.RespawnInsetPx * GameConfig.Px;
        /// <summary>开局点（原版 H-130）。</summary>
        public static float StartY => -HalfHeight + 130f * GameConfig.Px;
        /// <summary>出怪线：屏幕上方一点。</summary>
        public static float SpawnY => HalfHeight + 0.4f;
        /// <summary> despawn 线：屏幕下方一点。</summary>
        public static float DespawnY => -HalfHeight - 0.6f;

        public static float FromTopPx(float px) => GameConfig.FromTopPx(px, HalfHeight);
        public static float FromTopFrac(float f) => GameConfig.FromTopFrac(f, HalfHeight);

        /// <summary>出场公平性的下边界：原版 y &lt; H×0.94 才算合法入屏。</summary>
        public static float EntryBottomY => GameConfig.FromTopFrac(GameConfig.EntryBottomFrac, HalfHeight);

        public static void Refresh(Camera cam)
        {
            if (cam == null) return;
            HalfHeight = cam.orthographicSize;
            Aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 9f / 16f;
        }

        /// <summary>相机半高：宽屏按宽度撑满，高屏按设备比例拉长（与网页版 measure() 同式）。</summary>
        public static float ComputeHalfHeight(float aspect)
        {
            return Mathf.Max(GameConfig.OrthoSizeMin, GameConfig.PlayHalfWidth / Mathf.Max(0.2f, aspect));
        }
    }
}
