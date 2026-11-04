# v0.21.3 — 新应用图标（3/4 英雄视角）

只换桌面/启动器图标：玩法、数值、HUD 与 v0.21.2 完全一致。

## 新图标长什么样

- **主体**：同一架 Polyfork interceptor，抬头 52°、绕 Z 转 160°，从机背后上方看；
  机头朝上、机翼展开、两台尾喷在下方。尾喷对象改成**暖金自发光**，金色只落在引擎上。
- **背景**：深空径向渐变（`#0B1226 → #03050C`）+ 青/紫极光 + 稀疏星点。
- **构图**：一圈细青色瞄准环 + 一圈更淡的紫内环；不再用两层菱形把舰船框成徽章。
- **小尺寸**：舰船占画面 86%，48dp 下仍能看出机翼、机头和两颗金色引擎；
  预览见 `artgen/icon_preview_48.png` / `_96.png` / `_192.png` / `icon_preview.png`。

## 文件与产物

| 文件 | 说明 |
|---|---|
| `Assets/Icons/icon_1024.png` | 1024 母图，构建时 `PlayerSettings.SetIcons(Android)` |
| `Assets/Icons/icon_fg_1024.png` | 自适应前景（透明，缩到 74% 安全区） |
| `Assets/Icons/icon_bg_1024.png` | 自适应背景（满幅） |
| `artgen/icon_src/interceptor_hero_3q.png` | 3/4 透明底源图（已入仓库） |
| `artgen/blender/polyfork_icon.py` | 从 Polyfork FBX 渲 3/4 源图 |
| `artgen/gen_icon.py` | 排版母图 + 自适应层 + 小尺寸预览 |

重跑：

```bash
blender --background --factory-startup --python artgen/blender/polyfork_icon.py
cp artgen/blender/out/icon/hero.png artgen/icon_src/interceptor_hero_3q.png
python artgen/gen_icon.py
```

> 注意：Polyfork 的 FBX 里有一个 2×2×2 的 `Cube` 占位物，俯视时藏在机体内，
> 3/4 视角会从机背顶出来；`polyfork_icon.py` 已把它删掉。

## 验收

- 模拟器桌面实拍：`docs/shots/android_v0213_launcher.png`（圆形遮罩，图标位于 All apps 首行）。
- APK：**29,054,516** 字节；versionName **0.21.3** / versionCode **31**；
  V2 签名 `CN=Android Debug`；sha256 `a367865b1eb9dd44431eacab04c910f05919c20caa8da3d857e8990f518f8ecb`。
