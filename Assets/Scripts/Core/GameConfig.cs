using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 数值与配色的单一真相源。所有数字直接来自网页版
    /// `D:/V-Coding/space-fighter/www/js/`（v2.8.4），改这里之前先回原版核对。
    ///
    /// 口径：网页版画布固定 W=540、H 随设备比例变化（game.js:117-127）。
    /// 世界宽度定为 10 单位，所以 **1 原版像素 = 10/540 = 0.0185185 世界单位**。
    /// 之前按 0.025（400px≈10 单位）换算，等于把整机放大 1.35 倍 —— 机体、弹体、
    /// 速度、判定圈全部偏大，屏幕上显得又挤又快，是真机"画面别扭"的根因。
    /// </summary>
    public static class GameConfig
    {
        // ---- 世界口径 ----
        public const float WorldWidth = 10f;
        public const int RefW = 540;            // 网页版画布宽（固定）
        public const int RefH = 960;            // 9:16 时的画布高
        public const float Px = WorldWidth / RefW;          // 0.0185185f
        public const float PlayHalfWidth = WorldWidth * 0.5f;
        /// <summary>9:16 时的相机半高 = 960px × Px ÷ 2。高屏（20:9）会更大，用 GameView 取实际值。</summary>
        public const float OrthoSizeMin = RefH * Px * 0.5f;   // 8.8889f

        /// <summary>把"距画布顶部的像素"换成世界 Y（画布 y 向下，世界 y 向上）。</summary>
        public static float FromTopPx(float px, float halfH) => halfH - px * Px;
        /// <summary>把"画布高度的比例（从顶部算）"换成世界 Y。</summary>
        public static float FromTopFrac(float f, float halfH) => halfH * (1f - 2f * f);

        // ---- 玩家（entities.js:704,753-754,415-416）----
        public const float PlayerSpeedPx = 440f;
        public const float PlayerFocusSpeedPx = 185f;
        public static readonly float PlayerSpeed = PlayerSpeedPx * Px;          // 8.148
        public static readonly float PlayerFocusSpeed = PlayerFocusSpeedPx * Px; // 3.426
        public static readonly float PlayerFocusSpeedMul = PlayerFocusSpeedPx / PlayerSpeedPx;
        public static readonly float PlayerHitRadius = 4.6f * Px;      // 0.0852
        public static readonly float PlayerGrazeRadius = 28f * Px;     // 0.5185
        public const int MaxShields = 3;
        public const int StartLives = 3;
        // 开局配置（网页版 entities.js:375-385 的 Player 构造）：护盾 1 层、僚机 0 架、
        // 脉冲线 2 级且只有它解锁。僚机只能从补给箱拿——白送两架会把整条掉落经济打废。
        public const int StartShields = 1;
        public const int StartDrones = 0;
        public const int StartPulseLevel = 2;
        public const int StartUnlockedLines = 1;
        // 活动区：x∈[31, W-31]、y∈[H*0.18, H-104]
        public const float BoundSideInsetPx = 31f;
        public const float BoundTopFrac = 0.18f;
        public const float BoundBottomInsetPx = 104f;
        public const float RespawnInsetPx = 120f;   // 掉命后回到 (W/2, H-120)

        // ---- 触屏驾驶（entities.js:721-734）----
        public const float DragFollowRate = 30f;    // k = 1-exp(-30dt)
        public const float MouseFollowRate = 26f;
        public const float DragMaxLateralPx = 162f; // ±W*0.3，横向抓取偏移上限
        public const float DragLiftPx = 140f;       // 机体最多抬到指尖上方 140px，避免被拇指挡住
        public const float DragDropPx = 60f;        // 也允许落到指尖下方 60px

        // ---- 冲刺与相位（entities.js:641-662,742；game.js:873-891）----
        public const float DashTime = 0.17f;
        public const float DashCooldown = 1.7f;
        public const float DashSpeedPx = 1250f;
        public static readonly float DashSpeed = DashSpeedPx * Px;      // 23.15
        public const float DashInvuln = 0.36f;
        public const float PhaseRadiusPx = 34f;
        public static readonly float PhaseRadius = PhaseRadiusPx * Px;
        public const float PhaseNovaPerBullet = 0.55f;
        public const int PhaseScorePerBullet = 6;

        // ---- 无敌帧分级（entities.js:388,668；game.js:1198,1216,1244,1354）----
        public const float InvulnOnSpawn = 1.6f;
        public const float InvulnAfterHullHit = 2.8f;
        public const float InvulnAfterShield = 1.3f;
        public const float InvulnAfterReflect = 1.5f;
        public const float InvulnAfterNova = 0.9f;
        public const float InvulnOnZoneChange = 1.5f;
        public const float InvulnOnBossPhase = 0.75f;
        public const float SpawnGrace = 1.2f;

        // ---- 星爆（entities.js:694-696,664-671；game.js:846,860,906,1002,1095,1242,1476-1508）----
        public const float NovaRegenPerSecond = 1.8f;
        public const float NovaOnKill = 2.5f;
        public const float NovaOnEliteKill = 7f;
        public const float NovaOnGraze = 0.9f;
        public const float NovaOnHitEnemy = 0.06f;
        public const float NovaOnHitBoss = 0.12f;
        public const float NovaOnGem = 0.5f;
        public const float NovaOnPointDefense = 0.6f;
        public const float NovaOnHullHit = 25f;      // raw，不受失谐影响
        public const float NovaOnShieldBreak = 8f;   // raw
        public const float NovaOnReflectSave = 4f;   // raw
        public const float DetunedNovaMul = 0.45f;
        public const float OverdriveTime = 7f;
        public const float OverdriveMax = 9.5f;
        public const float OverdriveKillExtend = 0.11f;
        public const float OverdriveEliteKillExtend = 0.26f;
        public const float OverdriveDamageMul = 1.8f;
        public const float OverdriveFireRateMul = 0.74f;   // 间隔乘子
        public const float NovaEnemyDamage = 32f;
        public const float NovaBossDamageFrac = 0.06f;
        public const float NovaBossDamageFlat = 30f;
        public const int NovaScorePerClearedBullet = 10;
        public const int ClearScorePerBullet = 4;

        // ---- 武器线（entities.js:336-349,354-362,863-876,910-931）----
        public const int ResonanceMaxStacks = 5;
        public const float ResonanceWindow = 7f;
        public const float ResonancePerStackDamage = 0.08f;
        public const float ResonancePerStackCharge = 0.06f;
        public const int DetuneRearmCharge = 100;     // 手上这把要打满才重新就绪
        public const int DetuneClearCharge = 55;      // 收纳中的这把回到 55 即恢复
        public const float SwapResonanceGap = 1.2f;   // 换线前至少停这么久才叠共鸣
        public const float VentRatio = 2.4f;          // 排气速度 = drain × 2.4
        public const float DetunedDamageMul = 0.55f;
        public const float DetunedIntervalMul = 1.45f; // 失谐是"更慢"，不是更快
        public const float SoloDrainMul = 0.55f;      // 只解锁一条线时蓄能掉得更慢
        public const float HitChargePenalty = 45f;    // 挨一发掉 45 蓄能

        public struct LineDef
        {
            public string Id, Label;
            public float SpeedPx, Drain, Recover;
            public Color Tint, Deep;
            public float[] FireRate;      // 5 级射速间隔（秒）
            public LineDef(string id, string label, float speedPx, float drain, float recover,
                           Color tint, Color deep, float[] fireRate)
            {
                Id = id; Label = label; SpeedPx = speedPx; Drain = drain; Recover = recover;
                Tint = tint; Deep = deep; FireRate = fireRate;
            }
            public float Speed => SpeedPx * Px;
        }

        public static readonly Color Teal = Hex("35E0C8");
        public static readonly Color TealDeep = Hex("0F766E");
        public static readonly Color Rose = Hex("FF5C8A");
        public static readonly Color RoseDeep = Hex("9F1239");
        public static readonly Color Gold = Hex("FFC24B");
        public static readonly Color GoldDeep = Hex("92600A");
        public static readonly Color Violet = Hex("9B8CFF");     // 原版 P.violet
        public static readonly Color VioletDeep = Hex("5B4ECF");

        public static readonly LineDef Pulse = new LineDef("pulse", "脉冲", 1250f, 7.4f, 15.5f, Teal, TealDeep,
            new[] { 0.120f, 0.108f, 0.098f, 0.090f, 0.082f });
        public static readonly LineDef Prism = new LineDef("prism", "棱镜", 560f, 10.5f, 12f, Rose, RoseDeep,
            new[] { 0.360f, 0.330f, 0.305f, 0.280f, 0.245f });
        public static readonly LineDef Swarm = new LineDef("swarm", "蜂群", 830f, 8.6f, 13.5f, Gold, GoldDeep,
            new[] { 0.250f, 0.210f, 0.180f, 0.160f, 0.145f });

        public static LineDef Line(string id)
        {
            if (id == "prism") return Prism;
            if (id == "swarm") return Swarm;
            return Pulse;
        }

        /// <summary>
        /// 武器名按等级换叫法（原版 ui.js:8-12）：满级那一声「星瀑 / 终焉棱镜 / 蜂后」
        /// 是玩家一路升级的奖励感，只写「脉冲 LV5」是把这层反馈抹平了。
        /// </summary>
        public static readonly string[][] LineAliases =
        {
            new[] { "脉冲炮", "双联脉冲", "三叉星流", "四象阵列", "星瀑" },
            new[] { "棱镜", "双棱", "棱镜矩阵", "裂光", "终焉棱镜" },
            new[] { "蜂群", "双子蜂", "三蜂", "五蜂", "蜂后" },
        };

        public static string WeaponName(string id, int level)
        {
            int row = id == "prism" ? 1 : id == "swarm" ? 2 : 0;
            return LineAliases[row][Mathf.Clamp(level, 1, 5) - 1];
        }

        // 棱镜：单发威力 / 贯穿数 / 对巨构加成（entities.js:910-921）
        public static readonly float[] PrismDamage = { 4, 4, 5, 6, 6 };
        public static readonly int[] PrismPierce = { 1, 1, 2, 2, 3 };
        public const float PrismBossBonus = 1.18f;
        // 蜂群：弹数 / 单发威力（entities.js:923-931）
        public static readonly int[] SwarmCount = { 2, 3, 4, 4, 4 };
        public static readonly float[] SwarmDamage = { 2, 2, 2, 3, 3 };
        public const float SwarmHoming = 1.45f;
        public const float SwarmTurnRate = 4.6f;
        public const float SwarmLockDelay = 0.11f;
        public const float FocusPatternCompress = 0.42f;   // 精准模式收束弹道
        public const float FocusLateralMul = 0.2f;

        // ---- 弹幕（game.js:763-779,1289）----
        public const float BulletLifetime = 4f;
        public const int MaxEnemiesAlive = 42;
        public const int MaxEnemyBullets = 420;
        public const float MineDrag = 3.8f;          // 水雷速度按 exp(-3.8×dt) 衰减到停住
        public const float MineBurstSpeedPx = 128f;  // 破片初速 = 128 + 星区×2
        public const float WavePhaseRate = 7.2f;     // 波动弹的相位频率
        public const float PlasmaBurstSpeedPx = 105f;

        // ---- 敌机（entities.js:1210-1233,1265-1268,1481-1493）----
        public const float EnemyHpPerSector = 0.16f;       // hp ×(1+(s-1)*0.16)
        public const float EnemySpeedPerTier = 0.14f;      // 密度档位提速
        public const float EnemyFirePerTier = 0.10f;       // 密度档位缩间隔
        public const float EnemyFireIntervalFloor = 0.7f;
        public const float AffixBaseChance = 0.16f;
        public const float AffixMaxExtra = 0.14f;
        public const float EliteSizeMul = 1.22f;
        public const float EliteHpMul = 2.4f;
        public const float EliteScoreMul = 3.2f;
        public const float EliteFireMul = 0.82f;
        public const float ArmoredHpMul = 1.45f;
        public const float ArmoredDamageTakenMul = 0.72f;
        public const float RapidSizeMul = 0.92f;
        public const float RapidFireMul = 0.72f;
        public const float VolatileHpMul = 0.9f;
        public const float VolatileFuse = 0.5f;
        public const int VolatileSplit = 6;
        // 出场公平性：age > entryDelay(0.35)+0.18 且 y∈(-10, H*0.94)
        public const float EntryDelay = 0.35f;
        public const float EntryGrace = 0.18f;
        public const float EntryTopInsetPx = 10f;
        public const float EntryBottomFrac = 0.94f;

        // ---- 敌机判定与运动（entities.js:1263-1341）----
        public const float EnemyCollisionSizePx = 0.78f;   // 判定半径 = size × 0.78，美术仍按 size
        public const float EnemyChargeEliteMul = 0.8f;     // 精英蓄力更短
        public const float ArmoredSizeMul = 1.06f;
        public const float ArmoredFireMul = 1.12f;         // 装甲机射得更「慢」
        public const float EnemyLifeSeconds = 16f;         // 悬停/环绕机 16 秒后离场
        public const float HoverLeaveSpeedPx = 170f;
        public const float OrbitLeaveSpeedPx = 160f;
        public const float StrafeDropSpeedPx = 120f;
        public const float DiveSlowSpeedPx = 40f;          // 宽限期内的慢速下落
        public const float DiveSpeedMul = 1.5f;            // 锁定玩家后速度 ×1.5
        public const float HoverSpanPx = 88f;              // 驻留横向摆幅
        public const float HoverBulwarkSpanPx = 55f;
        public const float HoverSecondAmpPx = 20f;         // 叠一层慢速 sin(t*0.23)
        public const float HoverBobPx = 8f;
        public const float ApproachWobblePx = 12f;         // 进场时 x 微摆
        public const float OrbitRadiusPx = 62f;
        public const float OrbitRadiusYPx = 24f;
        public const float OrbitRate = 1.15f;
        public const float StrafeBobPx = 16f;
        public const float RockDriftPx = 12f;
        public const float CarrierSummonEvery = 3.6f;
        public const float CarrierShardlingChance = 0.55f;  // 其余是俯冲蜂
        public const float CarrierBandTopFrac = 0.45f;      // 只在上半屏 45% 内召唤
        public const float CarrierChildEntryDelay = 0.15f;
        public const float SplitterChildHpFrac = 0.5f;      // 分裂幼体只有半血
        // 导演独有增强（原版剧本不随机出精英）：想回到纯原版把这里改成 0
        public const float RandomEliteChance = 0.18f;

        // ---- 连锁与计分（game.js:74-76,977-979,998-1021）----
        public const float ComboWindow = 2.8f;
        public const int ComboMaxMult = 8;
        public const int ComboStep = 5;

        // ---- 打击停顿与慢镜（game.js:1015,1209,1257,1343,1382,1484）----
        // 原版每次关键命中都会「钉」住世界几十毫秒，受创与星爆还会拖一段慢镜。
        // 这一层不做，游戏摸起来就是「塑料」——数值对了但打不动人。
        public const float FreezeKill = 0.025f;        // 精英/堡垒被击落
        public const float FreezeHit = 0.05f;          // 护盾破、力场挡下致命一击、Boss 破防
        public const float FreezePhase = 0.06f;        // Boss 换阶段
        public const float FreezeDesperate = 0.08f;    // Boss 进临界
        public const float FreezeNova = 0.1f;          // 星爆
        public const float FreezeBossDie = 0.12f;      // Boss 崩解
        public const float SlowHitDur = 0.3f, SlowHitScale = 0.5f;
        public const float SlowNovaDur = 0.4f, SlowNovaScale = 0.45f;
        public const float SlowDeathDur = 1.2f, SlowDeathScale = 0.28f;
        public const float SlowBossDieDur = 1.4f, SlowBossDieScale = 0.35f;

        // ---- Boss（entities.js:1727-1793,1832,1880-1910,1940-1945,1975,2029-2040）----
        public const float BossHpSectorGrowth = 0.70f;
        public const float BossHpGrowthCap = 3.4f;
        public const float BossPhaseHigh = 0.66f;
        public const float BossPhaseLow = 0.30f;
        public const float BossCriticalFrac = 0.12f;
        public const float BossMercyWindow = 1.15f;
        public const float BossBreakFloor = 90f;        // 原版 60；Unity 提高，避免每 4 秒一次破防
        public const float BossBreakFracOfMaxHp = 0.30f;  // 原版 0.18；同上（见 docs/PARITY.md）
        public const float BossBreakStun = 1.2f;
        public const float BossBreakDamageMul = 1.35f;
        public const float BossTelegraph = 0.5f;
        public const float BossIdleFirePhase1 = 0.95f;
        public const float BossIdleFirePhase2 = 0.66f;
        public const float BossBulletSpeedBase = 152f;
        public const float BossBulletSpeedPerTier = 46f;
        public static readonly float[] BossBulletSpeedDiv = { 1f, 1.06f, 1.18f };

        // ---- Boss 出招节奏（entities.js:1945-1967,2444-2448）----
        public static readonly float[] BossSpellCd = { 0.85f, 0.62f, 0.42f };  // 一/二/三阶段间隔
        public const float BossSpellCdFloor = 0.3f;
        public const float BossChainCd = 0.16f;              // 三阶段链招几乎不停
        public const int BossChainEveryTier0 = 4;            // 一档密度每 4 招连锁一次
        public const int BossChainEveryOtherTier = 3;
        public const float BossInitialCd = 1.15f;
        public const float BossPostBreakCd = 0.55f;          // 破防结束立刻补一招
        public const float BossDesperateCd = 0.45f;          // 临界起手间隔
        public const float BossDesperateFrac = 0.12f;        // 血量低于 12% 进临界
        public const float BossDesperateRingNormal = 0.45f;  // 三阶段收招后追加免费环的概率
        public const float BossDesperateRingOtherTier = 0.65f;
        public const float BossDesperateRingHard = 0.7f;     // 临界时
        public const float BossDesperateRingHardest = 0.9f;
        public const int BossFreeRingNormal = 16;
        public const int BossFreeRingDesperate = 22;
        public const float BossFreeRingSpeedMul = 0.75f;
        public const float BossContactRx = 0.85f;            // 接触椭圆 = size×0.85 + 8
        public const float BossContactRy = 0.70f;            // size×0.70 + 8
        public const float BossContactPadPx = 8f;
        public const float BossHitRx = 1.02f;                // 玩家弹判定椭圆 = size×1.02 / ×0.82
        public const float BossHitRy = 0.82f;
        public const float BossWarnSeconds = 3.0f;           // game.js:1297 预警 3.0 秒并清场
        public const float SectorClearSeconds = 4.4f;        // game.js:1416 通关幕间

        // ---- 击破结算（game.js:1395-1400）----
        public const float ClearTimeBonusBase = 90f;
        public const float ClearTimeBonusPerSecond = 40f;
        public const float ClearNoHitBase = 1200f;
        public const float ClearNoHitPerSector = 200f;
        public const float ClearGrazePerHit = 20f;
        public const float ClearBaseScore = 4000f;
        public const float ClearBasePerSector = 900f;
        // Unity 独有增强：每星区通关额外补偿 500×(区号+1)，现在显式列进结算面板
        public const float SectorCompensationPerZone = 500f;

        // ---- 评级（ui.js:555-557）----
        public const int GradeS = 120000, GradeA = 60000, GradeB = 22000;
        public const int GradeSectorS = 8, GradeSectorA = 5, GradeSectorB = 3;

        // ---- 僚机（entities.js:612-633）----
        public const int MaxDrones = 3;
        public static readonly string[] DroneKinds = { "gunner", "lance", "aegis", "swarm" };
        // 挂点（原版像素，y 向下）：1 架 (-64,8)；2 架 (±64,8)；3 架 (-68,2)/(68,2)/(0,48)
        public static readonly Vector2[] DroneSlots2A = { new Vector2(-64, 8), new Vector2(64, 8) };
        public static readonly Vector2[] DroneSlots3 = { new Vector2(-68, 2), new Vector2(68, 2), new Vector2(0, 48) };
        /// <summary>
        /// 僚机种类挑选：优先补一个当前没有的种类（原版 pickDrone 的 unique 偏好），
        /// 三个挂点因此能凑成三种不同僚机；都有了才随机。纯函数，门禁直接断言。
        /// </summary>
        public static string PickDroneKind(System.Collections.Generic.HashSet<string> inUse, int roll)
        {
            foreach (var k in DroneKinds)
                if (inUse == null || !inUse.Contains(k)) return k;
            return DroneKinds[Mathf.Abs(roll) % DroneKinds.Length];
        }

        public const float AegisPdInterval = 0.5f;   // 原版 pdT = 0.5（game.js:794），环视觉与点防同拍
        public const float AegisPdRadiusPx = 38f;
        public static readonly float AegisPdRadius = AegisPdRadiusPx * Px;
        public const int AegisPdClearCount = 4;

        // ---- 补给箱（game.js:1061-1086；director.js:49,230-261,287-303）----
        public const float SupplyInterval = 20f;
        public const float SupplyIntervalMin = 19f;
        public const float SupplyMidProgress = 0.55f;
        public const int PityThreshold = 3;
        public const float SwapIdleForSwapCrate = 10f;
        public const float SwapCrateChance = 0.62f;
        public const float ReflectDuration = 8f;

        /// <summary>三台 Boss 的基础血量。</summary>
        public static float BossBaseHp(int bossIndex) =>
            BossMoves.BaseHp[Mathf.Clamp(bossIndex, 0, 2)];

        /// <summary>Boss 血量随星区：×(1 + min(3.4, (s-1)×0.70))。</summary>
        public static float BossHpScale(int zone) =>
            1f + Mathf.Min(BossHpGrowthCap, Mathf.Max(0, zone - 1) * BossHpSectorGrowth);

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
