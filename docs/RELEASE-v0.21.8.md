# v0.21.8 — 整项目代码审查修复批次

对 Unity 版做了一轮全量代码审查（以 `web/www/js` 网页原版为行为基准），修复 10 项玩法正确性、
暂停语义、HUD 数据一致性与性能问题。无新增玩法内容。

## 修复内容

### 玩法正确性

- **贯穿弹重复扣血**：`Projectile` 新增已命中目标集合，`Director` 对同一敌机只结算一次，
  对齐网页版 `Bullet.hitSet`（entities.js:45 / game.js:827-833）。棱镜与僚机 beam 停在敌机体内
  不再逐帧连击，贯穿武器的实际 DPS 回到原版水平。
- **暂停不冻结弹幕**：`BulletPool` 在 `Paused/Menu` 状态跳过 `Tick`，暂停期间敌弹不再继续飞、
  到期或消失；`Dead` 仍推进，保留死亡慢镜。
- **暂停销毁 Boss 光束**：`BossLaser` 暂停时只冻结、不再 `Kill`，恢复后该招剩余节拍仍有光束。
- **弹池重复释放**：`BulletPool.Release` 先确认弹体仍在活动表，避免 `TrimEnemyBullets` 在
  Burst 链中提前释放后被二次压入空闲栈、同一实例被两次 `Spawn` 复用。

### HUD 与持久化

- **静音**：启动时应用已保存的静音状态；`SetMuted` 同时静音 `_drone/_pulse`
  两层音乐，「音乐与音效」开关真正全部生效。
- **结算「最高倍率」**：改用 `Stats.MaxCombo` 换算，不再因死亡前舰体受击清零 `Combo` 而恒显示 ×1。
- **菜单「最远星区」**：新增 `GameRoot.BestZone` 持久化（`aurora.bestzone`），返回基地后不再回落为 01。
- **UiKit.Arc 纹理缓存**：缓存键补上尺寸，清理缓存时 `Destroy` 旧纹理，消除充能环/动作环
  同帧反复重建造成的原生内存上涨。

### 清理

- 删除破防结束时遗留的 `[BOSS-RESUME]` 调试日志。
- 移除未使用的 `Director.EnemyBulletSpeed`、`BossController._hitsAtSpawn`、
  `EnemyShip.MarkCounted/Rot`、`BossController.Rot`、`BuildScript.EnsureUiMaterials/EnsureMat`。

## 验收

- 数值门禁 `VerifyNumbers`：**446/446**。
- Unity `-batchmode -nographics` 编译：**0 error**；`Assembly-CSharp.dll` 正常重建。
- 构建链路：`ExportAndroidProject` → `gradle.properties` 改 `-Xmx4g` →
  Unity 自带 OpenJDK17 + gradle-launcher 8.13 `assembleRelease`，`BUILD SUCCESSFUL in 1m 14s`。

## APK

- 文件名：`AuroraRift-Unity-v0.21.8.apk`（项目根目录，同时上传 GitHub Release）
- 大小：**29,056,716** 字节
- versionName **0.21.8** / versionCode **36**
- 清单：`com.sctale.spacefighter`，minSdk 25 / targetSdk 37 / compileSdk 37，
  launchable-activity `com.unity3d.player.UnityPlayerGameActivity`
- 签名：`apksigner verify --print-certs` → **Verifies**（v2 scheme），
  `C=US, O=Android, CN=Android Debug`，证书 SHA-256
  `629583f74bb10fefb0874ac95e5031153cc89b48011b208bffb02e344e3c2f7c`
- APK sha256：`0049afaf14731bf17238a4ca7cae8b8f5b37ba375df12ccfe49e1d13f2749305`
