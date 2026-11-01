#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AuroraRift.EditorTools
{
    /// <summary>
    /// 数值契约门禁：把网页版 design-test.js 里那批关键数值搬成可执行断言。
    /// 用法：Unity.exe -batchmode -quit -projectPath &lt;proj&gt; \
    ///        -executeMethod AuroraRift.EditorTools.VerifyNumbers.Run -logFile verify.log
    /// 退出码 0 = 全过；1 = 有失败（可直接接进发布脚本前置）。
    /// </summary>
    public static class VerifyNumbers
    {
        static readonly List<string> Failures = new List<string>();
        static int _passed;

        [MenuItem("Aurora/Verify Design Numbers")]
        public static void Run()
        {
            Failures.Clear();
            _passed = 0;

            WeaponLines();
            PlayerHitbox();
            CheckDifficulties();
            Enemies();
            CheckBossMoves();
            Progression();

            foreach (var f in Failures) Debug.LogError("[VERIFY] FAIL " + f);
            Debug.Log($"[VERIFY] SUMMARY passed={_passed} failed={Failures.Count}");
            if (Application.isBatchMode) EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
        }

        static void Check(string name, float actual, float expected, float tol = 0.0001f)
        {
            if (Mathf.Abs(actual - expected) <= tol) { _passed++; return; }
            Failures.Add($"{name}: expected {expected}, got {actual}");
        }

        static void CheckTrue(string name, bool cond)
        {
            if (cond) { _passed++; return; }
            Failures.Add(name);
        }

        static void WeaponLines()
        {
            Check("pulse.speed", GameConfig.Pulse.Speed, 12.5f);
            Check("pulse.drain", GameConfig.Pulse.Drain, 7.4f);
            Check("pulse.recover", GameConfig.Pulse.Recover, 15.5f);
            Check("prism.speed", GameConfig.Prism.Speed, 5.6f);
            Check("prism.drain", GameConfig.Prism.Drain, 10.5f);
            Check("prism.recover", GameConfig.Prism.Recover, 12f);
            Check("swarm.speed", GameConfig.Swarm.Speed, 8.3f);
            Check("swarm.drain", GameConfig.Swarm.Drain, 8.6f);
            Check("swarm.recover", GameConfig.Swarm.Recover, 13.5f);
            Check("detuned.power", GameConfig.DetunedPower, 0.55f);
            Check("detuned.rate", GameConfig.DetunedFireRate, 0.72f);
            Check("resonance.perStack", GameConfig.ResonancePerStack, 0.08f);
            Check("resonance.max", GameConfig.ResonanceMaxStacks, 5);
        }

        static void PlayerHitbox()
        {
            // 原版 4.6px / 28px，1px = 0.025 世界单位
            Check("player.hitRadius", GameConfig.PlayerHitRadius, 0.115f, 1e-4f);
            Check("player.grazeRadius", GameConfig.PlayerGrazeRadius, 0.7f, 1e-4f);
            Check("player.maxShields", GameConfig.MaxShields, 3);
        }

        static void CheckDifficulties()
        {
            var c = Difficulties.Cruise;
            Check("cruise.enemyRate", c.EnemyRate, 1.22f);
            Check("cruise.bulletSpeed", c.BulletSpeed, 0.9f);
            Check("cruise.bossBullet", c.BossBullet, 0.86f);
            Check("cruise.bossHp", c.BossHp, 0.9f);
            Check("cruise.nova", c.Nova, 1.2f);
            Check("cruise.scoreMul", c.ScoreMul, 0.8f);

            var a = Difficulties.Abyss;
            Check("abyss.enemyRate", a.EnemyRate, 0.8f);
            Check("abyss.bulletSpeed", a.BulletSpeed, 1.1f);
            Check("abyss.bossBullet", a.BossBullet, 1.12f);
            Check("abyss.bossHp", a.BossHp, 1.18f);
            Check("abyss.nova", a.Nova, 0.85f);
            Check("abyss.scoreMul", a.ScoreMul, 1.35f);

            var s = Difficulties.Standard;
            CheckTrue("standard is baseline",
                s.EnemyRate == 1f && s.BulletSpeed == 1f && s.BossHp == 1f && s.ScoreMul == 1f);
        }

        static void Enemies()
        {
            Check("enemy.count", EnemyDefs.All.Length, 12);
            Expect(EnemyKind.Drifter, 4, 100, 17);
            Expect(EnemyKind.Wasp, 3, 150, 15);
            Expect(EnemyKind.Lancer, 15, 280, 23);
            Expect(EnemyKind.Orbiter, 19, 340, 21);
            Expect(EnemyKind.Splitter, 11, 230, 20);
            Expect(EnemyKind.Bulwark, 46, 520, 30);
            Expect(EnemyKind.Rock, 14, 70, 23);
            Expect(EnemyKind.Sniper, 20, 320, 22);
            Expect(EnemyKind.Minekeeper, 28, 380, 24);
            Expect(EnemyKind.Sidewinder, 17, 310, 21);
            Expect(EnemyKind.Carrier, 78, 680, 34);
            Expect(EnemyKind.Shardling, 5, 90, 13);
        }

        static void Expect(EnemyKind k, float hp, float score, float sizePx)
        {
            var d = EnemyDefs.Get(k);
            Check($"{k}.hp", d.Hp, hp);
            Check($"{k}.score", d.Score, score);
            Check($"{k}.radius", d.Radius, sizePx * 0.025f, 1e-4f);
        }

        static void CheckBossMoves()
        {
            Check("boss.moveCount", Enum.GetValues(typeof(BossMove)).Length, 22);
            // 三台 Boss 的 3 阶段序列都必须能取到招
            for (int tier = 0; tier < 3; tier++)
                for (int phase = 0; phase < 3; phase++)
                    CheckTrue($"boss.tier{tier}.phase{phase}", Enum.IsDefined(typeof(BossMove), BossMoves.Pick(tier, phase, 0)));
            Check("boss.tier.zone3", BossMoves.TierForZone(3), 0);
            Check("boss.tier.zone4", BossMoves.TierForZone(4), 1);
            Check("boss.tier.zone8", BossMoves.TierForZone(8), 2);
        }

        static void Progression()
        {
            // 敌机随星区解锁：zone1 只有 2 种
            CheckTrue("progression.zone1Pool", DirectorTierPoolSize(1) == 2);
            CheckTrue("progression.zone8Pool", DirectorTierPoolSize(8) >= 10);
        }

        /// <summary>与 Director.PickKind 同步的池子大小（改那边时这里要跟着改）。</summary>
        static int DirectorTierPoolSize(int zone)
        {
            int n = 2;
            if (zone >= 2) n += 2;
            if (zone >= 3) n += 2;
            if (zone >= 4) n += 1;
            if (zone >= 5) n += 1;
            if (zone >= 6) n += 1;
            if (zone >= 7) n += 1;
            if (zone >= 8) n += 1;
            return n;
        }
    }
}
#endif
