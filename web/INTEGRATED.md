# 网页版（v2.8.4）——已并入本仓库

这里是原仓库 `sctale/SpaceFighter` 的 **完整源码快照**（HEAD `03c2611`，v2.8.4），
原样搬进 `space-fighter-unity/web/`，作为 Unity 版的**对照基准**保留：

- Unity 的数值门禁 `Assets/Editor/VerifyNumbers.cs`（278 条断言）就是从
  `www/js/entities.js`、`game.js`、`director.js` 逐条抄下来的。
- 网页版的玩法门禁在 `web/.tools/design-test.js`（70 条断言），两边可以对照查看。
- 历史版本、原始 README/CHANGELOG 都在本目录里，未做改动。

> 只导入了 git 跟踪的文件（81 个）。`node_modules/`、`android/` 下的构建产物、
> 以及 `.tools/shots/`（237MB 一次性模拟器截图）没有进来——它们都能重新生成。
> 完整备份（含 `.git` 历史与这些截图）见 `../backups/space-fighter-v2.8.4-backup-20261102.zip`。

## 目录

| 路径 | 说明 |
|---|---|
| `www/` | 游戏本体（纯 canvas JS，`index.html` + `js/*.js` + `css/`） |
| `android/` | Capacitor 安卓壳工程（Gradle） |
| `legacy/` | 早期版本留档 |
| `.tools/` | 冒烟/玩法/响应式测试与构建脚本 |
| `package.json` | npm 脚本入口 |

## 怎么跑

```bash
cd web
npm install

npm run test            # 冒烟 + 90 秒压力 + 帧率 + 版本一致性
npm run test:design     # 玩法设计回归（70 项）
npm run test:all        # 全部测试

npx serve www           # 纯浏览器里玩（无 Capacitor 桥接时自动降级）
npm run android:build:win   # Windows 出 APK（先 sync 再 gradlew assembleRelease）
```

产物落在 `web/android/app/build/outputs/apk/release/`。

## 与 Unity 版的关系

| 关注点 | 网页版 | Unity 版 |
|---|---|---|
| 载体 | Capacitor + canvas | Unity 6000.3 + IMGUI |
| 数值 | `www/js/*.js` 里的常量 | `Assets/Scripts/Core/GameConfig.cs`（同值） |
| 门禁 | `.tools/design-test.js` 70 条 | `Assets/Editor/VerifyNumbers.cs` 278 条 |
| 状态 | **已冻结**：只作为参考，不再单独发版 | 持续迭代 |

改玩法时**两边一起看**：先改 `web/www/js`（可读性最好的原始实现），再把数值/规则
落到 `GameConfig.cs`，最后在 `VerifyNumbers` 里补一条断言。
