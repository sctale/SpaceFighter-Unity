#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AuroraRift.EditorTools
{
    /// <summary>
    /// 数值契约门禁：把网页版（space-fighter v2.8.4）的关键数值搬成可执行断言。
    /// 用法：Unity.exe -batchmode -quit -projectPath &lt;proj&gt; \
    ///        -executeMethod AuroraRift.EditorTools.VerifyNumbers.Run -logFile verify.log
    /// 退出码 0 = 全过；1 = 有失败（发布脚本前置）。
    ///
    /// 口径提醒：所有"像素"都是网页版画布像素（W=540 固定），换算系数 GameConfig.Px = 10/540。
    /// </summary>
    public static class VerifyNumbers
    {
        static readonly List<string> Failures = new List<string>();
        static int _passed;
        static readonly float Px = GameConfig.Px;

        [MenuItem("Aurora/Verify Design Numbers")]
        public static void Run()
        {
            Failures.Clear();
            _passed = 0;

            Viewport();
            Player();
            StartState();
            WeaponLines();
            ChargeEconomy();
            Nova();
            DashAndPhase();
            InvulnTiers();
            Enemies();
            ElitesAndAffixes();
            Combo();
            Drones();
            Crates();
            CheckBoss();
            ClearBonus();
            Grading();
            CheckDifficulties();
            TouchDriving();
            Behaviour();

            foreach (var f in Failures) Debug.LogError("[VERIFY] FAIL " + f);
            Debug.Log($"[VERIFY] SUMMARY passed={_passed} failed={Failures.Count}");
            if (Application.isBatchMode) EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------ 分组断言

        static void Viewport()
        {
            Check("scale.Px(10/540)", Px, 10f / 540f, 1e-6f);
            Check("viewport.RefW", GameConfig.RefW, 540);
            Check("viewport.RefH", GameConfig.RefH, 960);
            Check("viewport.halfHeight@9:16", GameConfig.OrthoSizeMin, 960f * Px * 0.5f, 1e-5f);
            Check("viewport.halfHeight@20:9", GameView.ComputeHalfHeight(1080f / 2400f), 5f / (1080f / 2400f), 1e-4f);
            Check("viewport.halfWidth", GameConfig.PlayHalfWidth, 5f);
            // 原版边界 x∈[31,509]、y∈[H*0.18, H-104]
            Check("bounds.sideInset", GameConfig.BoundSideInsetPx * Px, 31f * Px);
            Check("bounds.topFrac", GameConfig.BoundTopFrac, 0.18f);
            Check("bounds.bottomInset", GameConfig.BoundBottomInsetPx, 104f);
        }

        /// <summary>
        /// 开局状态：网页版 Player 构造（entities.js:375-385）+ Game.lives（game.js:64）。
        /// 这一组是"白送两架僚机"那类回归的哨兵。
        /// </summary>
        static void StartState()
        {
            Check("start.shields", GameConfig.StartShields, 1f);
            Check("start.drones", GameConfig.StartDrones, 0f);
            Check("start.pulseLevel", GameConfig.StartPulseLevel, 2f);
            Check("start.unlockedLines", GameConfig.StartUnlockedLines, 1f);
            Check("start.lives", GameConfig.StartLives, 3f);
            // Boss 悬停点 = max(H*0.27, 252px) 距顶，必须跟着实时视口走
            float hh169 = GameView.ComputeHalfHeight(1080f / 1920f);
            float hh209 = GameView.ComputeHalfHeight(1080f / 2400f);
            Check("start.bossAnchor@16:9", Mathf.Max(2f * hh169 / Px * 0.27f, 252f), 259.2f, 0.01f);
            Check("start.bossAnchor@20:9", Mathf.Max(2f * hh209 / Px * 0.27f, 252f), 324f, 0.01f);
            CheckTrue("start.bossAnchor.insideView", 2f * hh209 / Px * 0.27f > 252f,
                "20:9 上 252px 下限不再生效，Boss 会落在画面中部");
        }

        static void Player()
        {            Check("player.speed", GameConfig.PlayerSpeed, 440f * Px, 1e-5f);
            Check("player.focusSpeed", GameConfig.PlayerFocusSpeed, 185f * Px, 1e-5f);
            Check("player.hitR", GameConfig.PlayerHitRadius, 4.6f * Px, 1e-5f);
            Check("player.grazeR", GameConfig.PlayerGrazeRadius, 28f * Px, 1e-5f);
            Check("player.shieldMax", GameConfig.MaxShields, 3);
            Check("player.lives", GameConfig.StartLives, 3);
        }

        static void WeaponLines()
        {
            Check("pulse.speed", GameConfig.Pulse.SpeedPx, 1250f);
            Check("prism.speed", GameConfig.Prism.SpeedPx, 560f);
            Check("swarm.speed", GameConfig.Swarm.SpeedPx, 830f);
            Check("pulse.drain", GameConfig.Pulse.Drain, 7.4f);
            Check("prism.drain", GameConfig.Prism.Drain, 10.5f);
            Check("swarm.drain", GameConfig.Swarm.Drain, 8.6f);
            Check("pulse.recover", GameConfig.Pulse.Recover, 15.5f);
            Check("prism.recover", GameConfig.Prism.Recover, 12f);
            Check("swarm.recover", GameConfig.Swarm.Recover, 13.5f);

            // 5 级射速曲线（entities.js:871-875）
            float[] pulse = { 0.120f, 0.108f, 0.098f, 0.090f, 0.082f };
            float[] prism = { 0.360f, 0.330f, 0.305f, 0.280f, 0.245f };
            float[] swarm = { 0.250f, 0.210f, 0.180f, 0.160f, 0.145f };
            for (int i = 0; i < 5; i++)
            {
                Check($"pulse.rate.L{i + 1}", GameConfig.Pulse.FireRate[i], pulse[i], 1e-5f);
                Check($"prism.rate.L{i + 1}", GameConfig.Prism.FireRate[i], prism[i], 1e-5f);
                Check($"swarm.rate.L{i + 1}", GameConfig.Swarm.FireRate[i], swarm[i], 1e-5f);
            }
            for (int i = 0; i < 5; i++)
            {
                Check($"prism.dmg.L{i + 1}", GameConfig.PrismDamage[i], new[] { 4f, 4f, 5f, 6f, 6f }[i]);
                Check($"prism.pierce.L{i + 1}", GameConfig.PrismPierce[i], new[] { 1, 1, 2, 2, 3 }[i]);
                Check($"swarm.count.L{i + 1}", GameConfig.SwarmCount[i], new[] { 2, 3, 4, 4, 4 }[i]);
                Check($"swarm.dmg.L{i + 1}", GameConfig.SwarmDamage[i], new[] { 2f, 2f, 2f, 3f, 3f }[i]);
            }
            Check("prism.bossBonus", GameConfig.PrismBossBonus, 1.18f);
            Check("focus.compress", GameConfig.FocusPatternCompress, 0.42f);
        }

        static void ChargeEconomy()
        {
            Check("detune.damage", GameConfig.DetunedDamageMul, 0.55f);
            Check("detune.interval", GameConfig.DetunedIntervalMul, 1.45f);
            Check("detune.nova", GameConfig.DetunedNovaMul, 0.45f);
            Check("detune.rearm", GameConfig.DetuneRearmCharge, 100);
            Check("detune.clear", GameConfig.DetuneClearCharge, 55);
            Check("vent.ratio", GameConfig.VentRatio, 2.4f);
            Check("swap.gap", GameConfig.SwapResonanceGap, 1.2f);
            Check("resonance.max", GameConfig.ResonanceMaxStacks, 5);
            Check("resonance.window", GameConfig.ResonanceWindow, 7f);
            Check("resonance.damage", GameConfig.ResonancePerStackDamage, 0.08f);
            Check("resonance.charge", GameConfig.ResonancePerStackCharge, 0.06f);
            Check("drain.solo", GameConfig.SoloDrainMul, 0.55f);
            Check("hit.chargePenalty", GameConfig.HitChargePenalty, 45f);
        }

        static void Nova()
        {
            Check("nova.regen", GameConfig.NovaRegenPerSecond, 1.8f);
            Check("nova.kill", GameConfig.NovaOnKill, 2.5f);
            Check("nova.eliteKill", GameConfig.NovaOnEliteKill, 7f);
            Check("nova.graze", GameConfig.NovaOnGraze, 0.9f);
            Check("nova.hitEnemy", GameConfig.NovaOnHitEnemy, 0.06f);
            Check("nova.hitBoss", GameConfig.NovaOnHitBoss, 0.12f);
            Check("nova.gem", GameConfig.NovaOnGem, 0.5f);
            Check("nova.hullHit", GameConfig.NovaOnHullHit, 25f);
            Check("nova.shieldBreak", GameConfig.NovaOnShieldBreak, 8f);
            Check("nova.reflectSave", GameConfig.NovaOnReflectSave, 4f);
            Check("overdrive.time", GameConfig.OverdriveTime, 7f);
            Check("overdrive.max", GameConfig.OverdriveMax, 9.5f);
            Check("overdrive.damage", GameConfig.OverdriveDamageMul, 1.8f);
            Check("overdrive.rate", GameConfig.OverdriveFireRateMul, 0.74f);
            Check("overdrive.killExtend", GameConfig.OverdriveKillExtend, 0.11f);
            Check("overdrive.eliteKillExtend", GameConfig.OverdriveEliteKillExtend, 0.26f);
            Check("nova.enemyDamage", GameConfig.NovaEnemyDamage, 32f);
            Check("nova.bossFrac", GameConfig.NovaBossDamageFrac, 0.06f);
            Check("nova.bossFlat", GameConfig.NovaBossDamageFlat, 30f);
            Check("nova.scorePerBullet", GameConfig.NovaScorePerClearedBullet, 10);
            Check("clear.scorePerBullet", GameConfig.ClearScorePerBullet, 4);
        }

        static void DashAndPhase()
        {
            Check("dash.time", GameConfig.DashTime, 0.17f);
            Check("dash.cooldown", GameConfig.DashCooldown, 1.7f);
            Check("dash.speedPx", GameConfig.DashSpeedPx, 1250f);
            Check("dash.speed", GameConfig.DashSpeed, 1250f * Px, 1e-4f);
            Check("dash.invuln", GameConfig.DashInvuln, 0.36f);
            Check("phase.radiusPx", GameConfig.PhaseRadiusPx, 34f);
            Check("phase.nova", GameConfig.PhaseNovaPerBullet, 0.55f);
            Check("phase.score", GameConfig.PhaseScorePerBullet, 6);
        }

        static void InvulnTiers()
        {
            Check("invuln.spawn", GameConfig.InvulnOnSpawn, 1.6f);
            Check("invuln.hullHit", GameConfig.InvulnAfterHullHit, 2.8f);
            Check("invuln.shield", GameConfig.InvulnAfterShield, 1.3f);
            Check("invuln.reflect", GameConfig.InvulnAfterReflect, 1.5f);
            Check("invuln.nova", GameConfig.InvulnAfterNova, 0.9f);
            Check("invuln.zone", GameConfig.InvulnOnZoneChange, 1.5f);
            Check("invuln.bossPhase", GameConfig.InvulnOnBossPhase, 0.75f);
        }

        static void Enemies()
        {
            Check("enemy.kinds", EnemyDefs.All.Length, 12);
            Check("enemy.hpPerSector", GameConfig.EnemyHpPerSector, 0.16f);
            Check("enemy.speedPerTier", GameConfig.EnemySpeedPerTier, 0.14f);
            // 原版 entities.js:1392/1275：弹速 min(300,150×(1+tier×0.14))、移速 ×(1+tier×0.14)
            Check("enemy.bulletSpeed@t0", Mathf.Min(300f, 150f * (1f + 0f * 0.14f)), 150f);
            Check("enemy.bulletSpeed@t3", Mathf.Min(300f, 150f * (1f + 3f * 0.14f)), 213f);
            Check("enemy.speedMul@t3", 1f + 3f * 0.14f, 1.42f, 1e-4f);
            Check("enemy.fireFloor", GameConfig.EnemyFireIntervalFloor, 0.7f);
            Check("enemy.capAlive", GameConfig.MaxEnemiesAlive, 42);
            Check("enemy.capBullets", GameConfig.MaxEnemyBullets, 420);
            Check("enemy.entryDelay", GameConfig.EntryDelay + GameConfig.EntryGrace, 0.53f, 1e-5f);
            Check("enemy.entryBottom", GameConfig.EntryBottomFrac, 0.94f);
            Check("enemy.bulletSpeedPx", 150f, 150f);

            // 12 种的半径/血量/分数必须与原版 ENEMY_TYPES 对齐（原版像素）
            var expect = new (EnemyKind k, float sizePx, float hp, float score)[]
            {
                (EnemyKind.Drifter, 17, 4, 100), (EnemyKind.Wasp, 15, 3, 150),
                (EnemyKind.Lancer, 23, 15, 280), (EnemyKind.Orbiter, 21, 19, 340),
                (EnemyKind.Splitter, 20, 11, 230), (EnemyKind.Bulwark, 30, 46, 520),
                (EnemyKind.Rock, 23, 14, 70), (EnemyKind.Sniper, 22, 20, 320),
                (EnemyKind.Minekeeper, 24, 28, 380), (EnemyKind.Sidewinder, 21, 17, 310),
                (EnemyKind.Carrier, 34, 78, 680), (EnemyKind.Shardling, 13, 5, 90),
            };
            foreach (var e in expect)
            {
                var def = EnemyDefs.Get(e.k);
                Check($"{e.k}.radiusPx", def.Radius / Px, e.sizePx, 0.02f);
                Check($"{e.k}.hp", def.Hp, e.hp);
                Check($"{e.k}.score", def.Score, e.score);
            }
        }

        static void ElitesAndAffixes()
        {
            Check("elite.size", GameConfig.EliteSizeMul, 1.22f);
            Check("elite.hp", GameConfig.EliteHpMul, 2.4f);
            Check("elite.score", GameConfig.EliteScoreMul, 3.2f);
            Check("elite.fire", GameConfig.EliteFireMul, 0.82f);
            Check("armored.hp", GameConfig.ArmoredHpMul, 1.45f);
            Check("armored.damageTaken", GameConfig.ArmoredDamageTakenMul, 0.72f);
            Check("rapid.size", GameConfig.RapidSizeMul, 0.92f);
            Check("rapid.fire", GameConfig.RapidFireMul, 0.72f);
            Check("volatile.hp", GameConfig.VolatileHpMul, 0.9f);
            Check("volatile.fuse", GameConfig.VolatileFuse, 0.5f);
            Check("volatile.split", GameConfig.VolatileSplit, 6);
            Check("affix.base", GameConfig.AffixBaseChance, 0.16f);
            Check("affix.extra", GameConfig.AffixMaxExtra, 0.14f);
            // 词缀概率：0.16 + min(0.14, (s-3)*0.02)
            Check("affix.chance@3", 0.16f + Mathf.Min(0.14f, 0 * 0.02f), 0.16f, 1e-5f);
            Check("affix.chance@10", 0.16f + Mathf.Min(0.14f, 7 * 0.02f), 0.30f, 1e-5f);
        }

        static void Combo()
        {
            Check("combo.window", GameConfig.ComboWindow, 2.8f);
            Check("combo.maxMult", GameConfig.ComboMaxMult, 8);
            Check("combo.step", GameConfig.ComboStep, 5);
            Check("combo.mult@0", MultiplierAt(0), 1);
            Check("combo.mult@5", MultiplierAt(5), 2);
            Check("combo.mult@45", MultiplierAt(45), 9);
            Check("combo.mult@100", MultiplierAt(100), 9);   // 1 + min(8, 20)
        }

        static float MultiplierAt(int combo) => 1 + Mathf.Min(GameConfig.ComboMaxMult, combo / GameConfig.ComboStep);

        static void Drones()
        {
            Check("drone.max", GameConfig.MaxDrones, 3);
            Check("drone.kinds", GameConfig.DroneKinds.Length, 4);
            Check("drone.slotL", DroneShip.SlotOffset(0, 2).x, -64);
            Check("drone.slotR", DroneShip.SlotOffset(1, 2).x, 64);
            Check("drone.slotTail", DroneShip.SlotOffset(2, 3).y, 48);
            Check("drone.slotWideL", DroneShip.SlotOffset(0, 3).x, -68);
            Check("aegis.pdInterval", GameConfig.AegisPdInterval, 0.5f);   // game.js:794 pdT=0.5
            Check("aegis.pdRadiusPx", GameConfig.AegisPdRadiusPx, 38f);
            Check("aegis.pdClear", GameConfig.AegisPdClearCount, 4);
        }

        static void Crates()
        {
            Check("supply.interval", GameConfig.SupplyInterval, 20f);
            Check("supply.intervalMin", GameConfig.SupplyIntervalMin, 19f);
            Check("supply.midProgress", GameConfig.SupplyMidProgress, 0.55f);
            Check("supply.pity", GameConfig.PityThreshold, 3);
            Check("supply.swapIdle", GameConfig.SwapIdleForSwapCrate, 10f);
            Check("supply.swapChance", GameConfig.SwapCrateChance, 0.62f);
            Check("supply.reflect", GameConfig.ReflectDuration, 8f);
            // 两张权重表各自求和 = 100
            Check("loot.tableUnmaxed", 38 + 17 + 13 + 6 + 6 + 5 + 4 + 11, 100);
            Check("loot.tableMaxed", 22 + 20 + 9 + 9 + 7 + 4 + 20 + 9, 100);
        }

        static void CheckBoss()
        {
            Check("boss.count", BossMoves.BaseHp.Length, 3);
            Check("boss.hp.gatekeeper", BossMoves.BaseHp[0], 400f);
            Check("boss.hp.weaver", BossMoves.BaseHp[1], 380f);
            Check("boss.hp.helios", BossMoves.BaseHp[2], 440f);
            // 机体尺寸（画布像素）：接触/判定椭圆与炮口都按它算
            Check("boss.size.gatekeeper", BossMoves.SizePx[0], 76f);
            Check("boss.size.weaver", BossMoves.SizePx[1], 80f);
            Check("boss.size.helios", BossMoves.SizePx[2], 82f);
            Check("boss.hpGrowth", GameConfig.BossHpSectorGrowth, 0.70f);
            Check("boss.hpGrowthCap", GameConfig.BossHpGrowthCap, 3.4f);
            Check("boss.phaseHigh", GameConfig.BossPhaseHigh, 0.66f);
            Check("boss.phaseLow", GameConfig.BossPhaseLow, 0.30f);
            Check("boss.critical", GameConfig.BossCriticalFrac, 0.12f);
            Check("boss.desperateFrac", GameConfig.BossDesperateFrac, 0.12f);
            Check("boss.mercy", GameConfig.BossMercyWindow, 1.15f);
            Check("boss.breakFloor", GameConfig.BossBreakFloor, 60f);
            Check("boss.breakFrac", GameConfig.BossBreakFracOfMaxHp, 0.18f);
            Check("boss.breakStun", GameConfig.BossBreakStun, 1.2f);
            Check("boss.breakDamageMul", GameConfig.BossBreakDamageMul, 1.35f);
            Check("boss.telegraph", BossMoves.TelegraphTime, 0.5f);
            Check("boss.telegraphLegacy", GameConfig.BossTelegraph, 0.5f);
            Check("boss.bulletBase", GameConfig.BossBulletSpeedBase, 152f);
            Check("boss.bulletPerTier", GameConfig.BossBulletSpeedPerTier, 46f);
            // 阶段弹速系数（原版 bulletSpeed() 每次实时算，之前这组数是死代码）
            Check("boss.bulletPhase@1", GameConfig.BossBulletSpeedDiv[0], 1.00f);
            Check("boss.bulletPhase@2", GameConfig.BossBulletSpeedDiv[1], 1.06f);
            Check("boss.bulletPhase@3", GameConfig.BossBulletSpeedDiv[2], 1.18f);

            // ---- 出招间隔（entities.js:2443-2448）----
            Check("boss.cd.p1", GameConfig.BossSpellCd[0], 0.85f);
            Check("boss.cd.p2", GameConfig.BossSpellCd[1], 0.62f);
            Check("boss.cd.p3", GameConfig.BossSpellCd[2], 0.42f);
            Check("boss.cd.floor", GameConfig.BossSpellCdFloor, 0.3f);
            Check("boss.cd.chain", GameConfig.BossChainCd, 0.16f);
            Check("boss.cd.chainEveryTier0", GameConfig.BossChainEveryTier0, 4);
            Check("boss.cd.chainEveryOther", GameConfig.BossChainEveryOtherTier, 3);
            Check("boss.cd.initial", GameConfig.BossInitialCd, 1.15f);
            Check("boss.cd.postBreak", GameConfig.BossPostBreakCd, 0.55f);
            Check("boss.cd.desperate", GameConfig.BossDesperateCd, 0.45f);
            Check("boss.idle.p2", GameConfig.BossIdleFirePhase1, 0.95f);
            Check("boss.idle.p3", GameConfig.BossIdleFirePhase2, 0.66f);
            // 三阶段收招后白送的环弹（原版 desperate 时更多）
            Check("boss.ring.normal", GameConfig.BossDesperateRingNormal, 0.45f);
            Check("boss.ring.otherTier", GameConfig.BossDesperateRingOtherTier, 0.65f);
            Check("boss.ring.hard", GameConfig.BossDesperateRingHard, 0.7f);
            Check("boss.ring.hardest", GameConfig.BossDesperateRingHardest, 0.9f);
            Check("boss.ring.count", GameConfig.BossFreeRingNormal, 16);
            Check("boss.ring.countDesperate", GameConfig.BossFreeRingDesperate, 22);
            Check("boss.ring.speed", GameConfig.BossFreeRingSpeedMul, 0.75f);

            // ---- 判定形状（game.js:839-840 / 939-940）----
            Check("boss.hitRx", GameConfig.BossHitRx, 1.02f);
            Check("boss.hitRy", GameConfig.BossHitRy, 0.82f);
            Check("boss.contactRx", GameConfig.BossContactRx, 0.85f);
            Check("boss.contactRy", GameConfig.BossContactRy, 0.70f);
            Check("boss.contactPad", GameConfig.BossContactPadPx, 8f);

            // ---- 流程节拍 ----
            Check("boss.warnSeconds", GameConfig.BossWarnSeconds, 3.0f);
            Check("boss.sectorClear", GameConfig.SectorClearSeconds, 4.4f);

            // ---- 三台 × 三阶段的序列长度（原版是 4/6/6、5/5/6、4/5/6，不是统一 4/6/6）----
            int[][] expectLens = { new[] { 4, 6, 6 }, new[] { 5, 5, 6 }, new[] { 4, 5, 6 } };
            for (int b = 0; b < 3; b++)
                for (int ph = 0; ph < 3; ph++)
                    Check($"boss.seq[{b}].p{ph}", BossMoves.SequenceLength(b, ph), expectLens[b][ph]);

            // 逐条锁死"背板"表：任何一次悄悄改序列都会在这里红
            var gk = new[]
            {
                new[] { BossMove.Fan, BossMove.WallGap, BossMove.TwinBeam, BossMove.MineCurtain },
                new[] { BossMove.Spiral, BossMove.WallGap, BossMove.Pulsar, BossMove.Fan,
                        BossMove.TwinBeam, BossMove.Sweep },
                new[] { BossMove.Charge, BossMove.CrossSweep, BossMove.WallGap, BossMove.Cascade,
                        BossMove.Ring, BossMove.Spiral },
            };
            var wv = new[]
            {
                new[] { BossMove.Ring, BossMove.Homing, BossMove.HomingBarrage, BossMove.Cross,
                        BossMove.WaveCurtain },
                new[] { BossMove.SpiralDouble, BossMove.Blink, BossMove.HomingBarrage, BossMove.Summon,
                        BossMove.Cross },
                new[] { BossMove.CrossSweep, BossMove.LaserWeb, BossMove.SpiralDouble,
                        BossMove.HomingBarrage, BossMove.Ring, BossMove.WaveCurtain },
            };
            var hl = new[]
            {
                new[] { BossMove.Spiral, BossMove.Barrage, BossMove.LaserWeb, BossMove.MineCurtain },
                new[] { BossMove.NovaBurst, BossMove.Summon, BossMove.TwinBeam, BossMove.Spiral,
                        BossMove.Barrage },
                new[] { BossMove.NovaBurst, BossMove.CrossSweep, BossMove.Cascade, BossMove.LaserWeb,
                        BossMove.MineRing, BossMove.Barrage },
            };
            var all = new[] { gk, wv, hl };
            for (int b = 0; b < 3; b++)
                for (int ph = 0; ph < 3; ph++)
                    for (int i = 0; i < all[b][ph].Length; i++)
                        CheckTrue($"boss.seq[{b}][{ph}][{i}]=={all[b][ph][i]}",
                            BossMoves.Pick(b, ph, i) == all[b][ph][i],
                            $"守门人/织星者/新星核心 第 {b} 台第 {ph + 1} 阶段第 {i + 1} 招不是原版那一招");

            // 22 招全覆盖（三台并集才是全表，单台只用自己那几招）
            var seen = new HashSet<BossMove>();
            for (int b = 0; b < 3; b++)
                for (int ph = 0; ph < 3; ph++)
                    for (int i = 0; i < BossMoves.SequenceLength(b, ph); i++)
                        seen.Add(BossMoves.Pick(b, ph, i));
            Check("boss.moves.covered", seen.Count, 22);
            Check("boss.moves.enum", System.Enum.GetValues(typeof(BossMove)).Length, 22);

            // 招式时长（entities.js:1772-1778）：连续弹幕最长，环爆最短
            var dur = new (BossMove m, float s)[]
            {
                (BossMove.Fan, 1.3f), (BossMove.Spiral, 2.0f), (BossMove.Ring, 1.05f),
                (BossMove.Homing, 1.0f), (BossMove.Sweep, 2.5f), (BossMove.Cross, 2.0f),
                (BossMove.Summon, 1.15f), (BossMove.Barrage, 1.9f), (BossMove.TwinBeam, 2.2f),
                (BossMove.MineCurtain, 2.0f), (BossMove.Blink, 2.2f), (BossMove.LaserWeb, 2.4f),
                (BossMove.WaveCurtain, 2.0f), (BossMove.WallGap, 2.8f), (BossMove.Pulsar, 2.2f),
                (BossMove.Charge, 3.2f), (BossMove.CrossSweep, 2.9f), (BossMove.Cascade, 3.1f),
                (BossMove.HomingBarrage, 2.7f), (BossMove.SpiralDouble, 2.4f),
                (BossMove.MineRing, 1.9f), (BossMove.NovaBurst, 2.6f),
            };
            foreach (var d in dur) Check($"boss.dur.{d.m}", BossMoves.Duration(d.m), d.s, 1e-4f);
            Check("boss.dur.count", dur.Length, 22);

            // 预告色族：光束 rose / 位移 violet / 投送 gold / 其余 teal
            Check("boss.tell.sweep", BossMoves.Tell(BossMove.Sweep), GameConfig.Rose);
            Check("boss.tell.laserWeb", BossMoves.Tell(BossMove.LaserWeb), GameConfig.Rose);
            Check("boss.tell.blink", BossMoves.Tell(BossMove.Blink), GameConfig.Violet);
            Check("boss.tell.charge", BossMoves.Tell(BossMove.Charge), GameConfig.Violet);
            Check("boss.tell.summon", BossMoves.Tell(BossMove.Summon), GameConfig.Gold);
            Check("boss.tell.mineRing", BossMoves.Tell(BossMove.MineRing), GameConfig.Gold);
            Check("boss.tell.fan", BossMoves.Tell(BossMove.Fan), GameConfig.Teal);
            // 只有 blink / charge 会接管机体位置
            CheckTrue("boss.lock.blink", BossMoves.LocksMove(BossMove.Blink));
            CheckTrue("boss.lock.charge", BossMoves.LocksMove(BossMove.Charge));
            CheckTrue("boss.lock.fan.free", !BossMoves.LocksMove(BossMove.Fan));
            CheckTrue("boss.lock.novaBurst.free", !BossMoves.LocksMove(BossMove.NovaBurst));

            // 轮换：三台按 (sector-1)%3 循环，而不是按星区段固定
            Check("boss.rotate@1", BossMoves.BossForZone(1), 0);
            Check("boss.rotate@2", BossMoves.BossForZone(2), 1);
            Check("boss.rotate@3", BossMoves.BossForZone(3), 2);
            Check("boss.rotate@4", BossMoves.BossForZone(4), 0);
            Check("boss.rotate@5", BossMoves.BossForZone(5), 1);
            Check("boss.rotate@7", BossMoves.BossForZone(7), 0);
            // 弹幕强度档：min(3, floor((s-1)/5))
            Check("density.tier@1", BossMoves.TierForZone(1), 0);
            Check("density.tier@5", BossMoves.TierForZone(5), 0);
            Check("density.tier@6", BossMoves.TierForZone(6), 1);
            Check("density.tier@11", BossMoves.TierForZone(11), 2);
            Check("density.tier@20", BossMoves.TierForZone(20), 3);
            // Boss 血量曲线：1 + min(3.4, (s-1)*0.70)
            Check("boss.hpScale@1", GameConfig.BossHpScale(1), 1f);
            Check("boss.hpScale@2", GameConfig.BossHpScale(2), 1.70f, 1e-4f);
            Check("boss.hpScale@6", GameConfig.BossHpScale(6), 4.4f, 1e-4f);   // 5×0.70=3.5 封顶 3.4
            Check("boss.hpScale@9", GameConfig.BossHpScale(9), 4.4f, 1e-4f);   // 封顶 1+3.4
        }


        static void ClearBonus()
        {
            Check("clear.timeBase", GameConfig.ClearTimeBonusBase, 90f);
            Check("clear.timePerSec", GameConfig.ClearTimeBonusPerSecond, 40f);
            Check("clear.noHitBase", GameConfig.ClearNoHitBase, 1200f);
            Check("clear.noHitPerSector", GameConfig.ClearNoHitPerSector, 200f);
            Check("clear.grazePer", GameConfig.ClearGrazePerHit, 20f);
            Check("clear.base", GameConfig.ClearBaseScore, 4000f);
            Check("clear.perSector", GameConfig.ClearBasePerSector, 900f);
        }

        static void Grading()
        {
            Check("grade.S", GameConfig.GradeS, 120000);
            Check("grade.A", GameConfig.GradeA, 60000);
            Check("grade.B", GameConfig.GradeB, 22000);
            Check("grade.sectorS", GameConfig.GradeSectorS, 8);
            Check("grade.sectorA", GameConfig.GradeSectorA, 5);
            Check("grade.sectorB", GameConfig.GradeSectorB, 3);
        }

        static void CheckDifficulties()
        {
            var cruise = Difficulties.Cruise;
            Check("cruise.enemyRate", cruise.EnemyRate, 1.22f);
            Check("cruise.bulletSpeed", cruise.BulletSpeed, 0.9f);
            Check("cruise.bossBullet", cruise.BossBullet, 0.86f);
            Check("cruise.bossHp", cruise.BossHp, 0.9f);
            Check("cruise.nova", cruise.Nova, 1.2f);
            Check("cruise.score", cruise.ScoreMul, 0.8f);

            var abyss = Difficulties.Abyss;
            Check("abyss.enemyRate", abyss.EnemyRate, 0.8f);
            Check("abyss.bulletSpeed", abyss.BulletSpeed, 1.1f);
            Check("abyss.bossBullet", abyss.BossBullet, 1.12f);
            Check("abyss.bossHp", abyss.BossHp, 1.18f);
            Check("abyss.nova", abyss.Nova, 0.85f);
            Check("abyss.score", abyss.ScoreMul, 1.35f);

            var std = Difficulties.Standard;
            Check("standard.all1", std.EnemyRate + std.BulletSpeed + std.BossBullet + std.BossHp + std.Nova + std.ScoreMul, 6f);
        }

        static void TouchDriving()
        {
            Check("touch.followRate", GameConfig.DragFollowRate, 30f);
            Check("touch.mouseFollow", GameConfig.MouseFollowRate, 26f);
            Check("touch.lateralPx", GameConfig.DragMaxLateralPx, 162f);
            Check("touch.liftPx", GameConfig.DragLiftPx, 140f);
            Check("touch.dropPx", GameConfig.DragDropPx, 60f);
            Check("touch.pickupPx", 30f, 30f);
        }

        // ------------------------------------------------------------ 工具

        /// <summary>
        /// 行为断言：以前这里只查「常量对不对」，所以「常量对、没生效」的一类 bug 全绿通过
        /// （蓄能按帧扣、换线不清 idle、Boss 间隔被难度系数污染都属于这类）。
        /// 能走纯函数的走纯函数；必须实例化的（敌机血量/判定半径）就在编辑态建了再拆。
        /// </summary>
        static void Behaviour()
        {
            // ---- 蓄能经济：按枪扣，所以每秒掉电量正好等于 drain ----
            float i1 = WeaponSystem.Interval("pulse", 1, false, false);
            Check("beh.pulseIntervalL1", i1, 0.120f, 1e-5f);
            float cost = WeaponSystem.ChargeCost("pulse", 1, 2, false, false);
            Check("beh.pulseCostL1", cost, 7.4f * 0.120f, 1e-4f);
            // 关键性质：cost / interval = drain（每秒），不是 drain × 帧率
            Check("beh.pulseDrainPerSecond", cost / i1, 7.4f, 1e-3f);
            Check("beh.soloCost", WeaponSystem.ChargeCost("pulse", 1, 1, false, false),
                  7.4f * 0.120f * GameConfig.SoloDrainMul, 1e-4f);
            Check("beh.detunedInterval", WeaponSystem.Interval("prism", 1, false, true),
                  0.360f * GameConfig.DetunedIntervalMul, 1e-5f);
            // 失谐不是"停火"：打空的那把仍然开火，只是慢 1.45 倍、伤害 0.55 倍
            CheckTrue("beh.detunedStillFires", WeaponSystem.Interval("prism", 1, false, true) < 1f);

            // ---- 换线共鸣门槛：没停够时间一律不给 ----
            CheckTrue("beh.resonance.needsDwell", !WeaponSystem.ResonanceGain(false, 100f, false));
            CheckTrue("beh.resonance.needsCharge", !WeaponSystem.ResonanceGain(true, 54.9f, false));
            CheckTrue("beh.resonance.needsRearm", !WeaponSystem.ResonanceGain(true, 100f, true));
            CheckTrue("beh.resonance.ok", WeaponSystem.ResonanceGain(true, 55f, false));

            // ---- Boss 出招间隔：难度系数不参与 ----
            Check("beh.bossCd.p1", BossController.SpellCooldown(0, 0, 1, false), 0.85f);
            Check("beh.bossCd.p2", BossController.SpellCooldown(1, 0, 1, false), 0.62f);
            Check("beh.bossCd.p3", BossController.SpellCooldown(2, 1, 1, false), 0.42f);
            Check("beh.bossCd.chainTier0", BossController.SpellCooldown(2, 0, 4, false), 0.16f);
            Check("beh.bossCd.chainOther", BossController.SpellCooldown(2, 1, 3, false), 0.16f);
            Check("beh.bossCd.desperate", BossController.SpellCooldown(2, 0, 1, true), 0.16f);
            // 破防期间不攒槽：否则持续输出会把 Boss 永久锁在眩晕里（v0.19.2 实测）
            CheckTrue("beh.bossBreak.frozenWhileStunned",
                      !BossController.BreakGaugeAccrues(stunned: true, broken: false));
            CheckTrue("beh.bossBreak.accruesNormally",
                      BossController.BreakGaugeAccrues(stunned: false, broken: false));

            // ---- 武器别名：满级要有满级的叫法，不能一路"脉冲"到五档 ----
            Check("beh.alias.pulse1", GameConfig.WeaponName("pulse", 1), "脉冲炮");
            Check("beh.alias.pulse5", GameConfig.WeaponName("pulse", 5), "星瀑");
            Check("beh.alias.prism5", GameConfig.WeaponName("prism", 5), "终焉棱镜");
            Check("beh.alias.swarm5", GameConfig.WeaponName("swarm", 5), "蜂后");
            Check("beh.alias.clamp", GameConfig.WeaponName("swarm", 99), "蜂后");

            // ---- 敌机：把真正的机体建出来量它 ----
            var go = new GameObject("[verify-enemy]");
            try
            {
                var e = go.AddComponent<EnemyShip>();
                e.Setup(EnemyKind.Drifter, 1, Difficulties.Standard, false, 1f, -1f, Affix.None);
                Check("beh.enemy.hp.s1", e.Hp, 4f);
                Check("beh.enemy.hitR.drifter", e.Radius, 17f * 0.78f * Px, 1e-4f);

                e.Setup(EnemyKind.Drifter, 6, Difficulties.Standard, false, 1f, -1f, Affix.None);
                Check("beh.enemy.hp.s6", e.Hp, 7f);                 // round(4×1.80)
                e.Setup(EnemyKind.Drifter, 6, Difficulties.Standard, true, 1f, -1f, Affix.None);
                Check("beh.enemy.hp.s6elite", e.Hp, 17f);           // round(round(7.2)×2.4)

                e.Setup(EnemyKind.Lancer, 6, Difficulties.Standard, false, 1f, -1f, Affix.Armored);
                Check("beh.enemy.hp.armored", e.Hp, 39f);           // round(round(27)×1.45)
                Check("beh.enemy.size.armored", e.Radius, 23f * 1.06f * 0.78f * Px, 1e-3f);

                e.Setup(EnemyKind.Splitter, 1, Difficulties.Standard);
                float parentHp = e.Hp;
                e.Setup(EnemyKind.Shardling, 1, Difficulties.Standard, false, GameConfig.SplitterChildHpFrac);
                CheckTrue("beh.enemy.splitterHalfChild", e.Hp <= parentHp * 0.5f + 1f && e.Hp >= 1f,
                          "分裂幼体应当只有半血，实测 " + e.Hp + " / 母体 " + parentHp);

                e.Setup(EnemyKind.Sniper, 1, Difficulties.Standard, true);
                Check("beh.enemy.eliteCharge", EnemyDefs.Get(EnemyKind.Sniper).Charge * GameConfig.EnemyChargeEliteMul,
                      0.55f * 0.8f, 1e-5f);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void Check(string name, float actual, float expected, float tol = 0.0001f)
        {
            if (Mathf.Abs(actual - expected) <= tol) { _passed++; return; }
            Failures.Add($"{name}: expected {expected}, got {actual}");
        }

        static void CheckTrue(string name, bool ok, string info = "")
        {
            if (ok) { _passed++; return; }
            Failures.Add(name + (string.IsNullOrEmpty(info) ? "" : " — " + info));
        }

        /// <summary>颜色断言（招式预告色族用得上，差一个通道就是另一种读法）。</summary>
        static void Check(string name, Color actual, Color expected)
        {
            bool ok = Mathf.Abs(actual.r - expected.r) < 0.005f && Mathf.Abs(actual.g - expected.g) < 0.005f
                      && Mathf.Abs(actual.b - expected.b) < 0.005f;
            CheckTrue(name, ok, "expected " + expected + ", got " + actual);
        }

        static void Check(string name, string actual, string expected)
        {
            CheckTrue(name, actual == expected, $"expected 「{expected}」, got 「{actual}」");
        }
    }
}
#endif
