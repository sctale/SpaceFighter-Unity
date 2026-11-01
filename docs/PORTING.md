# 太空战斗机 · Aurora Rift → Unity 6 重构对照表

原版：`D:\V-Coding\space-fighter`（Capacitor + HTML5 Canvas，约 9,900 行）
Unity 版：`D:\V-Coding\space-fighter-unity`（Unity 6000.3.10f1 LTS，纯代码搭场景）

## 为什么这么搭

Unity 版**不提交任何手工场景/预制体**：`GameBootstrap` 用
`[RuntimeInitializeOnLoadMethod]` 在运行期建相机、玩家、HUD、弹池、导演、星空，
`BuildScript` 只在构建时生成一个空场景并写进 Build Settings。
好处是 CLI 一条命令就能出包，不需要打开编辑器点按钮：

```bash
Unity.exe -batchmode -quit -projectPath D:/V-Coding/space-fighter-unity \
  -buildTarget Android \
  -executeMethod AuroraRift.EditorTools.BuildScript.BuildAndroid
```

## 模块映射

| 网页版 | Unity 版 | 状态 |
|---|---|---|
| `www/js/core.js`（循环/输入/常量） | `Core/GameRoot.cs` + `Core/GameBootstrap.cs` + `Core/GameConfig.cs` | 已迁移 |
| `www/js/entities.js` 玩家/武器/敌人 | `Entities/PlayerShip.cs`、`Systems/WeaponSystem.cs`、`Entities/EnemyShip.cs`、`Entities/DroneShip.cs` | 部分迁移 |
| `www/js/game.js`（Boss/掉落/结算） | `Systems/BossController.cs`、`Systems/Director.cs` | 部分迁移 |
| `www/js/background.js`（10 星区天空） | `Systems/Starfield.cs` | 简化实现 |
| `www/js/ui.js`（仪表带/HUD） | `UI/Hud.cs` | 简化实现 |
| `www/js/art.js`（程序化美术） | `artgen/gen_ships.py` → `Assets/Resources/Ships/*.png`，运行期由 `Visuals/ShipArt.cs` 载入 | 已替换（舰船/弹体/补给箱） |
| `www/js/director.js`（波次/星区推进） | `Systems/Director.cs` | 简化实现 |
| `.tools/design-test.js`（数值断言） | 待建：Unity EditMode 测试复刻同一批断言 | 未做 |

## 数值对齐（已从原版抄入 `GameConfig`）

| 项 | 原版值 | Unity 值 |
|---|---|---|
| 脉冲线 speed/drain/recover | 1250 / 7.4 / 15.5 | 12.5 / 7.4 / 15.5（世界单位缩放） |
| 棱镜线 | 560 / 10.5 / 12 | 5.6 / 10.5 / 12 |
| 蜂群线 | 830 / 8.6 / 13.5 | 8.3 / 8.6 / 13.5 |
| 失谐威力/射速 | 55% / 下降 | 0.55f / 0.72f |
| 共鸣 | 每层 +8%，最多 5 层 | 同 |
| 玩家判定/擦弹半径 | 4.6px / 28px | 0.46 / 2.8（世界单位） |
| 开局武器 | 脉冲 Lv2，其余 Lv1 未解锁 | 同 |
| 护盾上限/开局 | 3 / 1 | 同 |

## 迁移进度（2026-11-01 第十二轮 · v0.15.0 舰船美术专项）

### 已完成（有产物/截图/门禁证据）

| 系统 | 实现 | 验证方式 |
|---|---|---|
| 12 种敌机 | `EnemyDefs` 数据表（hp/score/尺寸/移动/开火/冷却/掉率） | `VerifyNumbers` 24 条断言 |
| 3 种精英词缀 | 装甲/急速/爆裂（第 3 星区起 22%） | 实际对局截图 |
| Boss 22 招 | `BossMoves` + 三台 Boss × 三阶段固定序列 | 22 枚枚举 + 阶段映射断言 |
| 三档难度 | 6 组系数（弹速/Boss 血/星爆/得分） | 18 条断言 |
| 武器三线 | 蓄能/失谐/共鸣/换线，5 级 | 12 条断言 |
| 掉落与保底 | 4 类补给箱 + 连续 3 次未出缺失项强制补 | 实机验证 |
| 三档弹幕密度 | 每 5 星区一档，影响齐射与波次目标 | 代码 + 截图 |
| 僚机 | 寻的/重炮/支援三类，翼位阻尼跟随 | 截图 |
| 力场 | Q/右键，8 秒吞弹 + 抵消一次致命 | 实机 |
| 结算 | S/A/B/C/D 评级 + 基础/时间/无伤/擦弹明细 | 截图 |
| 10 套星区天空 | 巨行星/环带/极光/裂隙/岩带/恒星/双月/彗星/光纱/核心弧环 | zone1/2/3/6 截图 |
| 音频 | 双层程序化音轨 + 5 类音效（零素材） | 运行 |
| HUD | IMGUI，安全区适配，Boss 血条/预告/结算/精准环 | 多张截图 |
| 设置 | 轻柔特效 / 高对比敌弹（持久化） | 代码 |
| 手感 | 开火火光、受击震动、击破爆发、阶段冲击波 | 代码 + 截图 |
| **舰船美术 v0.15** | 玩家三线涂装、12 敌机、3 僚机、3 Boss 各一套独立机体；金属渐变/倒角/装甲刻线/发光引擎 | `artgen/preview.png` + 1080×1920 实机截图 |
| **Boss 三层结构** | 本体 + 独立旋转环 + 呼吸光环/核心辉光；受击只闪外圈，不洗掉涂装 | `auto_bz1/4/7` 截图 |
| **弹体/补给箱** | 7 类弹体精灵（追踪弹/重炮/水雷/针弹…）+ 4 类补给箱图标 | 实机截图 |
| **安卓中文 HUD** | 随包带 Noto Sans CJK SC 子集（942 字形 / 274KB），修复 v0.14.0 安卓上 IMGUI 中文整段空白 | 模拟器实机截图 |

门禁：`VerifyNumbers` **278 条断言全过**；构建：Windows + Android APK 均 CLI 产出并通过 `aapt2`/`apksigner` 校验。

### v0.16.0 对标清单（网页版 v2.8.4）

| 域 | 网页版 | Unity 版现状 |
|---|---|---|
| 比例 | 画布固定 540 宽，`px→世界` 由画布换算 | ✅ `Px = 10/540`；`GameView` 实时供给所有边界 |
| 视口 | 移动区 `x∈[31,509]`、`y∈[H*0.18, H-104]` | ✅ 同上，含 20:9 长屏 |
| 星区节奏 | `buildScript()` 12~17 条剧本 → clearing → Boss | ✅ 逐条移植，含 55% 中期补给 |
| 敌机/精英/词缀 | 12 种 + 精英门槛 + 3 词缀 | ✅ 同名同参数 |
| Boss 招式 | 22 招 + 分时 `at()` + 光束实体 + idleFire | ✅ `BossMoves` + `BossLaser` + `Pending` 队列 |
| 开局 | 护盾 1、无僚机、脉冲 2 级 | ✅ 并进门禁 |
| 掉落 | 双权重表 + 保底 + 换线箱 | ✅ |
| 界面 | 顶栏/仪表带/动作环/暂停/结算 | ✅ IMGUI 四层，热区与绘制同源 |
| 暂停 | 失焦/切后台自动暂停并清输入 | ✅ `OnApplicationPause/Focus` |
| 震动/触感 | `haptic([...])`（Capacitor） | ❌ 未接（Unity 需 Android 震动插件，暂缺） |
| 背景视差 | `background.js` 分层视差 | ⚠️ 各星区天空已区分主题，逐层视差未复刻 |

### 舰船美术管线（v0.15.0 新增）

- 源脚本：`artgen/gen_ships.py`，CPU 光栅化（3–4× 超采样 + 面积降采样），输出到
  `Assets/Resources/Ships/`，同时生成 `_mask.png` 供受击白边使用。
- 预览：`python artgen/preview.py` → `artgen/preview.png`（暗底 1× / 大图细节 / 游戏尺寸）。
- 设计语言：深色底甲 + 顶光金属渐变 + 倒角高光 + 发光座舱/引擎/能量线；
  轮廓线设有“最终显示像素下限”，避免高分辨率图缩到 100px 后线条消失。
- 调色板：玩家三线 teal/rose/gold；敌机沿用 `EnemyDefs.Tint` 的色系（玫红/紫/钢蓝/青/金）。
- 运行期：`ShipArt.Get(name, worldHeight)` 按“画布世界高度”建 Sprite，
  `ForRadius()` 保证视觉外缘与判定半径一致；找不到资源时自动回退 `RuntimeSprites`。
- 导入：`Editor/ShipArtImporter.cs` 对 `Resources/Ships` 关压缩/关 mipmap，避免安卓色阶。
- HUD 字体：`Resources/Fonts/AuroraCJK.ttf`（Noto Sans CJK SC 子集，OFL 1.1）。
  原因：安卓 `Font.CreateDynamicFontFromOSFont` 只有 Roboto，v0.14.0 的中文 HUD 在手机上整段不渲染；
  现在优先 `Resources.Load<Font>`，找不到才回退系统字体。改 UI 文案后要重新跑
  `pyftsubset`（见 `artgen/fontwork/chars.txt` 的生成方式）或换回完整字体。
- 截图验收：`SpaceFighter.exe -shots -autoshot -zone N [-boss] -tag xxx`，
  输出 `docs/shots/auto_xxx_*.png`；`-boss` 直接进 Boss 战。

### 仍未完成

1. **MCP 工具级调用**（非代码问题）  
   已证：桥接 `127.0.0.1:6400` 监听、Python 服务端握手、`tools/list` 30 个工具、
   `manage_tools activate` 在编辑器内执行成功。  
   未证：其它工具的实际执行——它们默认关闭，开关存 `EditorPrefs`，而**批处理模式的 EditorPrefs 不落盘**
   （实测注册表零条目），只能从 GUI 打开一次。GUI 首次启动需要接受
   `Unity Editor Software Terms`（法律协议，需本人操作）。  
   步骤：双击 `打开Unity工程(含MCP桥接).cmd` → 勾选同意 → `Accept and Continue` →
   编辑器加载后 Qoder 里 `/mcp reload` → 我即可读取场景层级。

2. **背景/星空仍是简化实现**  
   10 套星区天空已经能区分主题，但原版 `background.js` 的巨行星环带、光纱等逐层视差尚未复刻；
   舰船已换成独立精灵，后续可继续补背景层。


## 构建注意（踩过的）

- **不要**用 `SerializedObject` 改 `ProjectSettings/GraphicsSettings.asset`：会让之后每次 `BuildPipeline.BuildPlayer` 原生崩溃。
  恢复办法是把该文件移走让 Unity 重建默认值。
- 删过 `Library` 之后，`Builds/AndroidProject` 里的 IL2CPP 生成代码会与新的 libil2cpp 冲突（`ld.lld: duplicate symbol`），
  必须整目录删掉重新导出再跑 Gradle。
- 构建脚本要**校验产物版本号**再改名，否则会拿旧包冒充新版本（本轮真的发生过）。
- **`Strip Engine Code`（`PlayerSettings.stripEngineCode`）必须关**：开着的话原生 `MonoBehaviour`(class 115)
  会被剥离，之后每次 `AddComponent` / 脚本实例化 / IMGUI 画字都报
  `Could not produce class with ID 115`，`GUI.DoSetSkin` 每帧抛 `NullReferenceException`——
  表现就是**安卓上 HUD 整层不画**，桌面端完全正常（平台差异，极易漏测）。
  实测 `link.xml` 保 `MonoScript` 无效，只能关裁剪；代价约 +6.4MB 包体。
- `il2cpp.exe` 会往系统临时目录写文件，那台机器上 `%LOCALAPPDATA%\Temp` 可能被拒；
  Gradle 前置里要把 `TMP/TEMP` 指到工程内干净目录，否则报 `UnauthorizedAccessException`。
- 每次 `ExportAndroidProject` 都会重新生成 `Builds/AndroidProject/gradle.properties`，
  里面的 `org.gradle.jvmargs` 又变回 `-Xmx0M`（headless 下 Unity 算出来的堆是 0），
  必须在导出后、跑 Gradle 前改回 `-Xmx4g -Dfile.encoding=UTF-8`。

## 环境前提（本机）

- 编辑器：`C:\Program Files\Unity\Hub\Editor\6000.3.10f1`
- Android：JDK17 用 Unity 自带 `AndroidPlayer/OpenJDK`；SDK 复用 `%LOCALAPPDATA%\Android\Sdk`；
  NDK **必须 r27c = 27.2.12479018**（Unity 6.3 强制校验，27.1 会被拒绝）
- 许可证：Unity Personal（Hub 登录后落在 `%LOCALAPPDATA%\Unity\licenses\`）
- headless 跑之前**先关 Unity Hub**，否则授权客户端互斥锁会让 Editor 崩溃

## 构建与 MCP 的实测结论（2026-11-01）

- CLI 构建：Windows 直出 exe；Android 必须「导出 Gradle 工程 → 改 `gradle.properties` 的 `-Xmx0M`
  → 设置 `TMP/TEMP` 到干净目录 → 用 Unity 自带 gradle-launcher 跑 `assembleRelease`」。
  产物 20.8MB，`aapt2` 验包名 `com.sctale.spacefighter`，`apksigner` 验到 Android Debug 证书。
- MCP：UPM 包 + `uvx mcpforunityserver` 已打通 JSON-RPC（initialize / tools/list / tools/call）。
  `manage_tools activate core` 在编辑器内执行成功；但 batchmode 下多数工具仍报 "disabled"，
  要真正操作场景需用 GUI 编辑器（双击根目录 `打开Unity工程(含MCP桥接).cmd`，且不要以管理员运行）。
- 桥接端口固定 `127.0.0.1:6400`，由 `-executeMethod MCPForUnity.Editor.McpCiBoot.StartStdioForCi` 拉起。
