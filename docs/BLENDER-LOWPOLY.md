# Blender 操作说明（本机实测）

> **先选路**：要「游戏里实时转的 3D 模型」→ 走 [路径 A](#路径-a低模--unity-实时资产)；要「把 3D 渲成 2D 精灵替换贴图」
> → 走 [路径 B](#路径-b硬表面建模--渲染成-2d-精灵当前游戏实际用法)。**当前游戏用的是 B**，A 是早期验证过的另一条管线，保留备查。
>
> 两条路共用本机环境，但**渲染器和参数完全不同**，别互相套用。

## 本机事实（两条路共用）

- 版本：Blender **4.5.15 LTS**（build 2026-10-22，hash `5afeabba6097`）
- 安装位置：`D:\Program Files\Blender Foundation\Blender 4.5`（官方 ZIP 便携版，免 UAC）
- 完整性：官方 `blender-4.5.15.sha256` 校验通过
- 启动方式：开始菜单 `Blender 4.5`；命令行 `blender`（`~/bin` 下的 shim，`.cmd` 指向上面那个 exe）
- GPU：NVIDIA GTX 1660；Cycles 的 OptiX / CUDA 均已可用
- 自带 Python：3.11（**没有 PIL**，见 B7 第 5 条）
- 无头模式下 `gpu.platform` 查询会报 "GPU functions for drawing are not available in background mode"，属正常；能出图即证明上下文可用。

**渲染器实测（2026-11-03，128px 立方体探针）**

| 引擎 | 首次出图 | 结论 |
|---|---|---|
| `BLENDER_EEVEE_NEXT` | 14.74s | 能用，但首次要编译着色器，批渲不划算 |
| `CYCLES`（OptiX GPU，16 采样） | **0.61s** | 路径 B 用它：42 张精灵批量重渲约 4 分钟 |

---

## 路径 A：低模 → Unity 实时资产

> 这一节是早期低模验证的原始记录，产物在 `D:\V-Coding\.blender-setup\`，**不在本仓库内**，也没进游戏。

### A1 安装与产物

- 样例脚本与产物：`D:\V-Coding\.blender-setup\`
  - `make_drone.py`：程序化低模舰船 → FBX/GLB + 双视角预览
  - `out\SF_Drone_LowPoly.blend`：可继续编辑的源文件
  - `out\SF_Drone_preview_front.png` / `_rear.png`
  - `UnityImportTest\`：独立 Unity 导入验证工程（未污染本工程）

### A2 Low Poly 规范

- 平面着色（Shade Flat），**不加 Bevel/Subdivision**
- 小型舰船目标：**100–300 tris**；样例 `SF_Drone_LowPoly` = 85 verts / 63 polys / 134 tris
- 硬边、块面清晰；发光推进器用独立材质而不是后期 Bloom
- 统一三材质槽：`Hull`（深色装甲）、`Trim`（青色结构）、`Glow`（自发光）
- 必须 UV（Smart Project 即可），Unity 侧样例 UV 与顶点数一致

### A3 导出（Unity 原生 FBX）

```python
bpy.ops.export_scene.fbx(
    filepath=..., use_selection=True, object_types={"MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y",
    mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False,
)
```

同时导出 GLB（`export_format="GLB"`, `export_yup=True`），给使用 glTFast 的场景备用。

### A4 Unity 导入行为（实测）

- Unity 6（6000.3.10f1）导入后，根节点自动带 **-90°X 旋转**（Euler 270）
- 世界包围盒 `(3.240, 0.800, 2.830)`：飞船平躺，高度 0.8，**机头朝 -Z**
- 网格：260 verts / 134 tris / 3 submeshes / 3 materials（Hull、Trim、Glow）
- 说明：FBX 的本地网格轴向仍是 Blender 的 (X, Y, Z)，根节点的旋转负责转成 Unity 的 Y-up；摆放时不要再手动转一次

### A5 无头命令

```bash
blender --version

# 生成样例资产 + EEVEE 预览
blender -b -P D:\V-Coding\.blender-setup\make_drone.py

# 回读校验 FBX/GLB
blender -b --factory-startup -P D:\V-Coding\.blender-setup\reimport.py

# Unity 导入断言（独立临时工程）
"C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath D:\V-Coding\.blender-setup\UnityImportTest ^
  -executeMethod ImportProbe.Run -logFile D:\V-Coding\.blender-setup\unity-import.log
```

---

## 路径 B：硬表面建模 → 渲染成 2D 精灵（当前游戏实际用法）

游戏是 2D 弹幕，但机体要「看起来是真 3D」。做法：**在 Blender 里按真机体建模，
正交顶视渲成透明 PNG，原位替换 `Assets/Resources/Ships/` 的精灵**。
战斗管线、碰撞判定、包体结构全不动——所以 C# 一行没改。

### B1 管线文件

| 文件 | 作用 |
|---|---|
| `artgen/blender/gen_art.py` | 主入口：建场景 + 建模型 + Cycles 渲 beauty / mask / ring |
| `artgen/blender/post_glow.py` | 给渲好的 PNG 烘同色外发光（PIL，**系统 python 跑**） |
| `artgen/blender/check_art.py` | 契约验收：画布/居中/半径比/占比/mask IoU，不过就非零退出 |
| `artgen/gen_icon.py` | 应用图标（读 `Ships/player_pulse.png` 当前景） |

`gen_art.py` 内部分四段：场景装置 → 材质 → 造型原语 → 机体/敌机/Boss/小件。
模型全部由代码构造，没有手工 .blend，改参数即可重渲。

### B2 命令

```bash
# 看有哪些资产
blender --background --factory-startup --python artgen/blender/gen_art.py -- --list

# 渲指定几张（--samples 默认 128；批渲 80 足够，一张 512² 约 5-8 秒）
blender --background --factory-startup --python artgen/blender/gen_art.py -- \
  --only player_pulse,boss_weaver,enemy_carrier --out artgen/blender/out/batch --samples 80

# 烘外发光（必须在渲完之后、验收之前）
python artgen/blender/post_glow.py --dir artgen/blender/out/batch

# 契约验收
python artgen/blender/check_art.py --dir artgen/blender/out/batch

# 图标（改了机体就要重跑，它直接读游戏里的玩家机贴图）
python artgen/gen_icon.py
```

**全量重渲（38 张，约 4 分钟）**：`--only` 后面接
`player_pulse,player_prism,player_swarm,boss_gatekeeper,boss_weaver,boss_helios,`
`enemy_drifter,enemy_wasp,enemy_lancer,enemy_orbiter,enemy_splitter,enemy_bulwark,enemy_rock,`
`enemy_sniper,enemy_minekeeper,enemy_sidewinder,enemy_carrier,enemy_shardling,`
`bullet_player_bolt,bullet_player_lance,bullet_player_micro,bullet_enemy_needle,bullet_enemy_orb,`
`bullet_enemy_heavy,bullet_enemy_mine,drone_seeker,drone_cannon,drone_support,drone_swarm,`
`crate_weapon,crate_shield,crate_drone,crate_prism,crate_swarm,crate_reflect,crate_life,crate_gem,crate_swap`

**替换进游戏**：只覆盖同名文件（`.meta` 与 guid 必须保住，否则场景/代码里的引用会断）：

```bash
for f in artgen/blender/out/batch/*.png; do
  b=$(basename "$f"); [ -f "Assets/Resources/Ships/$b" ] && cp "$f" "Assets/Resources/Ships/$b"
done
```

### B3 场景装置（数值照抄即可）

- **相机**：`ORTHO`，`ortho_scale = 2.0`（视野 [-1,1]），位置 `(0, 0, 8)`，旋转 0 → 沿 -Z 俯视 XY 平面。
  正交是硬要求：透视会让转向/对齐随位置变化，弹幕游戏没法用。
- **四盏灯**（都是 Area，单位 W）：

| 灯 | 位置 | 尺寸 | 能量 | 颜色 | 作用 |
|---|---|---|---|---|---|
| Key | (1.1, 1.5, 3.2) | 2.2×2.2 | 170 | (1.00, 0.96, 0.90) | 局部金属高光 + 硬阴影 |
| Fill | (-2.2, -1.6, 2.2) | 4.0×4.0 | 22 | (0.55, 0.68, 1.00) | 只把阴影提离死黑 |
| Rim | (2.8, -0.2, 0.5) | 2.4×0.6 | 420 | (0.42, 0.55, 1.00) | 勾霓虹边，硬表面质感主要靠它 |
| Under | (-0.6, -2.6, 0.8) | 2.2×0.6 | 120 | (0.35, 0.85, 0.95) | 把机体边缘从深色背景里托出来 |

- **世界**：底色 `(0.012, 0.018, 0.035)`，`strength = 0.12`（只影响反射，不影响透明底）。
- **渲染**：Cycles GPU（OptiX，失败退 CUDA/CPU）、80–128 采样、OIDN 降噪、
  `film_transparent = True`、PNG RGBA 8bit、`view_transform = 'Standard'`。
  **不要用 AgX/Filmic**：会把霓虹洗成灰白，颜色全对不上游戏配色。

### B4 造型与材质

- **核心原语 `poly_prism(name, pts2d, z0, z1, mat, bevel, loc, rot_z)`**：任意 2D 轮廓沿 Z 挤出 +
  Bevel（2 段、限角）。机翼、六边形要塞、弹体、补给箱全用它。
- 派生：`box` / `cyl` / `arc_ring`（环带，Boss 旋转环层）/ `_octagon` / `_star` / `_lens`（弹体）/
  `flip_180`（整组绕世界 Z 转 180°，见 B6 第 3 条）。
- **材质**（Principled）：`hull` 金属 0.95 / 粗糙 0.22；`armor` 0.90 / 0.32；`dark` 0.70 / 0.55；
  `glass` 近黑 + 粗糙 0.03；**`neon` 用纯 Emission 节点**（不是 Principled 的 Emission 口，见 B6 第 1 条）。
- 敌机/僚机是**数据驱动**：`ENEMY_SPECS[kind]["body"]` 是一串零件
  （`("poly", pts, z0, z1, mat)` / `("box", size, loc, mat, bevel)` / `("cyl", r, d, loc, mat[, verts|rot])` /
  `("ring", r0, r1, a0, a1, z0, z1, mat)`），加个新机种只要往表里加一段。

### B5 输出契约（破了游戏里就会错位）

Unity 侧 `ShipArt.cs` 的约定：`Resources.Load("Ships/<name>")` → `ppu = tex.height / worldHeight`
→ pivot 居中 → `ForRadius()` 按 `ArtExtent = 0.46` 反推尺寸。所以：

| 项 | 要求 |
|---|---|
| 画布 | 玩家 512²、敌机 384²、Boss 640²、僚机/补给箱 192²、弹体 96²（重弹/水雷 128²、护盾 256²） |
| 主体 | 几何中心对齐画布中心，**外接半径 = 0.46 × 边长** |
| 朝向 | 玩家机/僚机/玩家弹 **机头 +Y（向上）**；敌机与 Boss **机头 -Y（向下）** |
| 透明 | straight alpha（Blender 默认），`film_transparent`，不要预乘 |
| 附加层 | 每张 ship 必须有 `_mask`（同尺寸同 pivot 的纯白剪影，受击闪白用）；Boss 另有 `_ring` / `_ring_mask` |
| 文件名 | 与现有完全一致；**不要删 `.meta`**（guid 变了 Unity 就丢引用） |

> 造型比例只能靠调模型参数解决。**禁止**改 `ShipArt.ArtExtent` 或画布尺寸去迁就某个模型。

### B6 六条硬教训（都是真渲过才暴露的）

1. **霓虹不能用 Principled 的自发光口。**
   420W 轮廓光在发光条上的镜面反射会把颜色打到 1.0，整条霓虹变纯白、色相全丢
   ——第一版 Boss 的玫瑰环就是这么变成白圈的。
   **修法**：`mat_emit()` 用纯 Emission 节点（对布光免疫），强度压到 `max(color)×strength ≈ 1.1–1.4`。
2. **几何包围盒 ≠ 视觉外接框。**
   薄片、发光边、隐藏件会让两者差好几像素（实测偏 9px、半径 0.484）。
   **修法**：`fit_to_contract()` —— 先渲 160px 小样，量 alpha 外接框中心与最大半径，再反推缩放/位移。
   有了这一步，38 张图一次过契约，不用手调。
3. **玩家朝上、敌机朝下，写反会整机颠倒。**
   第一批 prism/swarm 就是按敌机轮廓写的，渲出来上下颠倒。
   **修法**：`flip_180(objs)` 绕世界 Z 转 180°（对称机体等价于镜像）。
4. **外发光会撑大判定轮廓。**
   光晕 alpha 若超过 0.5，会和机体边缘合成出更大的不透明区域，视觉半径超出 0.46 判定圈。
   **修法**：`post_glow.py` 把光晕 alpha 上限压在 **0.45**；`check_art` 用 alpha>0.5 判轮廓时就不会被带偏。
5. **Blender 自带 Python 没有 PIL。**
   发光烘焙、拼图、检查全要用 PIL，所以必须放**系统 python**（`post_glow.py` / `check_art.py`），
   别在 `gen_art.py` 里 `import PIL`——会直接 ModuleNotFoundError。
6. **验收容差要和旧图对齐，不要照理论值卡。**
   旧手绘图实测：半径 0.51、居中偏 ±14px（不是理想的 0.46/±1px）。
   阈值定成 **半径比 [0.42, 0.52]、居中 ≤ 2.5% 画布**，否则新图全红、而"修回旧图"反而更差。

### B7 验收（`check_art.py`）

逐张断言，任一不过 → 退出码 1：

- 画布尺寸符合档位（含 heavy/mine=128、shield=256 的例外表）
- alpha 外接框中心偏差 ≤ 2.5% 画布
- 外接半径 / 画布边长 ∈ [0.42, 0.52]（目标 0.46）
- 有效不透明像素占比 ≥ 3%（防空图）
- `_mask` 与 beauty 的 alpha 形状 IoU ≥ 0.95（低了说明闪白会露边）

当前状态：**38 张精灵全部通过**。

### B8 排查清单

| 现象 | 先查 |
|---|---|
| 图是全透明的空图 | 模型跑出 [-1,1] 视锥；看日志有没有 `FIT_SKIP empty` |
| 整张发白 | 发光强度过高，或用 Principled 自发光被灯打到 1.0 |
| 上下颠倒 | `flip_180` 漏了（玩家/敌机朝向不同） |
| 尺寸偏大/偏小 | 小样被裁（几何超出视锥），fit 量到的是被裁后的框 |
| mask 与 beauty 对不上 | mask 必须渲「几何」；beauty 上的提亮/发光不能进 mask |
| Unity 里图没换 | 文件名是否一致、`.meta` 是否还在；看 logcat 有无 `missing ship art` |
| 图标看着没变 | Gradle 资源收缩会把 `res/` 名字混淆，别按文件名找；用 `aapt2 dump badging \| grep application-icon`，指向 `.xml` 说明自适应图标生效 |
| 安卓 HUD 全黑 | 不是美术问题，是 `PlayerSettings.stripEngineCode` 被打开了（见 `docs/PORTING.md`） |

### B9 资产清单（38 张 + 4 张光效 + 图标）

| 类别 | 张数 | 画布 | 说明 |
|---|---|---|---|
| 玩家三线 | 3 | 512 | pulse / prism / swarm，各有配色与翼型 |
| 敌机 | 12 | 384 | 剪影按机种区分，40–60px 下可辨 |
| Boss | 3(+3 ring) | 640 | 要塞 / 刀翼 / 核心，环层单独旋转 |
| 弹体 | 7 | 96/128 | 小件原则：主体自发光 + 极窄暗边 |
| 僚机 | 4 | 192 | gunner / lance / aegis / swarm |
| 补给箱 | 9 | 192 | 六边形胶囊 + 自发光图徽，按原 `TintOf` 配色 |
| 加性光效 | 4 | 96–256 | `bullet_flame` / `bullet_glow_soft` / `bullet_shield` / `bullet_aura_ring`，**保持手绘**：柔边就是设计意图，渲成 3D 反而死板 |

图标：`artgen/gen_icon.py` 读当前玩家机，缩放 0.60N、单独提亮 16%（暗装甲在暗底板上会像"挖了个洞"），
再叠到既有底板（暗场 + 青色菱形框 + 金色核心）上，输出 `Assets/Icons/icon_{1024,bg_1024,fg_1024}.png`；
安卓自适应图标由 `BuildScript.PatchAdaptiveIcon()` 在出包时写入。

---

## 两条路怎么选

| 想要 | 走 | 因为 |
|---|---|---|
| 机体能实时转向、受光、换材质 | A | 真网格进 Unity，代价是重做视觉层与性能验证 |
| 画面精致但玩法/性能不动 | **B（当前）** | 只是贴图替换，判定、包体结构、HUD 全不变 |
| 需要大量同风格小件（弹体/箱子） | B | 数据驱动，加一段 spec 就多一个资产 |

---

## 外部低模资产源：Polyfork（实测 2026-11-04）

> 官方站：https://polyfork.dev · 太空套件：https://polyfork.dev/kit/spaceship-wars-kit-234642
> 定位：低模资产库（**不是 Blender 插件**）。低模全部免费、可商用、免署名；FBX/GLB 直接下，无需账号。
> 它和 A/B 是正交的：资产既可走 A 进 Unity 实时渲染，也可走 B 渲成 2D 精灵。

### 价格与授权

| 档位 | 价格 | 说明 |
|---|---|---|
| Free | $0 | 2,978 个低模全部免费；FBX / GLB / USDZ / OBJ；可商用、免署名；可整套下载 kit |
| Pro | $15/月；$100 买断（限 200 席，原价 $125） | 解锁 HD/PBR 版、自有 CDN、remix 烘焙等；**Low Poly 管线用不到** |
| Platform | $299/月起 | 把资产托管给自己产品的用户时才需要；游戏内打包资产不属于此类 |

授权要点：

- 可以：商用、修改、放进游戏/客户端作品，免署名，永久。
- 不可以：把原始资产单独转售或再分发（含上传素材站、网盘、模板库），或用它训练商业 3D 资产生成器。
- 游戏可以随包携带模型；**原始 FBX/GLB 不要作为独立素材公开分发**。
- 免费资产的 `.mjs` 源码需要免费账号；FBX / GLB / CDN 无需账号。HD 版需要 Pro key。

### 太空套件实测

- `Spaceship Wars Kit`：**29 个部件，全部免费**，480–4,320 tris；统一调色板与米制比例。
- 舰船：Scout 3,418 / Interceptor 3,570 / Alien Raider 3,632 / Gunship 3,638 / Agile Starfighter 3,707 / Heavy Bomber 3,826 / Cargo Freighter 4,310 / Capital Cruiser 4,320 tris。
- 小件：Space Mine 480 / Fuel Drum 522 / Asteroid 530–754 / Missile 634 / Defense Turret 636 / Supply Crate 538 / Escape Pod 630 等，适合敌机与场景。
- 多数资产带可动部件（机翼、喷口），FBX 导入 Unity 后是独立子物体，可直接做动画。

**样例 Interceptor（`interceptor-9d2289`）实测**：

- Blender 4.5：3,570 tris，8 个命名材质（hull / trim / plating / recess / steel / glass / lamp / thruster）。
- Unity 6（6000.3.10f1）：21 个网格、6,509 verts、3,570 tris、8 材质、世界包围盒 **5.96 × 2.50 × 9.00 m**。
- **根节点旋转 0（Y-up，机头 +Z）**；这和我们自己从 Blender 导出的 FBX（根节点 -90°X，机头 -Z）不同，两条来源混用时注意朝向。

### 接入当前管线

- **A（实时 3D）**：直接下 FBX。资产是真实米制，9 m 的船在 10 单位宽的战场里缩放约 **0.15–0.2**；一艘船 8 个材质，同屏敌机多时 draw call 偏高——优先把 480–1,500 tris 的小件给敌机，3.4k–4.3k 的舰船给玩家/Boss，或在 Blender/Unity 里合并材质。
- **B（渲 2D 精灵）**：导入 FBX，按 B3 的相机/灯光渲。先按约 0.2 缩放到 `ortho_scale = 2.0` 的视野，再用 `fit_to_contract()` 精调；可动部件先摆好姿态再渲。
- **改色**：Polyfork 默认是红灰帝国配色，与游戏的青紫霓虹不符；FBX 已把顶点色烘成 8 个命名材质，直接改 `hull / trim / plating / ...` 的 Base Color 即可，不需要重做 UV。
- **取用（无需账号）**：

```bash
# 搜索免费太空资产
curl "https://polyfork.dev/api/assets?q=spaceship&free=1&per_page=100"

# 直接下 FBX / GLB
curl -L -o interceptor.fbx https://polyfork.dev/dl/interceptor-9d2289.fbx
curl -L -o interceptor.glb https://polyfork.dev/cdn/interceptor-9d2289.glb
```

- **MCP**：远端 HTTP 端点 `https://polyfork.dev/mcp`，免费资产无需 key；接进 Qoder 后可直接搜资产/拿元数据。接入前先确认 Qoder 的远端 MCP 配置格式。
- **已下载样例**：`D:\V-Coding\.blender-setup\polyfork\interceptor-9d2289.fbx`（仅本机验证用，未进仓库）。
