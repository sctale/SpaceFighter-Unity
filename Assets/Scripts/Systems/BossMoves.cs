using UnityEngine;

namespace AuroraRift
{
    public enum BossMove
    {
        Ring, RingDouble, Fan, FanWide, Aimed, AimedBurst,
        Spiral, SpiralReverse, WallGap, WallRotate, LaserSweep, LaserCross,
        Mines, MineField, Teleport, Dash, SpiralDual, AimedRing,
        BulletRain, Converge, Crossfire, FinalBurst
    }

    /// <summary>
    /// 原版「22 种 Boss 招式」。这里把每招实现成一次可复现的弹幕发射，
    /// 三台 Boss 各自 3 套阶段序列（固定顺序循环、可背板）。
    /// </summary>
    public static class BossMoves
    {
        public static string Name(BossMove m) => m switch
        {
            BossMove.Ring => "环爆",
            BossMove.RingDouble => "双层环",
            BossMove.Fan => "扇形",
            BossMove.FanWide => "大扇形",
            BossMove.Aimed => "追踪射击",
            BossMove.AimedBurst => "三连追踪",
            BossMove.Spiral => "螺旋弹幕",
            BossMove.SpiralReverse => "反向螺旋",
            BossMove.WallGap => "缺口弹墙",
            BossMove.WallRotate => "旋转弹墙",
            BossMove.LaserSweep => "扫射光束",
            BossMove.LaserCross => "十字光束",
            BossMove.Mines => "布雷",
            BossMove.MineField => "雷区",
            BossMove.Teleport => "位移突袭",
            BossMove.Dash => "冲撞",
            BossMove.SpiralDual => "双螺旋",
            BossMove.AimedRing => "追踪环",
            BossMove.BulletRain => "弹雨",
            BossMove.Converge => "收敛弹幕",
            BossMove.Crossfire => "交叉火力",
            BossMove.FinalBurst => "终末爆发",
            _ => "未知",
        };

        /// <summary>热身时长（招式预告），秒；激光/弹雨更长。</summary>
        public static float Telegraph(BossMove m) => m switch
        {
            BossMove.LaserSweep or BossMove.LaserCross => 0.85f,
            BossMove.FinalBurst or BossMove.BulletRain => 0.75f,
            BossMove.Dash or BossMove.Teleport => 0.6f,
            _ => 0.5f,
        };

        // 三台 Boss 各自的 3 阶段序列：热身 4 招、二阶段 6 招、三阶段 6 招，按序循环（可背板）。
        static readonly BossMove[][][] Sequences =
        {
            // Gatekeeper：环爆型
            new[]
            {
                new[] { BossMove.Ring, BossMove.Fan, BossMove.Aimed, BossMove.RingDouble },
                new[] { BossMove.Spiral, BossMove.WallGap, BossMove.AimedBurst, BossMove.FanWide,
                        BossMove.RingDouble, BossMove.LaserSweep },
                new[] { BossMove.SpiralDual, BossMove.Converge, BossMove.Crossfire, BossMove.BulletRain,
                        BossMove.WallRotate, BossMove.FinalBurst },
            },
            // Weaver：弹墙/激光型
            new[]
            {
                new[] { BossMove.WallGap, BossMove.LaserSweep, BossMove.Aimed, BossMove.Fan },
                new[] { BossMove.WallRotate, BossMove.LaserCross, BossMove.Spiral, BossMove.AimedRing,
                        BossMove.FanWide, BossMove.Teleport },
                new[] { BossMove.Converge, BossMove.SpiralReverse, BossMove.Crossfire, BossMove.BulletRain,
                        BossMove.MineField, BossMove.FinalBurst },
            },
            // Helios：混合/布雷型
            new[]
            {
                new[] { BossMove.AimedRing, BossMove.Mines, BossMove.Ring, BossMove.Dash },
                new[] { BossMove.Teleport, BossMove.MineField, BossMove.SpiralDual, BossMove.Fan,
                        BossMove.AimedBurst, BossMove.WallGap },
                new[] { BossMove.Crossfire, BossMove.Converge, BossMove.LaserCross, BossMove.SpiralReverse,
                        BossMove.BulletRain, BossMove.FinalBurst },
            },
        };

        public static BossMove Pick(int bossIndex, int phase, int index)
        {
            bossIndex = Mathf.Clamp(bossIndex, 0, Sequences.Length - 1);
            phase = Mathf.Clamp(phase, 0, 2);
            var seq = Sequences[bossIndex][phase];
            return seq[index % seq.Length];
        }

        /// <summary>每台 Boss 各阶段的招式数（4 / 6 / 6）。</summary>
        public static int SequenceLength(int bossIndex, int phase) =>
            Sequences[Mathf.Clamp(bossIndex, 0, 2)][Mathf.Clamp(phase, 0, 2)].Length;

        /// <summary>三台 Boss 的基础血量（原版 entities.js:1730/1740/1750）。</summary>
        public static readonly float[] BaseHp = { 400f, 380f, 440f };

        /// <summary>原版按 (sector-1)%3 轮换三台 Boss，而不是按星区段固定。</summary>
        public static int BossForZone(int zone) => ((zone - 1) % 3 + 3) % 3;

        /// <summary>网页版 BOSS_TYPES 的名字：阿刻戎 / 薇尔 / 赫利俄斯。</summary>
        public static readonly string[] BossNameZh = { "阿刻戎", "薇尔", "赫利俄斯" };
        public static readonly string[] BossTitleZh = { "守门人", "织星者", "新星核心" };
        public static readonly string[] BossEn = { "GATEKEEPER", "STAR WEAVER", "NOVA CORE" };

        public static string NameZh(int bossIndex) => BossNameZh[Mathf.Clamp(bossIndex, 0, 2)];
        public static string TitleZh(int bossIndex) => BossTitleZh[Mathf.Clamp(bossIndex, 0, 2)];
        public static string EnName(int bossIndex) => BossEn[Mathf.Clamp(bossIndex, 0, 2)];

        /// <summary>弹幕强度档（每 5 个星区一档，最多 3 档）。</summary>
        public static int TierForZone(int zone) => Mathf.Min(3, (zone - 1) / 5);
    }
}
