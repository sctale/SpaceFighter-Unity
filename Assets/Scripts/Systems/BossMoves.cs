using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 原版 22 招（web/www/js/entities.js:1724-1778）。
    /// 之前这里是一套自己发明的名字（RingDouble / WallRotate / Converge / FinalBurst…），
    /// 和原版的招式表只对得上三四招，所以「背板」这件事完全对不上。
    /// </summary>
    public enum BossMove
    {
        Fan, Spiral, Ring, Homing, Sweep, Cross, Summon, Barrage, TwinBeam, MineCurtain,
        Blink, LaserWeb, WaveCurtain, WallGap, Pulsar, Charge, CrossSweep, Cascade,
        HomingBarrage, SpiralDouble, MineRing, NovaBurst
    }

    /// <summary>
    /// 招式表：名称 / 时长 / 预告色族 / 是否移动机体 / 三台 Boss 的阶段序列。
    /// 全部照抄原版，不做"改进"——数值门禁会逐条比对。
    /// </summary>
    public static class BossMoves
    {
        /// <summary>原版 TELL_TIME：每一招都是 0.5 秒预告，没有例外（entities.js:1781）。</summary>
        public const float TelegraphTime = 0.5f;

        static readonly string[] Labels =
        {
            "扇形齐射", "星旋回廊", "环状爆发", "追踪星群", "双生扫射", "十字光牢",
            "母舰召唤", "重弹压制", "双子光矛", "雷区播种", "相位闪现", "星网绞杀",
            "波动弹幕", "裂隙光幕", "星脉脉冲", "贯穿冲撞", "光牢旋扫", "陨星暴雨",
            "猎群连射", "逆向双旋", "环形雷阵", "新星爆发",
        };

        static readonly float[] Durations =
        {
            1.3f, 2.0f, 1.05f, 1.0f, 2.5f, 2.0f, 1.15f, 1.9f, 2.2f, 2.0f,
            2.2f, 2.4f, 2.0f, 2.8f, 2.2f, 3.2f, 2.9f, 3.1f, 2.7f, 2.4f,
            1.9f, 2.6f,
        };

        // 预告色族（entities.js:1782-1786）：rose=光束 / violet=位移 / gold=投送 / 其余 teal
        static readonly BossMove[] BeamMoves = { BossMove.Sweep, BossMove.Cross, BossMove.TwinBeam,
                                                 BossMove.LaserWeb, BossMove.CrossSweep };
        static readonly BossMove[] MoveMoves = { BossMove.Blink, BossMove.Charge };
        static readonly BossMove[] OrdnanceMoves = { BossMove.Summon, BossMove.MineCurtain, BossMove.MineRing };

        public static string Name(BossMove m) => Labels[(int)m];
        public static float Duration(BossMove m) => Durations[(int)m];

        /// <summary>预告期的圆环与火花颜色，玩家靠这个颜色就能预判招式类型。</summary>
        public static Color Tell(BossMove m)
        {
            if (System.Array.IndexOf(BeamMoves, m) >= 0) return GameConfig.Rose;
            if (System.Array.IndexOf(MoveMoves, m) >= 0) return GameConfig.Violet;
            if (System.Array.IndexOf(OrdnanceMoves, m) >= 0) return GameConfig.Gold;
            return GameConfig.Teal;
        }

        /// <summary>MOVING_ATTACKS：这两招期间机体不悬停，由招式自己控制位置。</summary>
        public static bool LocksMove(BossMove m) => System.Array.IndexOf(MoveMoves, m) >= 0;

        // 三台 Boss 各 3 套固定序列（entities.js:1724-1726），按顺序循环，可背板。
        static readonly BossMove[][][] Sequences =
        {
            // 守门人 gatekeeper
            new[]
            {
                new[] { BossMove.Fan, BossMove.WallGap, BossMove.TwinBeam, BossMove.MineCurtain },
                new[] { BossMove.Spiral, BossMove.WallGap, BossMove.Pulsar, BossMove.Fan,
                        BossMove.TwinBeam, BossMove.Sweep },
                new[] { BossMove.Charge, BossMove.CrossSweep, BossMove.WallGap, BossMove.Cascade,
                        BossMove.Ring, BossMove.Spiral },
            },
            // 织星者 weaver
            new[]
            {
                new[] { BossMove.Ring, BossMove.Homing, BossMove.HomingBarrage, BossMove.Cross,
                        BossMove.WaveCurtain },
                new[] { BossMove.SpiralDouble, BossMove.Blink, BossMove.HomingBarrage, BossMove.Summon,
                        BossMove.Cross },
                new[] { BossMove.CrossSweep, BossMove.LaserWeb, BossMove.SpiralDouble,
                        BossMove.HomingBarrage, BossMove.Ring, BossMove.WaveCurtain },
            },
            // 新星核心 helios
            new[]
            {
                new[] { BossMove.Spiral, BossMove.Barrage, BossMove.LaserWeb, BossMove.MineCurtain },
                new[] { BossMove.NovaBurst, BossMove.Summon, BossMove.TwinBeam, BossMove.Spiral,
                        BossMove.Barrage },
                new[] { BossMove.NovaBurst, BossMove.CrossSweep, BossMove.Cascade, BossMove.LaserWeb,
                        BossMove.MineRing, BossMove.Barrage },
            },
        };

        public static BossMove Pick(int bossIndex, int phase, int index)
        {
            bossIndex = Mathf.Clamp(bossIndex, 0, Sequences.Length - 1);
            phase = Mathf.Clamp(phase, 0, 2);
            var seq = Sequences[bossIndex][phase];
            return seq[index % seq.Length];
        }

        /// <summary>每台 Boss 各阶段的招式数（4/6/6、5/5/6、4/5/6）。</summary>
        public static int SequenceLength(int bossIndex, int phase) =>
            Sequences[Mathf.Clamp(bossIndex, 0, 2)][Mathf.Clamp(phase, 0, 2)].Length;

        /// <summary>三台 Boss 的基础血量（原版 entities.js:1730/1740/1750）。</summary>
        public static readonly float[] BaseHp = { 400f, 380f, 440f };

        /// <summary>机体视觉尺寸（原版 def.size，画布像素）：76 / 80 / 82。</summary>
        public static readonly float[] SizePx = { 76f, 80f, 82f };

        /// <summary>原版按 (sector-1)%3 轮换三台 Boss，而不是按星区段固定。</summary>
        public static int BossForZone(int zone) => ((zone - 1) % 3 + 3) % 3;

        /// <summary>弹幕强度档（每 5 个星区一档，最多 3 档）。</summary>
        public static int TierForZone(int zone) => Mathf.Min(3, (zone - 1) / 5);

        // 网页版 BOSS_TYPES 的名字：阿刻戎 / 薇尔 / 赫利俄斯
        public static readonly string[] BossNameZh = { "阿刻戎", "薇尔", "赫利俄斯" };
        public static readonly string[] BossTitleZh = { "守门人", "织星者", "新星核心" };
        public static readonly string[] BossEn = { "GATEKEEPER", "STAR WEAVER", "NOVA CORE" };

        public static string NameZh(int bossIndex) => BossNameZh[Mathf.Clamp(bossIndex, 0, 2)];
        public static string TitleZh(int bossIndex) => BossTitleZh[Mathf.Clamp(bossIndex, 0, 2)];
        public static string EnName(int bossIndex) => BossEn[Mathf.Clamp(bossIndex, 0, 2)];
    }
}
