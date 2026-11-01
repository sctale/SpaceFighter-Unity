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
            Check("aegis.pdInterval", GameConfig.AegisPdInterval, 0.7f);
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
            Check("boss.hpGrowth", GameConfig.BossHpSectorGrowth, 0.70f);
            Check("boss.hpGrowthCap", GameConfig.BossHpGrowthCap, 3.4f);
            Check("boss.phaseHigh", GameConfig.BossPhaseHigh, 0.66f);
            Check("boss.phaseLow", GameConfig.BossPhaseLow, 0.30f);
            Check("boss.critical", GameConfig.BossCriticalFrac, 0.12f);
            Check("boss.mercy", GameConfig.BossMercyWindow, 1.15f);
            Check("boss.breakFloor", GameConfig.BossBreakFloor, 60f);
            Check("boss.breakFrac", GameConfig.BossBreakFracOfMaxHp, 0.18f);
            Check("boss.breakStun", GameConfig.BossBreakStun, 1.2f);
            Check("boss.breakDamageMul", GameConfig.BossBreakDamageMul, 1.35f);
            Check("boss.telegraph", GameConfig.BossTelegraph, 0.5f);
            Check("boss.bulletBase", GameConfig.BossBulletSpeedBase, 152f);
            Check("boss.bulletPerTier", GameConfig.BossBulletSpeedPerTier, 46f);

            // 三台 × 三阶段的序列长度 4 / 6 / 6
            for (int b = 0; b < 3; b++)
            {
                Check($"boss.seq[{b}].p0", BossMoves.SequenceLength(b, 0), 4);
                Check($"boss.seq[{b}].p1", BossMoves.SequenceLength(b, 1), 6);
                Check($"boss.seq[{b}].p2", BossMoves.SequenceLength(b, 2), 6);
            }
            // 22 招全覆盖
            var seen = new HashSet<BossMove>();
            for (int b = 0; b < 3; b++)
                for (int ph = 0; ph < 3; ph++)
                    for (int i = 0; i < BossMoves.SequenceLength(b, ph); i++)
                        seen.Add(BossMoves.Pick(b, ph, i));
            Check("boss.moves.covered", seen.Count, 22);
            Check("boss.moves.enum", System.Enum.GetValues(typeof(BossMove)).Length, 22);

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
    }
}
#endif
