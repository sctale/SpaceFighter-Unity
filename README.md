# 太空战斗机 · Aurora Rift — Unity 6 重制版

把网页版《太空战斗机 · Aurora Rift》用 Unity 6 重做的竖屏弹幕射击。Android APK 优先，桌面版用于快速验证与截图。

<p>
  <img src="docs/shots/android_v0191_play.png" width="290" alt="战斗 HUD（Blender 重制机体与弹体）" />
  <img src="docs/shots/art_small_v0191.jpg" width="290" alt="弹体 / 僚机 / 补给箱图鉴" />
  <img src="docs/shots/art_pilot_compare.jpg" width="290" alt="旧手绘 / 纯 3D / 3D+外发光" />
</p>

## 这一版的重点

- **美术重制（v0.19.0 / v0.19.1）**：38 张精灵改用 Blender 3D 建模 + 正交顶视渲图 + 同色外发光
  ——玩家三线、12 种敌机、3 台 Boss、7 种弹体、4 种僚机、9 种补给箱；文件名/画布/guid 不变，
  `ShipArt` 约定由渲染端自动对齐。管线在 `artgen/blender/`
  （`gen_art.py` / `post_glow.py` / `check_art.py`），可复现、可单张重渲。

- **手感对齐网页版（v0.18.0）**：修掉三个真 bug——蓄能按帧扣（pulse LV1 实际掉电 53/s
  而不是 7.4/s，两秒打空、长期失谐）、换线不清停留计时（共鸣可以连按刷满）、
  通关分重复计算；并补上原版的打击停顿与慢镜。
- **Boss 招式模型重做**：换成原版攻击状态机（0.5 秒预告 → 按时刻分节拍 → 按阶段间隔收招），
  22 招逐条照 `entities.js:2076-2432` 重写，三台 Boss 的阶段序列与「背板」顺序、出招间隔
  0.85/0.62/0.42 秒 + 链招 0.16 秒、弹速阶段系数、椭圆判定、破防与临界全部对齐。
- **关卡平衡对齐**：敌机判定 `size×0.78`、弹道线段扫掠命中、俯冲锁定玩家 ×1.5 速、
  悬停/环绕机 16 秒离场、分裂幼体半血、母舰 55/45 混编、水雷真减速+引信+10 破片、
  通关必掉箱与 4.4 秒幕间、时间奖励按整区用时。
- **界面排列按原版实测值**：页边距、顶/底渐变带（真渐变不是硬边）、Boss 面板内缩、
  仪表带五件套互不挤压、公告与提示位置、结算页单卡垂直居中、武器名按等级换叫法。
- **可回归的数值门禁**：`VerifyNumbers` **425 条**断言，其中一组是**行为断言**
  （蓄能每秒掉电量、共鸣门槛四态、Boss 间隔纯函数、编辑态真建敌机量血量与判定半径）——
  上一版只查常量，所以「常量对、代码没生效」的一类 bug 曾全绿通过。
- **舰船美术全部独立绘制**：玩家三条武器线三套涂装、12 种敌机、3 类僚机、3 台 Boss、7 类弹体、补给箱；金属渐变、装甲倒角、面板刻线、自发光引擎，不再是几何占位图。
- **3 台 Boss 各自独立**：Gatekeeper / Weaver / Helios，本体 + 旋转环 + 呼吸光环 + 核心辉光；受击只闪外圈，持续输出不会把机体洗成灰白。
- **完整玩法**：三条武器线（蓄能 / 失谐 / 共鸣 / 换线）、擦弹与相位穿弹、冲刺、星爆 NOVA、棱镜力场、过载、三档难度、10 个星区、Boss 22 招、破防与临界、僚机编队、掉落保底。
- **手机上全部可点**：精准 / 冲刺 / 星爆三枚动作环 + 武器胶囊换线 + 右上暂停，画得出的都点得到。
- **安卓中文 HUD**：随包带 Noto Sans CJK SC 子集字体（942 字形 / 274KB），修复了旧版手机上 IMGUI 中文整段空白的问题。
- **纯代码搭场景**：仓库不提交场景/预制体，`GameBootstrap` 运行时建相机、玩家、HUD、弹池、导演与星空；CLI 一条命令出包。

## 下载

到 [Releases](https://github.com/sctale/SpaceFighter-Unity/releases/latest) 下载：

- `AuroraRift-Unity-v0.19.1.apk`（Android Debug 签名，安装时允许“未知来源”即可）

桌面版：`Builds/Windows/SpaceFighter.exe`（Unity 构建产物，本地生成）。

更新记录见 [`CHANGELOG.md`](CHANGELOG.md)。

## 操作

| 动作 | 桌面 | 触屏 |
|---|---|---|
| 移动 | 鼠标拖动 / 方向键 / WASD | 按住拖动（抓取偏移 + 抬升，单指锁定） |
| 精准模式 | 按住 `Shift` / `Ctrl` | 按住右下「精准」环 |
| 冲刺穿弹 | `空格` | 点「冲刺」环 |
| 星爆 NOVA（满 100） | `X` / `J` | 点「星爆」环 |
| 切换武器线 | `1` 脉冲 / `2` 棱镜 / `3` 蜂群 | 点「换线」胶囊循环 |
| 棱镜力场（1 护盾，8 秒吞弹 + 挡一次致命） | `Q` / 鼠标右键 | 拾取力场箱自动生效 |
| 暂停 | `P` / `Esc` | 右上角 `II` |
| 重开 | 阵亡后点击 / `R` | 点击 |

调试参数：`-zone N` 指定起始星区；`-boss` 直接进 Boss 战；`-shots -autoshot -tag xxx` 定时截图到 `docs/shots`；
`-dump` 打印当前所有在画的 `SpriteRenderer`（抓"界面上多出个东西"）；`-rebuild` 自检「重新开始」链路。

## 构建

环境：Unity **6000.3.10f1**、Android SDK、NDK **27.2.12479018**、JDK 17（Unity 自带即可）。

```bash
# 数值门禁（425 条断言，构建前必跑）
Unity.exe -batchmode -quit -projectPath <project> \
  -executeMethod AuroraRift.EditorTools.VerifyNumbers.Run

# Windows
Unity.exe -batchmode -quit -projectPath <project> \
  -executeMethod AuroraRift.EditorTools.BuildScript.BuildWindows

# Android：先导出 Gradle 工程，再用本地 Gradle 出 Release APK
Unity.exe -batchmode -quit -projectPath <project> -buildTarget Android \
  -executeMethod AuroraRift.EditorTools.BuildScript.ExportAndroidProject
# 导出后把 Builds/AndroidProject/gradle.properties 里的 -Xmx0M 改成 -Xmx4g，
# 用 Unity 自带 OpenJDK17 + gradle-launcher 跑 assembleRelease。
```

### 重新生成美术

```bash
python artgen/gen_ships.py     # 输出到 Assets/Resources/Ships/*.png
python artgen/preview.py       # 输出 artgen/preview.png 总览图
python artgen/gen_icon.py      # 输出 Assets/Icons/*（构建时进 APK 图标）
```

设计语言、尺寸约定、受击遮罩与导入设置见 [`docs/PORTING.md`](docs/PORTING.md)。

### 重新生成 HUD 中文字体

```bash
# 需要 fonttools；字体源用安卓系统的 NotoSansCJK-Regular.ttc（OFL）
python artgen/gen_font.py <path-to-NotoSansCJK-Regular.ttc>
```

## 目录

```
Assets/Scripts/      # 核心循环、实体、Boss、导演、HUD、视觉层
Assets/Editor/       # CLI 构建、数值门禁、舰船贴图导入规则
Assets/Resources/    # Ships（舰船 PNG）、Icon（应用图标）与字体子集
artgen/              # Python 美术生成器（舰船/图标）+ 字体子集脚本 + 预览图
docs/                # 重构对照表、使用说明、验收截图
web/                 # 网页版 v2.8.4 源码（玩法/数值的对照基准，见 web/INTEGRATED.md）
backups/             # 旧仓库整包备份（本地文件，不进 git）
```

> **一个仓库维护**：网页版已并入 `web/`（原 `sctale/SpaceFighter` 的 v2.8.4 快照）。
> 改玩法时先看 `web/www/js/*.js`，落到 `Assets/Scripts/Core/GameConfig.cs`，
> 再在 `Assets/Editor/VerifyNumbers.cs` 补断言。完整备份（含旧仓库 `.git` 历史）
> 在 `backups/space-fighter-v2.8.4-backup-20261102.zip`。

## 已知限制

- 触感震动未接（网页版走 Capacitor `haptic`，Unity 需要安卓震动插件）。
- 背景/星空仍是各星区独立主题的简化实现，原版 `background.js` 的逐层视差尚未完全复刻。
- 自适应图标只覆盖 `app_icon.xml`（Unity 的清单引用名），圆形图标沿用同一张前景。
- APK 使用 Android Debug 签名，只适合个人/家庭安装；上架应用商店需换正式 keystore。

## License

代码与美术：Apache License 2.0，见 [LICENSE](LICENSE)。
HUD 字体：Noto Sans CJK SC 子集，SIL Open Font License 1.1，见 [Assets/Resources/Fonts/README-FONT.txt](Assets/Resources/Fonts/README-FONT.txt)。
