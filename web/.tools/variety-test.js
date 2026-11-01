const path = require('path');
const { pathToFileURL } = require('url');
const { launch, shotsDir } = require('./harness');
const URL = pathToFileURL(path.join(__dirname, '..', 'www', 'index.html')).href;
const SHOTS = shotsDir();

(async () => {
  const browser = await launch();
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push('CONSOLE: ' + m.text()); });
  page.on('pageerror', e => errors.push('PAGEERROR: ' + e.message));

  await page.goto(URL);
  await page.waitForFunction(() => window.__SF_READY === true);
  await page.click('#btnStart');
  await page.waitForTimeout(800);

  // isolate: no player fire, immortal player
  await page.evaluate(() => {
    const g = __SF.game;
    g.godMode = true;
    g.player.shoot = () => {};
    g.player.invuln = 9999;
    g.director.state = 'idle';
    g.director.pending.length = 0;
    g.director.script.length = 0;
  });

  async function inspectEnemy(type, ms, extra) {
    await page.evaluate(({ type, extra }) => {
      const g = __SF.game;
      g.enemies.length = 0; g.enemyBullets.length = 0; g.playerBullets.length = 0;
      const opts = Object.assign({ sector: 3 }, extra || {});
      const e = __SF.spawn(type, type === 'sidewinder' ? -40 : SF.W * 0.5, SF.H * 0.22, opts);
      e.hp = e.maxHp = 99999;
    }, { type, extra });
    await page.waitForTimeout(ms);
    const snap = await page.evaluate(() => {
      const g = __SF.game;
      const e = g.enemies[0];
      const kinds = {};
      for (const b of g.enemyBullets) kinds[b.kind] = (kinds[b.kind] || 0) + 1;
      return {
        types: g.enemies.map(x => x.type),
        pos: e ? { x: Math.round(e.x), y: Math.round(e.y), charge: +e.chargeT.toFixed(2) } : null,
        kinds,
      };
    });
    console.log('ENEMY', type.padEnd(11), JSON.stringify(snap));
    await page.screenshot({ path: path.join(SHOTS, '210-enemy-' + type + '.png') });
    return snap;
  }

  await inspectEnemy('sniper', 1800);
  await inspectEnemy('minekeeper', 3200);
  await inspectEnemy('sidewinder', 2600, { dir: 1 });
  await inspectEnemy('carrier', 4200);
  await inspectEnemy('shardling', 1200);
  await inspectEnemy('splitter', 900);

  const split = await page.evaluate(() => {
    const g = __SF.game;
    g.enemies.length = 0;
    const s = __SF.spawn('splitter', SF.W * 0.5, SF.H * 0.4);
    s.hp = 0; s.dead = true; g.killEnemy(s);
    return g.enemies.map(e => e.type);
  });
  console.log('SPLITTER -> ', JSON.stringify(split));

  // weapon modes
  await page.evaluate(() => { delete __SF.game.player.shoot; });
  const modes = {};
  for (const mode of ['prism', 'swarm']) {
    const r = await page.evaluate((m) => {
      const g = __SF.game, p = g.player;
      p.weapons[m].unlocked = true;
      p.selectWeapon(m); p.level = 5; p.weapons[m].level = 5; p.overdrive = 0;
      p.drones.length = 0;
      g.playerBullets.length = 0;
      g.enemies.length = 0;
      for (let i = 0; i < 5; i++) {
        const e = __SF.spawn('drifter', p.x, 110 + i * 38);
        e.hp = e.maxHp = 99999;
      }
      for (let i = 0; i < 8; i++) p.shoot();
      const kinds = {};
      for (const b of g.playerBullets) kinds[b.kind] = (kinds[b.kind] || 0) + 1;
      const hits = g.enemies.filter(e => e.flash > 0).length;
      return { kinds, pierceLeft: g.playerBullets.reduce((s, b) => s + (b.pierce || 0), 0), hits, total: g.playerBullets.length };
    }, mode);
    modes[mode] = r;
    console.log('WEAPON', mode, JSON.stringify(r));
    await page.screenshot({ path: path.join(SHOTS, '211-weapon-' + mode + '.png') });
  }

  // escort roster cap
  const cap = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    p.drones.length = 0;
    const added = ['gunner', 'lance', 'aegis', 'swarm'].map(k => p.addDrone(k));
    return { added, drones: p.drones.map(d => d.kind) };
  });
  console.log('DRONE CAP', JSON.stringify(cap));

  // drone point defense
  const droneTest = await page.evaluate(async () => {
    const g = __SF.game, p = g.player;
    p.drones.length = 0;
    ['gunner', 'lance', 'aegis', 'swarm'].forEach(k => p.addDrone(k));
    g.playerBullets.length = 0;
    g.enemyBullets.length = 0;
    for (let i = 0; i < 24; i++) {
      __SF.game.enemyBullets.push(new SF.Bullet(p.drones[2].x + (i % 5) * 4 - 8, p.drones[2].y + (i % 4) * 4 - 6, 0, 20, { kind: 'orb', r: 6, life: 8 }));
    }
    const before = g.enemyBullets.length;
    for (let i = 0; i < 80; i++) {
      p.update(1 / 60, g.input);
      for (let j = g.enemyBullets.length - 1; j >= 0; j--) if (!g.enemyBullets[j].update(1 / 60, p)) g.enemyBullets.splice(j, 1);
      g.pointDefense();
      for (let j = g.playerBullets.length - 1; j >= 0; j--) if (!g.playerBullets[j].update(1 / 60, p)) g.playerBullets.splice(j, 1);
    }
    const kinds = {};
    for (const b of g.playerBullets) kinds[b.kind] = (kinds[b.kind] || 0) + 1;
    return { drones: p.drones.map(d => d.kind), before, after: g.enemyBullets.length, friendlyKinds: kinds };
  });
  console.log('DRONES', JSON.stringify(droneTest));
  await page.evaluate(() => { __SF.game.player.reflectT = 8; });
  await page.waitForTimeout(700);
  await page.screenshot({ path: path.join(SHOTS, '212-drones-reflect.png') });

  // boss patterns with an immortal boss, sampled mid-pattern
  await page.evaluate(() => { __SF.startBoss(); });
  await page.waitForTimeout(3800);
  await page.evaluate(() => {
    const g = __SF.game;
    g.boss.hit = () => {};
    g.boss.maxHp = g.boss.hp = 99999;
  });
  for (const p of ['twinBeam', 'mineCurtain', 'blink', 'laserWeb', 'waveCurtain', 'spiral', 'fan', 'summon']) {
    await page.evaluate((name) => {
      const g = __SF.game;
      g.enemyBullets.length = 0; g.lasers.length = 0;
      __SF.fireBossAttack(name);
    }, p);
    await page.waitForTimeout(1100);
    const mid = await page.evaluate(() => {
      const g = __SF.game;
      const kinds = {};
      for (const b of g.enemyBullets) kinds[b.kind] = (kinds[b.kind] || 0) + 1;
      return { kinds, lasers: g.lasers.length, attack: g.boss.attack && g.boss.attack.name, label: g.boss.attackLabel, boss: { x: Math.round(g.boss.x), y: Math.round(g.boss.y) } };
    });
    console.log('BOSS', p.padEnd(12), JSON.stringify(mid));
    await page.screenshot({ path: path.join(SHOTS, '213-boss-' + p + '.png') });
    await page.waitForTimeout(1500);
  }

  const hud = await page.evaluate(() => {
    const mod = document.getElementById('weaponMod');
    return {
      pattern: document.getElementById('bossPattern').textContent,
      patternLive: document.getElementById('bossPattern').classList.contains('is-live'),
      shieldHidden: document.getElementById('shieldChip').hidden,
      modHidden: mod ? mod.hidden : true,
    };
  });
  console.log('HUD', JSON.stringify(hud));

  console.log('ERRORS', errors.length, errors.slice(0, 5).join('\n---\n'));
  await browser.close();
  if (errors.length) process.exit(1);
})().catch(e => { console.error('FAIL', e); process.exit(1); });
