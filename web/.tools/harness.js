// 回归脚本共用：跨平台启动浏览器 + 确保截图目录存在
const { chromium } = require('playwright-core');
const fs = require('fs');
const path = require('path');

async function launch() {
  const args = process.platform === 'linux' ? ['--no-sandbox'] : [];
  const channel = process.env.SF_BROWSER_CHANNEL;
  if (channel) return chromium.launch({ channel, headless: true, args });
  try {
    // 优先用 Playwright 自带 Chromium（CI / Linux）
    return await chromium.launch({ headless: true, args });
  } catch (err) {
    // Windows 本机通常没有自带 Chromium，退回 Edge
    try {
      return await chromium.launch({ channel: 'msedge', headless: true, args });
    } catch (err2) {
      err2.message = `${err2.message}\n（Chromium 启动失败：${err.message}）`;
      throw err2;
    }
  }
}

function shotsDir(sub) {
  const dir = path.join(__dirname, 'shots', sub || '');
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

module.exports = { launch, shotsDir };
