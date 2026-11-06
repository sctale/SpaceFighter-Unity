# v0.21.12 — 左上分数框版式重排

重新排列左上角分数框，解决 BEST / GRAZE 与分数挤在一起、层级不清的问题。

## 版式

- 固定为三层：`◆ SCORE` 眉标 → 大分数 → 细分隔线 → `BEST / GRAZE` 双列。
- 双列统一「标签在上、数值在下」：左列 BEST（金色）、右列 GRAZE（青色、右对齐），
  两列共用同一基线，中间加 1px 竖分隔线。
- 面板 306×148 → 320×152；分隔线从标签行上方移到 `topPad+119`，不再穿过标签。
- 标签字号 12 → 14，颜色改为与数值同色系的低透明度金/青，不再一律灰色。

## 验收

- 540×960 与 1080×1920 实拍：`docs/shots/auto_v02112c3_3.png`、
  `docs/shots/auto_v02112h3_3.png`；确认三层对齐、右列右对齐、分隔线不压字。
- 数值门禁 `VerifyNumbers`：**446/446**。
- Windows 构建 `BUILD_RESULT=Succeeded errors=0`。
- Android：`ExportAndroidProject` → 清理损坏的 `local_aar_for_lint` 缓存 →
  `assembleRelease`，**BUILD SUCCESSFUL in 33s**。

## APK

- 文件名：`AuroraRift-Unity-v0.21.12.apk`（项目根目录，同时上传 GitHub Release）
- 大小：**29,381,882** 字节
- versionName **0.21.12** / versionCode **40**
- 清单：`com.sctale.spacefighter`，minSdk 25 / targetSdk 37 / compileSdk 37，
  launchable-activity `com.unity3d.player.UnityPlayerGameActivity`
- 权限：`android.permission.VIBRATE`
- 签名：`apksigner verify` → **Verifies**（v2），`C=US, O=Android, CN=Android Debug`，
  证书 SHA-256 `629583f74bb10fefb0874ac95e5031153cc89b48011b208bffb02e344e3c2f7c`
- APK sha256：`ac2dd03ffa10f409bcefc8bf62962cf3a6258ca5b328078a814a4bff4eb69ddb`
