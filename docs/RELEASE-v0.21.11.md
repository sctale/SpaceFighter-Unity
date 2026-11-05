# v0.21.11 — 修复 Boss 出招被破防打断

破防、阶段切换与临界都不再取消当前招式；已经发射的子弹与光束也不会被清掉。

## 根因

- 破防触发时 `BossController.TakeDamage` 调用了 `ClearEnemyBullets(false)`，
  把玩家正在躲的整屏弹幕直接抹掉，看起来就是攻击被硬切断。
- `Update` 的眩晕分支冻结了「预告 / 招式节拍 / 出招计时」并 `return`，
  1.2 秒破防期间 Boss 完全停手，恢复后这一招的节拍已经错位。
- 破防伤害常常刚好把血量打过 66% / 30%，`EnterPhase` 会立刻取消当前招式、清子弹；
  12% 临界同样会取消。
- 实测探针：破防发生在 Cascade（陨星暴雨）中段时，`casting` 被置 False、
  `eb`（敌弹数）从 85 直接归零。

## 修复

- 破防只保留伤害倍率、提示与轻微震动，不再清弹、不再冻结任何计时。
- 阶段切换与临界只在「招式间隙」执行（`!_casting && _telegraph<=0 && _pending.Count==0`），
  正在放的招式必须走完；`EnterPhase` 不再 `ClearEnemyBullets` / `BossLaser.ClearAll` /
  取消招式，只改变后续招式的强度与弹速。
- 场上已有子弹按原速度继续飞完，破防期间也继续按节拍生成新子弹。

## 验收

- 探针复测（`-boss -laser -move Cascade -break -probe`）：
  `t=6.6 casting=True eb=54` → `t=7.0 破防` → `t=7.1~8.6 casting=True`、
  `eb` 连续增长 `85 → 115 → 139 → 163`；阶段在招式结束后才切到 P2，`eb` 未归零。
- 数值门禁 `VerifyNumbers`：**446/446**。
- Windows 构建 `BUILD_RESULT=Succeeded errors=0`。
- Android：`ExportAndroidProject` → `assembleRelease`，**BUILD SUCCESSFUL in 29s**。

## APK

- 文件名：`AuroraRift-Unity-v0.21.11.apk`（项目根目录，同时上传 GitHub Release）
- 大小：**29,382,314** 字节
- versionName **0.21.11** / versionCode **39**
- 清单：`com.sctale.spacefighter`，minSdk 25 / targetSdk 37 / compileSdk 37，
  launchable-activity `com.unity3d.player.UnityPlayerGameActivity`
- 权限：`android.permission.VIBRATE`
- 签名：`apksigner verify` → **Verifies**（v2），`C=US, O=Android, CN=Android Debug`，
  证书 SHA-256 `629583f74bb10fefb0874ac95e5031153cc89b48011b208bffb02e344e3c2f7c`
- APK sha256：`44fc495cdadcd25e53d3f7e98136a0229ab19e5256661ce1437532f6fbaa3537`
