# v0.21.9 — UI 与图标美术精修

把 `docs/DESIGN-REVIEW.md` 里剩余的视觉项收口，并统一整套界面表面语言。不改玩法数值。

## UI 精修

- **统一切角渐变面板**：新增 `UiKit.SlabGrad`，分数块、Boss 卡、仪表带、菜单战绩卡、
  难度选择、暂停/结算卡、主/次按钮全部换成切角八边形 + 垂直渐变 + 强调色描边。
- **开始菜单**：品牌标记加四向准星刻度；特性芯片加左侧青色刻线；战绩数字放大到 44 号；
  难度选择加 CRUISE / STANDARD / ABYSS 英文副标与选中光带；开始按钮加内高光；
  新增 `READY FOR LAUNCH` 眉标；操作提示改切角键帽卡；修复眉标/副标题/难度说明/页脚
  在居中框里左对齐的问题；页脚上移 10px 避免贴边。
- **战斗 HUD**：分数底板改渐变切角 + 青色顶线；星区两侧加菱形刻度；仪表带增加整条底板，
  舰体/护盾/武器环/进度轨/动作环统一到 58 中心线；动作环字号 21/14 → 18/11，
  触控热区同步下移 4px。
- **公告与受击**：公告入场加 0.10 缩放弹入、强调色光晕与顶线；受击改全屏压暗 +
  边缘泛色 + 一条故障扫描线；低血量暗角改玫红脉冲。
- **暂停 / 结算**：暂停卡改深色渐变切角 + 青色顶线，设置行加 ON/OFF 文本；
  结算卡 980 → 920，分数改 64 号辉光 + 菱形评级章，10 项数据改 52 高紧凑瓦片，
  新纪录改金色胶囊，修复「再次出击」与数据格重叠。
- **星点**：按深度做冷暖色差（远星偏蓝、近星偏暖白）。

## 应用图标

- 背景加星云长云带与四芒星；准星环加 24 个刻度、四段金色弧与四向菱形标记。
- 舰船加投影、青色边缘光、尾喷暖金余晖；1024 / 自适应前景背景 / 48·96·192 预览同步重生成。

## 验收

- 540×960 与 1080×1920 实拍：`docs/shots/auto_v0219c_*.png`（菜单/战斗）、
  `auto_v0219h_*.png`（1080p）、`auto_v0219pause_*.png`、`auto_v0219dead_*.png`。
- 数值门禁 `VerifyNumbers`：**446/446**。
- Unity 编译 0 error；Windows 构建 `BUILD_RESULT=Succeeded`。
- Android：`ExportAndroidProject` → `assembleRelease`，**BUILD SUCCESSFUL in 32s**。

## APK

- 文件名：`AuroraRift-Unity-v0.21.9.apk`（项目根目录，同时上传 GitHub Release）
- 大小：**29,138,764** 字节
- versionName **0.21.9** / versionCode **37**
- 清单：`com.sctale.spacefighter`，minSdk 25 / targetSdk 37 / compileSdk 37，
  launchable-activity `com.unity3d.player.UnityPlayerGameActivity`
- 签名：`apksigner verify --print-certs` → **Verifies**（v2 scheme），
  `C=US, O=Android, CN=Android Debug`，证书 SHA-256
  `629583f74bb10fefb0874ac95e5031153cc89b48011b208bffb02e344e3c2f7c`
- APK sha256：`040673ff05283f3b56811cd2cb3c69277dd04d4d6ab23f3145a85dbb90c3bea8`
