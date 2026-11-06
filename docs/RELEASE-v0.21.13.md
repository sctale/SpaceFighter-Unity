# v0.21.13 — 战斗 HUD 顶部层级重排

分数整体上移，关卡/星区独立成行，不再与分数抢同一条横线。

## 顶部层级

- 分数框从 `topPad+24` 提到 `topPad+6`；50 号大分数排版顶 `ScoreTop` 62.8 → 40，
  `BEST / GRAZE` 行随之整体上移（`BestTop` 120.8 → 98）。
- 星区从「SECTOR / 01 / 区名」三行居中块改为一条独立的 `360×42` 单行条：
  `◆ SECTOR 01 │ 区名`，放在分数框下方（`topPad+166`），不再和分数同一行。
- Boss 面板从 `top+180.8` 下移到 `top+226`，给星区条留出 18px 呼吸位，避免重叠。
- 分数面板高度 152 → 150，分隔线移到 `topPad+97.5`，双列基线保持 10.4 的墨迹间距。

## UI 走查

- **层级**：分数是唯一主视觉（50 号粗体），星区降为第二行的 360 宽信息条，暂停/连击留在右上。
- **间距**：分数面板 → 星区条 10px，星区条 → Boss 面板 18px；540 与 1080 比例下均无重叠。
- **对比**：SECTOR 眉标用低亮青、关卡号用亮青 26 号粗体、区名用 72% 墨白，三级可读。
- **安全区**：面板顶只比安全区多 6px，不再被状态栏/刘海压住。

## 验收

- 540×960 / 1080×1920 实拍：`docs/shots/auto_v02113c_3.png`、
  `docs/shots/auto_v02113h_3.png`。
- Boss 战实拍：星区条位于 Boss 面板上方，18px 间距无重叠。
- 数值门禁 `VerifyNumbers`：**446/446**。
- Windows 构建 `BUILD_RESULT=Succeeded errors=0`。
- Android：`ExportAndroidProject` → `assembleRelease`，**BUILD SUCCESSFUL in 59s**。

## APK

- 文件名：`AuroraRift-Unity-v0.21.13.apk`（项目根目录，同时上传 GitHub Release）
- 大小：**29,382,410** 字节
- versionName **0.21.13** / versionCode **41**
- 清单：`com.sctale.spacefighter`，minSdk 25 / targetSdk 37 / compileSdk 37，
  launchable-activity `com.unity3d.player.UnityPlayerGameActivity`
- 权限：`android.permission.VIBRATE`
- 签名：`apksigner verify` → **Verifies**（v2），`C=US, O=Android, CN=Android Debug`，
  证书 SHA-256 `629583f74bb10fefb0874ac95e5031153cc89b48011b208bffb02e344e3c2f7c`
- APK sha256：`eb9066a4f0828ab6aedee1fb842a6b35129fa10430a3a7eb4de338cec6944069`
