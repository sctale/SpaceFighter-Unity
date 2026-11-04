# v0.20.2 — 三条武器线各自一艘飞船

上一版三种武器线是同一外形换色；这一版补上两艘，三条线轮廓完全不同：

| 线 | 外形 | 来源 |
|---|---|---|
| 脉冲 | 双炮长机身截击机 | Polyfork `interceptor` |
| 棱镜 | 宽三角翼 + 长枪机头 | 程序化低模（`build_player_prism`） |
| 蜂群 | 双舱炮艇 + 发光弹舱 | 程序化低模（`build_player_swarm`） |

后两艘走 `gen_art.py` 的 `LOWPOLY_STYLE`（哑光 0.80、Specular 0.15、曝光 −0.30、
key/rim 0.8/0.4），与资源库那艘同一套平面语言；渲染后仍过 `fit_to_contract` /
`_mask` / 外发光 / `check_art`，`ShipArt` 契约与 C# 不动。

三视图与实战尺寸对照：`docs/shots/art_three_ships.jpg`。

## 验收

- 门禁 434 条全过；`check_art` 2/2（新渲的两艘）。
- 安卓真机 1080×1920：安装开打，`logcat` 中 `E/Unity` = 0。
- 包体 28,553,296 → **28,532,048** 字节。
