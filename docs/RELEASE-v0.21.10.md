# v0.21.10 — 真粗体字体 + Android 触感震动

补齐 v0.21.9 遗留的最后两项：标题字体与触感震动。

## 字体

- 随包新增 `AuroraCJK-Bold.ttf`（Noto Sans SC Bold 子集，OFL），与 Regular 共用同一份
  1121 字形的 UI 字符集。
- 标题、分数/结算数字、主/次按钮改用真粗体；Regular 仍用于说明文字与小型标签。
- `Hud.Bold()` 优先画真粗体，只有 Bold 字体缺失时才回退到旧的双重叠画。
- 菜单大标题 118 → 110，真粗体下更紧凑；`artgen/gen_font.py` 支持一次生成 Regular + Bold 两套子集。

## 触感震动

- 新增 `Haptics`：Android `VibrationEffect`（API 26+），老系统退回 `vibrate(ms)`。
  轻（冲刺/拾取/力场挡下）、中（护盾破/核心破防）、重（舰体受创/星爆）。
- 暂停页新增「触感震动」开关（默认开，`aurora.haptics` 持久化）。
- Unity 不会自动声明 VIBRATE：新增 `AndroidManifestVibrateHook`
  （`IPostGenerateGradleAndroidProject`），每次导出 Gradle 工程自动补权限。
- `Packages/manifest.json` 补开 `com.unity.modules.androidjni`，保证 `AndroidJavaObject` 可编译。

## 验收

- 540×960 / 1080×1920 实拍：`docs/shots/auto_v02110c_0.png`（菜单真粗体）、
  `auto_v02110c_3.png`（战斗）、`auto_v02110h_0.png`（1080p 菜单）、
  `auto_v02110pause_3.png`（四行设置）、`auto_v02110dead_3.png`（结算）。
- 数值门禁 `VerifyNumbers`：**446/446**。
- Windows 构建 `BUILD_RESULT=Succeeded errors=0`。
- Android：导出后 `launcher/src/main/AndroidManifest.xml` 含 `android.permission.VIBRATE`；
  `assembleRelease` **BUILD SUCCESSFUL in 39s**。

## APK

- 文件名：`AuroraRift-Unity-v0.21.10.apk`（项目根目录，同时上传 GitHub Release）
- 大小：**29,382,814** 字节
- versionName **0.21.10** / versionCode **38**
- 清单：`com.sctale.spacefighter`，minSdk 25 / targetSdk 37 / compileSdk 37，
  launchable-activity `com.unity3d.player.UnityPlayerGameActivity`
- 权限：`android.permission.VIBRATE`（`aapt2 dump permissions` 已确认）
- 签名：`apksigner verify` → **Verifies**（v2），`C=US, O=Android, CN=Android Debug`，
  证书 SHA-256 `629583f74bb10fefb0874ac95e5031153cc89b48011b208bffb02e344e3c2f7c`
- APK sha256：`31c01d9f0301ff77f2a52357018c1c21db0a6fac3bd88dd5e59713f5c8f489e4`
