# v0.21.5 — 左上角分数与 BEST 行分开

## 改了什么

- 左上角 HUD 的 **BEST 行下移**：从 116.4 → **122.4**（相对 1920 参考坐标），
  与 50 号分数框底（114.8）留 **7.6**。
- **GRAZE 行**顺延到 **147.4**；底板高度 138 → **142**，底边 166，
  与 Boss 卡（168.8）仍留 2.8。
- BEST 数值不再额外上提 3px，改为和标签同一基线。
- 仅 HUD 排版，不动玩法、数值与 Boss。

## 验收

- 540×960 实拍：
  - `docs/shots/score_v0215.png`（分数块放大）
  - `docs/shots/score_v0215_boss.png`（带 Boss 卡的顶部）
- 门禁 438/438（UI 改动不涉及数值）。
- APK：大小 **29,056,576** 字节；versionName **0.21.5** / versionCode **33**；
  V2 签名 `CN=Android Debug`；sha256 `00728b0cc769d95a1bb4db47e80e0ed1cab17f774debe5bda45af731a67a1e2d`。
