# v0.21.14 — 分数再上移

分数贴近安全区顶部，星区条与 Boss 面板保持原位。

## 版式

- 分数框 `topPad+6 → topPad+2`；50 号大分数 `ScoreTop` 40 → 26。
- SCORE 眉标移到 `panelY+8`；`BEST/GRAZE` 行 98/124 → 84/110；面板高度 150 → 146。
- 分隔线 97.5 → 82.5，仍保持 10.4 的分数/BEST 墨迹间距。
- 面板底部与星区条之间保留 18px，星区条位置不变。

## 验收

- 540×960 / 1080×1920 实拍：`docs/shots/auto_v02114c_3.png`、
  `docs/shots/auto_v02114h_3.png`。
- 数值门禁 `VerifyNumbers`：**446/446**。
- Windows 构建 `BUILD_RESULT=Succeeded errors=0`。
- Android：`ExportAndroidProject` → `assembleRelease`，**BUILD SUCCESSFUL in 28s**。

## APK

- 文件名：`AuroraRift-Unity-v0.21.14.apk`（项目根目录，同时上传 GitHub Release）
- 大小：**29,382,490** 字节
- versionName **0.21.14** / versionCode **42**
- 清单：`com.sctale.spacefighter`，minSdk 25 / targetSdk 37 / compileSdk 37，
  launchable-activity `com.unity3d.player.UnityPlayerGameActivity`
- 权限：`android.permission.VIBRATE`
- 签名：`apksigner verify` → **Verifies**（v2），`C=US, O=Android, CN=Android Debug`，
  证书 SHA-256 `629583f74bb10fefb0874ac95e5031153cc89b48011b208bffb02e344e3c2f7c`
- APK sha256：`39dfcbf49324fdec7e4c8b5ea1b47d1310f09ee8348a310a8a1d31af65325d43`
