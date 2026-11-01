const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const { launch, shotsDir } = require('./harness');
const GAME_URL = pathToFileURL(path.join(__dirname, '..', 'www', 'index.html')).href;
const SHOTS = shotsDir();
(async () => {
  const failures = [];
  const check = (label, ok, detail) => {
    console.log((ok ? '[OK]  ' : '[FAIL]') + ' ' + label + (detail === undefined ? '' : ' ' + JSON.stringify(detail)));
    if (!ok) failures.push(label);
  };
  const browser = await launch();
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push('CONSOLE: ' + m.text()); });
  page.on('pageerror', e => errors.push('PAGEERROR: ' + e.message));
  await page.goto(GAME_URL);
  await page.waitForFunction(() => window.__SF_READY === true);
  await page.screenshot({ path: path.join(SHOTS, '50-menu-final.png') });
  // 菜单里 HUD 是隐藏的，内部按钮不能被 Tab 聚焦
  await page.keyboard.press('Tab');
  const tabState = await page.evaluate(() => ({
    inHud: document.getElementById('hud').contains(document.activeElement),
    active: document.activeElement && document.activeElement.id,
  }));
  check('hidden HUD is not tabbable', !tabState.inHud, tabState);
  await page.click('#btnStart');
  await page.waitForTimeout(4000);
  await page.screenshot({ path: path.join(SHOTS, '51-play-level2.png') });
  check('HUD visible while playing', await page.evaluate(() => getComputedStyle(document.getElementById('hud')).visibility === 'visible'));
  console.log('START', JSON.stringify(await page.evaluate(() => __SF.snapshot())));

  // 版本一致性：菜单脚注、package.json、Gradle versionName/versionCode 必须同源
  const pkg = require(path.join(__dirname, '..', 'package.json'));
  const foot = await page.evaluate(() => document.querySelector('.menu-foot').textContent);
  const gradle = fs.readFileSync(path.join(__dirname, '..', 'android', 'app', 'build.gradle'), 'utf8');
  const gradleName = (gradle.match(/versionName\s+"([^"]+)"/) || [])[1];
  const gradleCode = Number((gradle.match(/versionCode\s+(\d+)/) || [])[1]);
  const v = pkg.version.split('.').map(Number);
  const expectedCode = v[0] * 10000 + v[1] * 100 + v[2] * 10;
  const versionOk = foot.includes('v' + pkg.version) && gradleName === pkg.version && gradleCode === expectedCode;
  console.log('VERSION', JSON.stringify({ foot: foot.trim(), pkg: pkg.version, gradleName, gradleCode, expectedCode }));
  check('version markers agree', versionOk, { pkg: pkg.version, gradleName, gradleCode, expectedCode });

  // keyboard controls
  const x0 = await page.evaluate(() => __SF.game.player.x);
  await page.keyboard.down('d');
  await page.waitForTimeout(400);
  await page.keyboard.up('d');
  const x1 = await page.evaluate(() => __SF.game.player.x);
  await page.keyboard.press('Space');
  await page.waitForTimeout(120);
  const dash = await page.evaluate(() => __SF.game.player.dashCd);
  check('keyboard movement + dash', x1 > x0 && dash > 0, { x0, x1, dash });
  await page.evaluate(() => { __SF.game.player.nova = 100; });
  await page.waitForTimeout(150);
  await page.screenshot({ path: path.join(SHOTS, '52-nova-ready.png') });
  await page.keyboard.press('x');
  await page.waitForTimeout(200);
  const novaState = await page.evaluate(() => ({ nova: __SF.game.player.nova, od: __SF.game.player.overdrive, score: __SF.game.score }));
  console.log('INPUT', JSON.stringify({ x0, x1, dash, novaState }));
  check('nova consumes charge and enters overdrive', novaState.nova === 0 && novaState.od > 0, novaState);

  // 低帧率扫掠回归：子弹单帧跨过战机，点判定会漏，线段判定必须命中
  const swept = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    g.enemyBullets.length = 0;
    g.enemies.length = 0;
    p.invuln = 0; p.dashT = 0; p.shieldCharges = 0; p.reflectT = 0;
    const before = g.lives;
    const b = new SF.Bullet(p.x, p.y - 200, 0, 24000, { kind: 'needle', r: 5, life: 2 });
    b.prevY = p.y - 200;
    b.y = p.y + 200;
    g.enemyBullets.push(b);
    g.collide();
    return { before, after: g.lives, removed: g.enemyBullets.length === 0 };
  });
  check('swept collision catches frame-skip bullet', swept.before - swept.after === 1 && swept.removed, swept);

  // pause screen with new setting
  await page.keyboard.press('Escape');
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(SHOTS, '53-pause-final.png') });
  const pauseState = await page.evaluate(() => __SF.snapshot().state);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  const resumeState = await page.evaluate(() => __SF.snapshot().state);
  console.log('PAUSE', pauseState, resumeState);
  check('pause / resume state', pauseState === 'paused' && resumeState === 'playing', { pauseState, resumeState });

  // mute toggle
  await page.keyboard.press('m');
  await page.waitForTimeout(200);
  console.log('MUTE', await page.evaluate(() => ({ muted: SF.Audio.muted, setting: __SF.game.settings.sound, ctx: SF.Audio.ctx && SF.Audio.ctx.state })));
  await page.keyboard.press('m');

  // boss + fps
  await page.evaluate(() => { __SF.game.player.invuln = 999; __SF.startBoss(); });
  await page.waitForTimeout(5500);
  const fps = await page.evaluate(async () => {
    let frames = 0; const t0 = performance.now();
    await new Promise(res => { function tick(){ frames++; if (performance.now() - t0 < 2000) requestAnimationFrame(tick); else res(); } requestAnimationFrame(tick); });
    return { fps: frames / ((performance.now() - t0) / 1000), boss: __SF.snapshot().boss, bullets: __SF.snapshot().counts.enemyBullets };
  });
  console.log('FPS', JSON.stringify(fps));
  check('boss fight runs', !!fps.boss, fps.boss);
  // 无 GPU 的 headless 环境约 30 FPS；底线用于拦截灾难性性能回退
  check('frame rate >= 24', fps.fps >= 24, { fps: Math.round(fps.fps) });
  await page.screenshot({ path: path.join(SHOTS, '54-boss-final.png') });
  // sustained fire: align player under boss by setting x every frame from the test
  await page.evaluate(() => { const g=__SF.game; g._alignTimer = setInterval(()=>{ if(g.boss){ g.player.x = g.boss.x; } }, 30); });
  await page.waitForTimeout(5000);
  console.log('BOSSHP', JSON.stringify(await page.evaluate(() => ({ snap: __SF.snapshot().boss, score: __SF.game.score }))));
  await page.screenshot({ path: path.join(SHOTS, '55-boss-hit.png') });
  await page.evaluate(() => clearInterval(__SF.game._alignTimer));

  // stress: long simulation from current state, forced sectors
  const stress = await page.evaluate(() => __SF.simulate(90, { boss: true }));
  console.log('STRESS', JSON.stringify(stress).slice(0, 1800));
  check('90s stress sim clean', stress.ok && !stress.nan, { ok: stress.ok, nan: stress.nan, errors: stress.errors });
  check('stress crosses sectors', stress.sectorsSeen.length >= 2, stress.sectorsSeen);
  console.log('ERRORS', errors.length, errors.join('\n---\n'));
  check('no console / page errors', errors.length === 0, errors.slice(0, 6));
  await browser.close();
  console.log(failures.length ? `FAILED ${failures.length}: ${failures.join(' | ')}` : 'ALL CHECKS PASS');
  if (failures.length) process.exit(1);
})().catch(e => { console.error('FAIL', e); process.exit(1); });
