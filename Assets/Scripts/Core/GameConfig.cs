using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 数值与配色的单一真相源。所有数值来自网页版 www/js/entities.js，
    /// 改这里之前先核对原版，避免两边分叉。
    /// </summary>
    public static class GameConfig
    {
        // ---- 世界口径：竖屏，宽 10 单位，高随屏幕比例延展 ----
        public const float WorldWidth = 10f;
        public const float PlayHalfWidth = WorldWidth * 0.5f;
        public const float OrthoSizeMin = 9f;      // 9:16 时半高
        public const float PlayerBoundY = 0.35f;   // 玩家活动区：屏幕底部约 65%

        // ---- 玩家 ----
        // 原版按像素：判定 4.6px、擦弹 28px；1px = 0.025 世界单位 → 0.115 / 0.7
        public const float PlayerHitRadius = 4.6f * 0.025f;
        public const float PlayerGrazeRadius = 28f * 0.025f;
        public const float PlayerSpeed = 9.5f;
        public const float PlayerPrecisionSpeedMul = 0.42f;
        public const int MaxShields = 3;
        public const float InvulnAfterHit = 1.6f;

        // ---- 武器线（原版 WEAPON_LINES）----
        public struct LineDef
        {
            public string Id, Label;
            public float Speed, Drain, Recover;
            public Color Tint, Deep;
            public LineDef(string id, string label, float speed, float drain, float recover, Color tint, Color deep)
            { Id = id; Label = label; Speed = speed; Drain = drain; Recover = recover; Tint = tint; Deep = deep; }
        }

        public static readonly Color Teal = Hex("35E0C8");
        public static readonly Color TealDeep = Hex("0F766E");
        public static readonly Color Rose = Hex("FF5C8A");
        public static readonly Color RoseDeep = Hex("9F1239");
        public static readonly Color Gold = Hex("FFC24B");
        public static readonly Color GoldDeep = Hex("92600A");

        public static readonly LineDef Pulse = new LineDef("pulse", "脉冲", 12.5f, 7.4f, 15.5f, Teal, TealDeep);
        public static readonly LineDef Prism = new LineDef("prism", "棱镜", 5.6f, 10.5f, 12f, Rose, RoseDeep);
        public static readonly LineDef Swarm = new LineDef("swarm", "蜂群", 8.3f, 8.6f, 13.5f, Gold, GoldDeep);

        public static LineDef Line(string id)
        {
            if (id == "prism") return Prism;
            if (id == "swarm") return Swarm;
            return Pulse;
        }

        // ---- 蓄能与共鸣 ----
        public const float DetunedPower = 0.55f;
        public const float DetunedFireRate = 0.72f;
        public const float ResonancePerStack = 0.08f;
        public const int ResonanceMaxStacks = 5;

        // ---- 弹幕 ----
        public const float BulletLifetime = 4f;

        // ---- 敌人 ----
        public const float EnemyLeashY = 0.9f;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
