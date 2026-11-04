# v0.21.4 — 修 Boss 被技能打断后不出手

## 根因

`BossController.Entering` 原来是一个**位置比较**：

```csharp
public bool Entering => transform.position.y > _anchor.y + 0.01f;
```

而 `Hover()` 会让 Boss 在悬停点上方 ±7–12px 做正弦摆动。于是每个悬停周期的上半段：

- `Update` 误走「入场下降」分支，把 Boss 往下推并 `return`，**跳过出招计时**；
- `Director` 的玩家子弹碰撞用 `!Boss.Entering` 门槛，**上半段无敌**；
- `TakeDamage` 同理，技能/破防伤害经常被吞。

实测出招计时一度只以 **0.2/s** 前进（正常 1.0/s），破防后更容易看起来「完全不出手」。
`Charge/Blink` 会把机体抬到悬停点之上，旧逻辑还会让整招无敌。

## 修法

- 改为一次性 `_entered` 标志：只有入场下降这一段 `Entering = true`，落到悬停点后永远为 false。
  Charge/Blink 抬高不再影响判定，子弹与技能都能正常命中。
- 破防/技能打断期间按网页版语义**冻结三只时钟**：预告、招式节拍、出招计时；眩晕结束再恢复。
  不会再把招式的剩余节拍在眩晕里放完。
- 眩晕结束 `ResumeTimer()` 把出招计时压到 **≤0.55s**，清理空标签；招式已超时就 `EndMove()`。
- 状态机看门狗：`_timer / _telegraph / _stun / _moveT / _moveDur` 出现 NaN/Infinity/招式超时，
  一律拉回可出招状态。
- 出招期间补回 `Hover()`（移动类招式仍由招式接管），和网页版一致。
- 顺手修 `Phase` 公开属性从未赋值：Boss 面板现在会从 P1 正常变 P2/P3。

## 验收

- 门禁 **438/438**（新增 4 条 `ResumeTimer` 断言）。
- `-probe` 实测：
  - 普通局约每 **2.5 秒**一招；
  - 7 秒强制破防后 `[BOSS-RESUME]` 立即恢复，8.7 秒重新进入「环状爆发」，
    随后「追踪星群」「猎群连射」正常；
  - `Charge` 中破防也能恢复，并在下一帧正常进入阶段切换。
- 实战截图：`docs/shots/boss_v0214_attack.png`（P2、逆向双旋、环形弹幕）。
- 复现命令：

```bash
Builds/Windows/SpaceFighter.exe -screen-width 540 -screen-height 960 \
  -shots -autoshot -long -probe -boss -zone 5 -break -tag finalboss
```

## APK

- 大小：**29,056,360** 字节；versionName **0.21.4** / versionCode **32**；
  V2 签名 `CN=Android Debug`；sha256 `66e97d87a696bc7a8a9a58a4645da332df7bb44e31345b11693c2445676dc557`。
