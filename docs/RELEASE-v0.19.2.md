# v0.19.2 — 应用图标换成 3D 玩家机

图标是玩家第一眼看到的东西，之前一直是旧手绘的浅色机体，和现在的 3D 重制版不是一套。

## 做法

- 图标生成器 `artgen/gen_icon.py` 原本就以 `Assets/Resources/Ships/player_pulse.png`
  为前景源，机体在 v0.19.0 换掉后**重跑一次**即可：暗场底板、青色菱形框、紫色内菱形、
  金色核心、星点全部沿用，只有机体变成新的 3D 版。
- 3D 机体的装甲很暗，压在暗底板上缩到 100px 会像"挖了个洞"；因此在图标管线里
  单独给机体提亮 16%（`gen_icon.py` 内部，不影响游戏内精灵）。
- 安卓自适应图标链路不变：`mipmap-anydpi-v26/app_icon.xml` +
  `drawable-nodpi/ic_launcher_{bg,fg}.png`，桌面裁圆后仍是「菱形框 + 机体」。

## 验收

- 真机安装后打开「应用信息」页截图，图标已显示为新图：
  `docs/shots/android_v0192_icon.png`（圆形裁切后的实际效果）。
- 程序内一切不变：图标只影响启动器与安装器，不改任何运行时行为。
- 包体 28,199,024 → **28,179,072** 字节。

## 预览

`artgen/icon_preview.png`（1024）与 `artgen/icon_preview_192.png`（桌面实际尺寸）。
