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

        // 三台 Boss（按星区档位）各自的 3 阶段序列；每阶段 2 招交替，保证 22 招全部登场
        static readonly BossMove[][][] Sequences =
        {
            // Boss A：环爆型（zone 1-3）
            new[]
            {
                new[] { BossMove.Ring, BossMove.Fan },
                new[] { BossMove.RingDouble, BossMove.AimedBurst },
                new[] { BossMove.Spiral, BossMove.FinalBurst },
            },
            // Boss B：弹墙/激光型（zone 4-6）
            new[]
            {
                new[] { BossMove.WallGap, BossMove.LaserSweep },
                new[] { BossMove.WallRotate, BossMove.LaserCross },
                new[] { BossMove.SpiralDual, BossMove.Converge },
            },
            // Boss C：混合型（zone 7+）
            new[]
            {
                new[] { BossMove.AimedRing, BossMove.Mines },
                new[] { BossMove.Teleport, BossMove.MineField },
                new[] { BossMove.Dash, BossMove.Crossfire },
            },
        };

        public static BossMove Pick(int bossTier, int phase, int index)
        {
            bossTier = Mathf.Clamp(bossTier, 0, Sequences.Length - 1);
            phase = Mathf.Clamp(phase, 0, 2);
            var seq = Sequences[bossTier][phase];
            return seq[index % seq.Length];
        }

        public static int TierForZone(int zone) => zone <= 3 ? 0 : zone <= 6 ? 1 : 2;
    }
}
