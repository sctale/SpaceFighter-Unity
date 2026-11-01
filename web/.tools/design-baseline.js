const path = require('path');
const { pathToFileURL } = require('url');
const { launch, shotsDir } = require('./harness');
const URL = pathToFileURL(path.join(__dirname, '..', 'www', 'index.html')).href;
const SHOTS = shotsDir('design');

(async () => {
  const browser = await launch();
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });

  await page.goto(URL);
  await page.waitForFunction(() => window.__SF_READY === true);
  await page.waitForTimeout(1400);
  await page.screenshot({ path: path.join(SHOTS, '01-menu.png') });

  await page.click('#btnStart');
  await page.waitForTimeout(6500);
  await page.screenshot({ path: path.join(SHOTS, '02-early.png') });

  // dense mixed combat
  await page.evaluate(() => {
    const g = __SF.game;
    g.godMode = true;
    g.sector = 4;
    g.director.startSector(4);
    ['drifter', 'lancer', 'orbiter', 'splitter', 'bulwark', 'sniper', 'minekeeper', 'sidewinder', 'carrier']
      .forEach((t, i) => {
        const e = __SF.spawn(t, SF.W * (0.14 + (i % 5) * 0.18), SF.H * (0.16 + Math.floor(i / 5) * 0.14));
        e.hp = e.maxHp = 99999;
      });
    __SF.give('shield'); __SF.give('shield');
    __SF.give('drone'); __SF.give('drone'); __SF.give('drone');
    __SF.give('prism'); __SF.give('swarm');
    __SF.give('reflect');
    g.player.nova = 86;
    g.combo = 28;
    g.comboTimer = g.comboMax;
  });
  await page.waitForTimeout(2200);
  await page.screenshot({ path: path.join(SHOTS, '03-arsenal-dense.png') });

  await page.evaluate(() => { __SF.game.player.nova = 100; __SF.game.tryNova(); });
  await page.waitForTimeout(320);
  await page.screenshot({ path: path.join(SHOTS, '04-nova.png') });
  await page.waitForTimeout(900);
  await page.screenshot({ path: path.join(SHOTS, '05-overdrive.png') });

  // enemy kill burst
  await page.evaluate(() => {
    const g = __SF.game;
    g.enemies.length = 0;
    for (let i = 0; i < 7; i++) {
      const e = __SF.spawn('drifter', SF.W * (0.18 + i * 0.11), SF.H * 0.35);
      e.hp = 1;
    }
  });
  await page.waitForTimeout(700);
  await page.screenshot({ path: path.join(SHOTS, '06-kills.png') });

  // boss
  await page.evaluate(() => {
    const g = __SF.game;
    g.enemies.length = 0;
    __SF.startBoss();
  });
  await page.waitForTimeout(4300);
  await page.screenshot({ path: path.join(SHOTS, '07-boss-ready.png') });
  await page.evaluate(() => {
    const g = __SF.game;
    g.boss.hit = () => {};
    g.boss.maxHp = 1200; g.boss.hp = 520;
    __SF.fireBossAttack('twinBeam');
  });
  await page.waitForTimeout(1100);
  await page.screenshot({ path: path.join(SHOTS, '08-boss-twin.png') });
  await page.evaluate(() => { __SF.game.lasers.length = 0; __SF.fireBossAttack('mineCurtain'); });
  await page.waitForTimeout(1300);
  await page.screenshot({ path: path.join(SHOTS, '09-boss-mines.png') });
  await page.evaluate(() => { __SF.game.enemyBullets.length = 0; __SF.fireBossAttack('blink'); });
  await page.waitForTimeout(950);
  await page.screenshot({ path: path.join(SHOTS, '10-boss-blink.png') });

  // death / game over
  await page.evaluate(() => {
    const g = __SF.game;
    g.godMode = false;
    g.boss.hit = undefined;
    g.boss.dying = true;
    g.boss.deathT = 2.7;
    g.onBossDefeated(g.boss);
    g.lives = 1;
    g.player.shieldCharges = 0;
    g.player.reflectT = 0;
    g.player.invuln = 0;
    g.playerHit();
  });
  await page.waitForTimeout(2800);
  await page.screenshot({ path: path.join(SHOTS, '11-gameover.png') });

  await page.evaluate(() => { __SF.toMenu(); });
  await page.waitForTimeout(1000);
  await page.click('#btnStart');
  await page.waitForTimeout(2500);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(600);
  await page.screenshot({ path: path.join(SHOTS, '12-pause.png') });

  // small screen
  const small = await browser.newContext({ viewport: { width: 360, height: 640 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const sp = await small.newPage();
  await sp.goto(URL);
  await sp.waitForFunction(() => window.__SF_READY === true);
  await sp.waitForTimeout(1300);
  await sp.screenshot({ path: path.join(SHOTS, '13-menu-small.png') });
  await sp.click('#btnStart');
  await sp.evaluate(() => { __SF.give('prism'); __SF.give('swarm'); __SF.give('shield'); __SF.give('drone'); __SF.startBoss(); });
  await sp.waitForTimeout(5000);
  await sp.screenshot({ path: path.join(SHOTS, '14-boss-small.png') });

  // desktop
  const desk = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
  const dp = await desk.newPage();
  await dp.goto(URL);
  await dp.waitForFunction(() => window.__SF_READY === true);
  await dp.waitForTimeout(1300);
  await dp.screenshot({ path: path.join(SHOTS, '15-menu-desktop.png') });
  await dp.click('#btnStart');
  await dp.waitForTimeout(5000);
  await dp.screenshot({ path: path.join(SHOTS, '16-play-desktop.png') });

  console.log('BASELINE_DONE', errors.length, errors.slice(0, 5).join('\n'));
  await browser.close();
  if (errors.length) process.exit(1);
})().catch(e => { console.error('FAIL', e); process.exit(1); });
