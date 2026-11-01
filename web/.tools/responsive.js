const path = require('path');
const { pathToFileURL } = require('url');
const { launch, shotsDir } = require('./harness');
const GAME_URL = pathToFileURL(path.join(__dirname, '..', 'www', 'index.html')).href;
const SHOTS = shotsDir();
(async () => {
  const failures = [];
  const browser = await launch();
  for (const [name, w, h, isMobile] of [['small', 360, 640, true], ['desktop', 1440, 900, false], ['tablet', 800, 1280, true]]) {
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: 1, isMobile, hasTouch: isMobile });
    const page = await ctx.newPage();
    page.on('pageerror', e => { failures.push(name + ': ' + e.message); console.log(name, 'ERR', e.message); });
    await page.goto(GAME_URL);
    await page.waitForFunction(() => window.__SF_READY === true);
    await page.waitForTimeout(1200);
    await page.screenshot({ path: path.join(SHOTS, `90-${name}-menu.png`) });
    await page.click('#btnStart');
    await page.waitForTimeout(2500);
    await page.screenshot({ path: path.join(SHOTS, `91-${name}-play.png`) });
    if (isMobile) await page.click('#btnPause'); else await page.keyboard.press('Escape');
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(SHOTS, `92-${name}-pause.png`) });
    const info = await page.evaluate(() => ({ stage: (()=>{const r=document.querySelector('.stage').getBoundingClientRect(); return {w:r.width,h:r.height,top:r.top}})(), screenOverflow: (()=>{const s=document.querySelector('.screen.is-active .panel-card'); return s ? {h:s.getBoundingClientRect().height, sh:s.scrollHeight, ch:s.clientHeight} : null})(), docH: document.documentElement.scrollHeight, innerH: innerHeight }));
    console.log(name, JSON.stringify(info));
    await ctx.close();
  }
  await browser.close();
  if (failures.length) { console.error('FAILED', failures.join(' | ')); process.exit(1); }
})().catch(e => { console.error(e); process.exit(1); });
