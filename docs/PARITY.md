# 与网页版的有意差异（PARITY）

参照实现冻结在 `web/www/js/`（原 space-fighter v2.8.4）。Unity 版的原则是
**数值、节奏、坐标一律以网页版为准**；只有下面这几条是 Unity 侧自己加的，
它们不是 bug，是刻意保留的产品决策，所以集中记在这里，免得下次对照时被当成"没对齐"。

## 保留的 Unity 独有增强

| 项 | 网页版 | Unity 版 | 开关在哪 |
|---|---|---|---|
| 剧本之外的随机精英 | 精英只在剧本显式写 `{elite:true}` 的位置出现（`director.js:107`） | 每个机种在达到星区门槛后还有 `RandomEliteChance` 的独立 roll | `GameConfig.RandomEliteChance`，改成 `0f` 即回到原版压力 |
| 星区封顶 | 无限星区，第 11 区起叫 `深空 NN 区`，血量曲线无上限 | 10 个星区封顶，之后停在第 10 区的曲线 | `Director.TotalZones` / `StartSector` 的 `Clamp` |
| 满级 power 箱 | 当前线满级时折成 +800 分 | 当前线满级时解锁/切换到下一条未解锁的线 | `WeaponSystem.GrantLevel()` |
| 通关星区补偿 | 结算只有 基础 + 时间 + 无伤 + 擦弹 | 额外 `500 × (区号+1)`，**显式列在结算面板的「星区补偿」一行** | `GameConfig.SectorCompensationPerZone` |
| 精英掉落表保底 | `pickUpgradeType()` 里没有 pity 计数 | 连续 3 次没掉到缺的武器线/僚机时必补 | `Director.PickUpgradeType()` 顶部两个分支 |
| 周期补给首箱节奏 | 20 秒后固定 19 秒 | 已对齐（原 `20 − 区号×0.2` 的自创收紧已删除） | — |

## 已经改成和原版一致、容易被误当"还能再调"的几处

- **蓄能开销按枪扣**：`WeaponSystem.ChargeCost()`，所以每秒掉电量正好等于 `line.drain`。
  任何"顺手改成每帧扣"都会让武器两秒打空（v0.17 就是这样）。
- **打空不停火**：失谐的那把仍然开火，代价是间隔 ×1.45、伤害 ×0.55（原版语义）。
- **Boss 出招间隔不吃难度**：难度系数只作用弹速与血量。
- **敌机判定半径 = size × 0.78**，美术仍按 size 画；两者是不同字段，别互相赋值。
- **敌弹命中用线段扫掠**（`segClosest`），点位判定会让 330px/s 的针弹穿人。
- **相位穿越只在 `DashT > 0` 的 0.17 秒里成立**，任何无敌帧都算会变成站着刷分。
- **时间奖励按整个星区用时**（`Director.SectorStartTime`），只算 Boss 战会几乎每次拿满。

## 还没做的原版特性

- 触感反馈：原版有 `SF.vibrate`（`settings.haptic`），Unity 侧还没接 `Handheld.Vibrate` /
  `MobileHaptic`。属于"能感觉到"的差异，但需要真机逐项试强度。
- 背景分层视差：原版 `background.js` 每层独立速度 + 星尘漂移，Unity 的 `Starfield`
  目前是按层给固定速度，缺少近景星云的整体位移。
- 无限星区与 `深空 NN 区` 命名（见上表，属产品决策不是遗漏）。
- 原版 `godMode`（调试用 1.2 秒无敌代替受击）没有对应命令。

## 怎么核对

```bash
# 数值 + 行为门禁（425 条）
Unity.exe -batchmode -quit -projectPath . \
  -executeMethod AuroraRift.EditorTools.VerifyNumbers.Run

# 逐招拍验收图（-move 接原版招式名）
Builds/Windows/SpaceFighter.exe -screen-width 540 -screen-height 960 \
  -shots -boss -laser -move wallGap -tag mv_wallgap -autoshot
```

改任何一条差异时，请同时更新本文件和 `VerifyNumbers` 里对应的断言，
否则下一个会话会把它当成回归。
