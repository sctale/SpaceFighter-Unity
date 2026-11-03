using UnityEngine;

namespace AuroraRift
{
    public enum EnemyKind
    {
        Drifter, Wasp, Lancer, Orbiter, Splitter, Bulwark,
        Rock, Sniper, Minekeeper, Sidewinder, Carrier, Shardling
    }

    public enum MovePattern { Sine, Dive, Hover, Orbit, Rock, Strafe }
    public enum FireMode { None, Aimed, Fan, Ring, Spread, Heavy, Needle, Mines, Wavefire, Carrier }

    /// <summary>
    /// 原版 ENEMY_TYPES 的 C# 版。像素值按 GameConfig.Px 换算成世界单位。
    /// 注意 Radius 存的是「机体尺寸」（原版 def.size，美术与炮口都按它算）；
    /// 判定半径另乘 GameConfig.EnemyCollisionSizePx = 0.78（entities.js:1263）。
    /// </summary>
    public struct EnemyDef
    {
        public EnemyKind Kind;
        public string Label;
        public float Radius, Hp, Score, Speed, Amp, TargetY;
        public float Charge;            // 开火前的蓄力预告秒数（原版只有狙击/布雷有）
        public MovePattern Move;
        public FireMode Fire;
        public float FireCdMin, FireCdMax;
        public float DropChance;
        public Color Tint;
    }

    public static class EnemyDefs
    {
        const float Px = GameConfig.Px; // 1 原版像素 = 10/540 世界单位（网页版画布 W=540）

        public static readonly EnemyDef[] All =
        {
            new EnemyDef { Kind = EnemyKind.Drifter,   Label = "普通机群", Radius = 17*Px, Hp = 4,  Score = 100, Speed = 78*Px,  Amp = 44*Px, Move = MovePattern.Sine,   Fire = FireMode.Aimed,   FireCdMin = 1.6f, FireCdMax = 2.2f, DropChance = 0.10f, Tint = GameConfig.Hex("FF6F91") },
            new EnemyDef { Kind = EnemyKind.Wasp,      Label = "俯冲蜂",   Radius = 15*Px, Hp = 3,  Score = 150, Speed = 185*Px, Move = MovePattern.Dive,   Fire = FireMode.None,    FireCdMin = 0f,   FireCdMax = 0f,   DropChance = 0.06f, Tint = GameConfig.Hex("FF9AB4") },
            new EnemyDef { Kind = EnemyKind.Lancer,    Label = "扇形射手", Radius = 23*Px, Hp = 15, Score = 280, Speed = 55*Px,  TargetY = 0.23f, Move = MovePattern.Hover, Fire = FireMode.Fan,   FireCdMin = 1.6f, FireCdMax = 2.1f, DropChance = 0.22f, Tint = GameConfig.Hex("A597FF") },
            new EnemyDef { Kind = EnemyKind.Orbiter,   Label = "环状炮台", Radius = 21*Px, Hp = 19, Score = 340, Speed = 50*Px,  TargetY = 0.27f, Move = MovePattern.Orbit, Fire = FireMode.Ring,  FireCdMin = 2.0f, FireCdMax = 2.7f, DropChance = 0.26f, Tint = GameConfig.Hex("9B8CFF") },
            new EnemyDef { Kind = EnemyKind.Splitter,  Label = "分裂体",   Radius = 20*Px, Hp = 11, Score = 230, Speed = 64*Px,  Amp = 32*Px, Move = MovePattern.Sine,   Fire = FireMode.Spread,  FireCdMin = 1.8f, FireCdMax = 2.4f, DropChance = 0.20f, Tint = GameConfig.Hex("FF6F91") },
            new EnemyDef { Kind = EnemyKind.Bulwark,   Label = "重装堡垒", Radius = 30*Px, Hp = 46, Score = 520, Speed = 36*Px,  TargetY = 0.20f, Move = MovePattern.Hover, Fire = FireMode.Heavy, FireCdMin = 1.8f, FireCdMax = 2.5f, DropChance = 0.40f, Tint = GameConfig.Hex("C3CBE0") },
            new EnemyDef { Kind = EnemyKind.Rock,      Label = "陨石",     Radius = 23*Px, Hp = 14, Score = 70,  Speed = 48*Px,  Move = MovePattern.Rock,   Fire = FireMode.None,    FireCdMin = 0f,   FireCdMax = 0f,   DropChance = 0f,    Tint = GameConfig.Hex("AAB6CC") },
            new EnemyDef { Kind = EnemyKind.Sniper,    Label = "长炮狙击", Radius = 22*Px, Hp = 20, Score = 320, Speed = 58*Px,  TargetY = 0.17f, Move = MovePattern.Hover, Fire = FireMode.Needle, FireCdMin = 1.6f, FireCdMax = 2.1f, Charge = 0.55f, DropChance = 0.24f, Tint = GameConfig.Hex("69F5DC") },
            new EnemyDef { Kind = EnemyKind.Minekeeper,Label = "布雷者",   Radius = 24*Px, Hp = 28, Score = 380, Speed = 42*Px,  TargetY = 0.25f, Move = MovePattern.Hover, Fire = FireMode.Mines,  FireCdMin = 2.4f, FireCdMax = 3.1f, Charge = 0.3f, DropChance = 0.30f, Tint = GameConfig.Hex("FFD27A") },
            new EnemyDef { Kind = EnemyKind.Sidewinder,Label = "侧掠弯刀", Radius = 21*Px, Hp = 17, Score = 310, Speed = 135*Px, TargetY = 0.33f, Move = MovePattern.Strafe, Fire = FireMode.Wavefire, FireCdMin = 1.25f, FireCdMax = 1.75f, DropChance = 0.22f, Tint = GameConfig.Hex("9B8CFF") },
            new EnemyDef { Kind = EnemyKind.Carrier,   Label = "召唤母舰", Radius = 34*Px, Hp = 78, Score = 680, Speed = 32*Px,  TargetY = 0.13f, Move = MovePattern.Hover, Fire = FireMode.Carrier, FireCdMin = 2.0f, FireCdMax = 2.6f, DropChance = 0.55f, Tint = GameConfig.Hex("8FA4C9") },
            new EnemyDef { Kind = EnemyKind.Shardling, Label = "分裂幼体", Radius = 13*Px, Hp = 5,  Score = 90,  Speed = 215*Px, Move = MovePattern.Dive,   Fire = FireMode.None,    FireCdMin = 0f,   FireCdMax = 0f,   DropChance = 0.03f, Tint = GameConfig.Hex("FF9AB4") },
        };

        public static EnemyDef Get(EnemyKind k)
        {
            foreach (var d in All) if (d.Kind == k) return d;
            return All[0];
        }
    }

    /// <summary>原版 SF.DIFFICULTY 的三档系数。</summary>
    public struct Difficulty
    {
        public string Id, Name, Note;
        public float EnemyRate, BulletSpeed, BossBullet, BossHp, Nova, ScoreMul;
    }

    public static class Difficulties
    {
        public static readonly Difficulty Cruise = new Difficulty
        { Id = "cruise", Name = "巡航", Note = "弹幕放缓 · 星爆更勤 · 得分 ×0.8", EnemyRate = 1.22f, BulletSpeed = 0.9f, BossBullet = 0.86f, BossHp = 0.9f, Nova = 1.2f, ScoreMul = 0.8f };

        public static readonly Difficulty Standard = new Difficulty
        { Id = "standard", Name = "标准", Note = "设计基准 · 原汁原味", EnemyRate = 1f, BulletSpeed = 1f, BossBullet = 1f, BossHp = 1f, Nova = 1f, ScoreMul = 1f };

        public static readonly Difficulty Abyss = new Difficulty
        { Id = "abyss", Name = "深渊", Note = "弹幕更快 · Boss 更硬 · 得分 ×1.35", EnemyRate = 0.8f, BulletSpeed = 1.1f, BossBullet = 1.12f, BossHp = 1.18f, Nova = 0.85f, ScoreMul = 1.35f };

        public static Difficulty[] All => new[] { Cruise, Standard, Abyss };

        public static Difficulty ById(string id)
        {
            if (id == "cruise") return Cruise;
            if (id == "abyss") return Abyss;
            return Standard;
        }
    }
}
