# v0.21.7 — 修 HUD 字形被裁剪框切平（"分数被挡住"的真因）

用户连续三次反馈"左上角分数被最高分挡住"。v0.21.5 / v0.21.6 两次把 BEST 行往下挪都没解决，
因为这根本不是两行重叠：**是数字的下半截被 IMGUI 的裁剪框切掉了**。

## 根因

- HUD 所有文字样式统一 `clipping = TextClipping.Clip`（`Hud.Label()`）。
- 排版框高度按"视觉行距"给：分数框 52；而 AuroraCJK（Noto Sans CJK 子集）的行盒约 **1.5em**，
  50 号字需要 75。IMGUI 于是把超出 52 的字形像素直接丢弃。
- 实测墨迹范围：数字墨迹从框顶下 19.2 开始、到下 55.2 结束（框只有 52）→
  **底部约 3.2 ref 被切平**："0" 的下圈变成平底、"8" 的下圈被削薄，小屏上读起来
  就像数字上横着压了一条硬边线，也就是"被上面/下面的字挡住"。
- 对照：v0.21.6 实拍数字墨迹高 32px，v0.21.7 实拍 36px（= 50 号字的完整 cap height）。

## 改了什么

- `Hud.Txt()` 落笔前调用新的 `Hud.FitLine(rect, fontSize, anchor)`：把**裁剪框**补到 1.5em 行盒，
  上对齐只往下扩、居中对齐上下各扩一半，**字形位置一律不动**。一处生效，菜单/暂停/结算/Boss 卡
  全部字号同时修好（结算页 58 号"巡航终止"、Boss 名称 26 号字此前都被切过）。
- 左上分数块恢复对标网页版的紧凑行距，并按"墨迹间距"折算（IMGUI 的 Leading 在框顶，
  CSS `line-height:1` 的 Leading 是负的，两者不能直接照抄框坐标）：
  - BEST：**130.4 → 120.8**（数字墨迹底 118 → BEST 墨迹顶 128.4，间距 10.4 = CSS 24.8+2 折算）
  - GRAZE：**155.4 → 146.8**
  - 底板高度 154 → **148**（底边 172），左侧青色光条 112 → **100**
  - Boss 卡保持在 180.8，与底板留 8.8 的缝
- 排版基准提成常量 `Hud.ScoreTop / ScoreBoxH / BestTop / GrazeTop / DigitInk / BestInk`，
  门禁直接读它们——以后改任何一项会先被断言拦下，而不是靠肉眼再猜一轮。
- 门禁 **438 → 446**：新增 `beh.hudClip.*`（补框不改字形位置、够高的框不缩、
  裁剪框必须盖住数字墨迹底）与 `beh.hudScore.inkGap / grazeBelow`。
- 仅 HUD 排版与绘制，不动玩法数值、Boss 行为与美术。

## 验收

- 540×960 实拍：`docs/shots/score_v0217.png`（分数块）、`docs/shots/score_v0217_boss.png`（带 Boss 卡）、
  `docs/shots/dead_v0217.png`（结算页大分数）。
- 真机（Android 模拟器 1080×1920）实拍：`docs/shots/android_v0217_score.png`（分数块特写）、
  `docs/shots/android_v0217_play.png`（整屏，分数 1,026）。
- 墨迹高度用像素扫描量（不是目测）：数字行亮像素连续 **36 行**（v0.21.6 只有 32 行就被切平），
  数字墨迹底 118 → BEST 墨迹顶 128.4，间距 10.4 ref 与网页版一致。
- 门禁 **446/446**；真机 `E/Unity` 计数 **0**。

## APK

- 文件名：`AuroraRift-Unity-v0.21.7.apk`（项目根目录）
- 大小：**29,056,244** 字节；versionName **0.21.7** / versionCode **35**
- 清单：`com.sctale.spacefighter`，minSdk 25 / targetSdk 37，
  launchable-activity `com.unity3d.player.UnityPlayerGameActivity`
- 签名：`apksigner verify` → **Verifies**，`CN=Android Debug`
- sha256：`db99c130fb4e41b0cccb66c69edd44453a5ec9d1e919903129de7535076e5e92`

