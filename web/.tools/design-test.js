// Design regression: escort formation, weapon DPS spread, off-screen fairness,
// and the boss spell rotation / phase pressure. Drives the real game objects.
const path = require('path');
const { pathToFileURL } = require('url');
const { launch, shotsDir } = require('./harness');
const URL = pathToFileURL(path.join(__dirname, '..', 'www', 'index.html')).href;
const SHOTS = shotsDir();

const step = (page, frames, perFrame) => page.evaluate(({ frames, perFrame }) => {
  const g = __SF.game;
  const dt = 1 / 60;
  const out = [];
  for (let i = 0; i < frames; i++) {
    if (perFrame) eval(perFrame)(g, dt, i, out);
    if (g.player && g.player.alive) g.player.update(dt, g.input);
    if (g.boss) g.boss.update(dt);
    for (let j = g.enemyBullets.length - 1; j >= 0; j--) if (!g.enemyBullets[j].update(dt, g.player)) g.enemyBullets.splice(j, 1);
    for (let j = g.playerBullets.length - 1; j >= 0; j--) if (!g.playerBullets[j].update(dt, g.player)) g.playerBullets.splice(j, 1);
    for (let j = g.enemies.length - 1; j >= 0; j--) if (!g.enemies[j].update(dt, g.player)) g.enemies.splice(j, 1);
    for (let j = g.lasers.length - 1; j >= 0; j--) if (!g.lasers[j].update(dt)) g.lasers.splice(j, 1);
    g.pointDefense();
    g.collide();
  }
  return out;
}, { frames, perFrame });

(async () => {
  const browser = await launch();
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push('CONSOLE: ' + m.text()); });
  page.on('pageerror', e => errors.push('PAGEERROR: ' + e.message));
  let failures = 0;
  const fail = (label, ok, info) => {
    if (!ok) failures++;
    console.log((ok ? '  [OK]  ' : '  [FAIL]') + ' ' + label.padEnd(34) + JSON.stringify(info));
  };

  await page.goto(URL);
  await page.waitForFunction(() => window.__SF_READY === true);
  await page.click('#btnStart');
  await page.waitForTimeout(600);
  await page.evaluate(() => {
    const g = __SF.game;
    g.godMode = true;
    g.player.invuln = 9999;
    g.director.state = 'idle';
    g.director.pending.length = 0;
    g.director.script.length = 0;
    g.enemies.length = 0; g.enemyBullets.length = 0; g.playerBullets.length = 0;
    g.settings.lowFx = true;
  });

  /* ---------- 1. escort flies on the wings, it does not orbit ---------- */
  await page.evaluate(() => {
    const p = __SF.game.player;
    p.drones.length = 0;
    p.addDrone('gunner'); p.addDrone('lance'); p.addDrone('aegis');
  });
  const formation = await step(page, 180, `(function(g,dt,i,out){
    g.player.x += Math.sin(i/12)*1.6;
    if (i % 30 === 0) out.push(g.player.drones.map(d => ({
      kind: d.kind, dx: Math.round(d.x - g.player.x), dy: Math.round(d.y - g.player.y),
      ang: +d.angle.toFixed(2)
    })));
  })`);
  const slots = formation[formation.length - 1];
  const sideOk = slots[0].dx < -30 && slots[1].dx > 30 && Math.abs(slots[0].dy) < 40;
  const stable = formation.every(f => f.every((d, k) => Math.abs(d.dx - slots[k].dx) <= 14 && Math.abs(d.dy - slots[k].dy) <= 16));
  fail('wingmen hold side slots', sideOk && stable, { first: formation[0], last: slots });

  /* ---------- 2. main-ship and escort ammo differ ---------- */
  const ammo = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    g.playerBullets.length = 0;
    p.weapons.pulse.unlocked = true; p.selectWeapon('pulse'); p.level = 3; p.weapons.pulse.level = 3;
    for (let i = 0; i < 6; i++) p.shoot();
    const main = g.playerBullets.map(b => b.kind);
    g.playerBullets.length = 0;
    p.drones.length = 0;
    ['gunner', 'lance', 'swarm', 'aegis'].forEach(k => p.addDrone(k));
    const hullShoot = p.shoot;
    p.shoot = () => {};                       // isolate wing fire from hull fire
    for (let i = 0; i < 120; i++) p.update(1 / 60, g.input);
    p.shoot = hullShoot;
    const drone = g.playerBullets.map(b => b.kind);
    return { main, drone, mainKinds: [...new Set(main)], droneKinds: [...new Set(drone)] };
  });
  fail('hull and wing ammo differ',
    ammo.mainKinds.length >= 1 && ammo.droneKinds.length >= 2 && !ammo.droneKinds.some(k => ammo.mainKinds.includes(k)),
    { hull: ammo.mainKinds, wing: ammo.droneKinds });

  /* ---------- 3. off-screen grunts cannot be hunted down ---------- */
  const offscreen = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    g.enemies.length = 0; g.playerBullets.length = 0;
    p.drones.length = 0;
    p.weapons.swarm.unlocked = true; p.selectWeapon('swarm'); p.level = 5; p.weapons.swarm.level = 5;
    const far = __SF.spawn('drifter', SF.W * 0.5, -260);
    far.hp = far.maxHp = 400;
    const near = __SF.spawn('drifter', SF.W * 0.5, SF.H * 0.3);
    near.hp = near.maxHp = 400;
    near.y = SF.H * 0.3;
    const dt = 1 / 60;
    for (let i = 0; i < 420; i++) {
      p.update(dt, g.input);
      for (const e of g.enemies) e.update(dt, p);
      far.y = -260; far.x = SF.W * 0.5;           // pinned off-screen: it never legally enters
      for (let j = g.playerBullets.length - 1; j >= 0; j--) if (!g.playerBullets[j].update(dt, p)) g.playerBullets.splice(j, 1);
      g.collide();
    }
    return {
      farTargetable: far.targetable(), farHp: Math.round(far.hp), farY: Math.round(far.y),
      nearTargetable: near.targetable(), nearHp: Math.round(near.hp),
      offscreenShootable: far.hp < far.maxHp,
    };
  });
  fail('off-screen enemy takes no damage',
    !offscreen.offscreenShootable && !offscreen.farTargetable && offscreen.nearHp < 400,
    offscreen);

  /* ---------- 3.5 speed/power triangle, charge, resonance, hit penalty ---------- */
  const tri = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    const out = {};
    for (const wname of ['pulse', 'prism', 'swarm']) {
      g.playerBullets.length = 0;
      p.weapons[wname].unlocked = true;
      p.selectWeapon(wname);
      p.level = 5; p.weapons[wname].level = 5;
      p.weapons[wname].charge = 100; p.weapons[wname].detuned = false;
      p.overdrive = 0; p.resonance = 0; p.resT = 0;
      p.shoot();
      const bs = g.playerBullets;
      out[wname] = {
        kinds: [...new Set(bs.map(b => b.kind))],
        avgSpeed: Math.round(bs.reduce((s, b) => s + Math.hypot(b.vx, b.vy), 0) / bs.length),
        maxDmg: Math.max(...bs.map(b => b.dmg)),
      };
    }
    return out;
  });
  fail('speed triangle pulse > swarm > prism',
    tri.pulse.avgSpeed > tri.swarm.avgSpeed && tri.swarm.avgSpeed > tri.prism.avgSpeed, tri);
  fail('power triangle prism > swarm > pulse',
    tri.prism.maxDmg > tri.swarm.maxDmg && tri.swarm.maxDmg > tri.pulse.maxDmg, tri);

  const charge = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    for (const k of ['pulse', 'prism', 'swarm']) {
      p.weapons[k].unlocked = true;
      p.weapons[k].charge = 100; p.weapons[k].detuned = false;
    }
    p.selectWeapon('pulse'); p.level = 5; p.weapons.pulse.level = 5;
    p.resonance = 0; p.resT = 0; p.overdrive = 0;
    p.x = SF.W * 0.5; p.y = SF.H * 0.8;
    const dt = 1 / 60;
    let t = 0, detunedAt = null, detunedMul = 1;
    while (t < 30) {
      p.update(dt, g.input);
      for (let j = g.playerBullets.length - 1; j >= 0; j--) if (!g.playerBullets[j].update(dt, p)) g.playerBullets.splice(j, 1);
      if (p.weapons.pulse.detuned && detunedAt === null) { detunedAt = +t.toFixed(1); detunedMul = p.damageMul(); }
      t += dt;
    }
    p.selectWeapon('prism');                        // rested line -> resonance
    const resonanceAfterSwap = p.resonance;
    const swapMul = p.damageMul();
    const pulseBefore = p.weapons.pulse.charge;
    for (let i = 0; i < 60 * 5; i++) p.update(dt, g.input);   // keep firing prism
    const pulseRecovered = Math.round(p.weapons.pulse.charge - pulseBefore);
    p.addDrone('gunner');
    const before = { level: p.level, drones: p.drones.length, charge: Math.round(p.weapons.prism.charge), res: p.resonance };
    const lost = p.applyHitPenalty();
    const after = { level: p.level, drones: p.drones.length, charge: Math.round(p.weapons.prism.charge), res: p.resonance };
    return { detunedAt, detunedMul, resonanceAfterSwap, swapMul, pulseRecovered, before, after, lost };
  });
  fail('sustained fire detunes a line (8-20s) at a softer bite',
    charge.detunedAt > 8 && charge.detunedAt < 20 && Math.abs(charge.detunedMul - 0.55) < 0.01, charge);
  fail('swapping to a charged line stacks resonance',
    charge.resonanceAfterSwap === 1 && charge.swapMul > charge.detunedMul, charge);
  fail('holstered lines refill', charge.pulseRecovered > 40, charge);
  fail('a hit costs charge, resonance and a wingman',
    charge.after.drones === charge.before.drones - 1 && charge.after.res === 0
    && charge.after.charge < charge.before.charge && charge.lost === 'gunner', charge);

  const droneAmmo = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    const out = {};
    p.drones.length = 0;
    ['gunner', 'lance'].forEach(k => p.addDrone(k));
    for (const wname of ['pulse', 'prism', 'swarm']) {
      p.weapons[wname].unlocked = true;
      p.selectWeapon(wname); p.level = 3; p.weapons[wname].level = 3;
      p.weapons[wname].charge = 100; p.weapons[wname].detuned = false;
      g.playerBullets.length = 0;
      p.drones.forEach(d => { d.fireT = 0; });
      for (let i = 0; i < 100; i++) p.update(1 / 60, g.input);
      out[wname] = [...new Set(g.playerBullets.map(b => b.kind))];
    }
    p.drones.length = 0;
    return out;
  });
  fail('wingmen change ammo with the active line',
    droneAmmo.pulse.includes('drone') && droneAmmo.prism.includes('beam') && droneAmmo.swarm.includes('micro'),
    droneAmmo);

  const tints = await page.evaluate(() => {
    const p = __SF.game.player;
    return {
      pulse: SF.WEAPON_LINES.pulse.tint, prism: SF.WEAPON_LINES.prism.tint, swarm: SF.WEAPON_LINES.swarm.tint,
      current: p.line().tint, active: p.activeWeapon,
    };
  });
  fail('each line paints the hull its own colour',
    new Set([tints.pulse, tints.prism, tints.swarm]).size === 3 && tints.current === tints[tints.active], tints);

  const novaEco = await page.evaluate(() => {
    const p = __SF.game.player;
    p.overdrive = 0; p.resonance = 0;
    p.weapons.swarm.charge = 0; p.weapons.swarm.detuned = true;
    p.selectWeapon('swarm');
    p.nova = 0; p.addNova(10);
    const detunedGain = p.nova;
    p.weapons.swarm.charge = 100; p.weapons.swarm.detuned = false;
    p.nova = 0; p.addNova(10);
    return { detunedGain, healthyGain: p.nova };
  });
  fail('a detuned line feeds nova slower', novaEco.detunedGain < novaEco.healthyGain * 0.6, novaEco);

  const regen = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    p.overdrive = 0; p.nova = 0; p.invuln = 9999;
    const dt = 1 / 60;
    for (let i = 0; i < 60 * 10; i++) p.update(dt, g.input);
    return { after10s: Math.round(p.nova) };
  });
  fail('nova builds up on its own (~1.8/s)', regen.after10s >= 14 && regen.after10s <= 22, regen);

  const tiers = await page.evaluate(() => {
    const mk = (s) => { const b = new SF.Boss(s); return { tier: b.tier, spd: Math.round(b.bulletSpeed()) }; };
    return { s1: mk(1), s5: mk(5), s6: mk(6), s10: mk(10), s11: mk(11) };
  });
  fail('intensity ramps in 5-sector tiers',
    tiers.s1.tier === 0 && tiers.s5.tier === 0 && tiers.s6.tier === 1 && tiers.s10.tier === 1 && tiers.s11.tier === 2
    && tiers.s1.spd === tiers.s5.spd && tiers.s6.spd > tiers.s5.spd, tiers);

  const hitNova = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    g.godMode = false;
    p.nova = 0; p.overdrive = 0;
    p.invuln = 0; p.dashT = 0; p.shieldCharges = 0; p.reflectT = 0;
    g.playerHit();
    const hullGain = Math.round(p.nova);
    p.nova = 0; p.overdrive = 0; p.invuln = 0; p.dashT = 0;
    p.shieldCharges = 1; p.reflectT = 0;
    g.playerHit();
    const shieldGain = Math.round(p.nova);
    g.godMode = true;
    p.shieldCharges = 0;
    p.invuln = 9999;
    return { hullGain, shieldGain, lives: g.lives };
  });
  fail('taking damage feeds the star core (hull > shield > 0)',
    hitNova.hullGain >= 20 && hitNova.shieldGain >= 5 && hitNova.shieldGain < hitNova.hullGain, hitNova);

  const vent = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    delete p.shoot;
    for (const k of ['pulse', 'prism', 'swarm']) { p.weapons[k].charge = 100; p.weapons[k].detuned = false; }
    p.selectWeapon('pulse'); p.level = 5; p.weapons.pulse.level = 5;
    p.overdrive = 0; p.resonance = 0;
    p.weapons.pulse.charge = 0; p.weapons.pulse.detuned = true;
    p.x = SF.W * 0.5; p.y = SF.H * 0.8;
    const dt = 1 / 60;
    let t = 0, rearmedAt = null;
    while (t < 20) {
      p.update(dt, g.input);
      for (let j = g.playerBullets.length - 1; j >= 0; j--) if (!g.playerBullets[j].update(dt, p)) g.playerBullets.splice(j, 1);
      if (!p.weapons.pulse.detuned && rearmedAt === null) rearmedAt = +t.toFixed(1);
      t += dt;
    }
    return { rearmedAt, charge: Math.round(p.weapons.pulse.charge) };
  });
  fail('a dry line vents and re-arms without switching', vent.rearmedAt !== null && vent.rearmedAt > 1 && vent.rearmedAt < 8, vent);

  // The complaint this guards: a meter that climbs a little, falls, detunes, climbs
  // again — never reaching the top. Sixty seconds of held-trigger fire must produce a
  // handful of clean full sweeps, not a fast sawtooth.
  const cycle = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    for (const k of ['pulse', 'prism', 'swarm']) {
      p.weapons[k].unlocked = true; p.weapons[k].charge = 100; p.weapons[k].detuned = false;
    }
    p.selectWeapon('pulse'); p.level = 5; p.weapons.pulse.level = 5;
    p.resonance = 0; p.resT = 0; p.overdrive = 0; p.invuln = 1e6;
    p.x = SF.W * 0.5; p.y = SF.H * 0.8;
    const dt = 1 / 60;
    const series = [];
    let t = 0;
    while (t < 60) {
      p.update(dt, g.input);
      for (let j = g.playerBullets.length - 1; j >= 0; j--) if (!g.playerBullets[j].update(dt, p)) g.playerBullets.splice(j, 1);
      series.push([+t.toFixed(2), +p.weapons.pulse.charge.toFixed(1), p.weapons.pulse.detuned ? 1 : 0]);
      t += dt;
    }
    const w = series.map((r) => r[1]);
    const d = series.map((r) => r[2]);
    let tops = 0, dryBeforeTop = 0, wasTop = true, sawDry = false;
    const readySpans = [];
    let spanStart = null;
    for (let i = 1; i < series.length; i++) {
      const ch = series[i][1], det = series[i][2], prevCh = series[i - 1][1], prevDet = series[i - 1][2];
      if (!wasTop && ch >= 99.5) { tops++; wasTop = true; if (sawDry) dryBeforeTop++; }
      if (ch < 5) sawDry = true;
      if (ch < 90) wasTop = false;
      if (prevDet === 1 && det === 0) { spanStart = series[i][0]; }
      if (prevDet === 0 && det === 1 && spanStart !== null) { readySpans.push(+(series[i][0] - spanStart).toFixed(1)); spanStart = null; }
    }
    // an open span at the end of the window is cut off by the test, not by the game
    const detuneEpisodes = d.filter((v, i) => v === 1 && (i === 0 || d[i - 1] === 0)).length;
    return { max: Math.max(...w), tops, dryBeforeTop, detuneEpisodes, readySpans, ventTo: w.filter((v, i) => d[i] === 0 && d[i - 1] === 1) };
  });
  fail('the charge meter really tops out (>=99.5) more than once per minute',
    cycle.max >= 99.5 && cycle.tops >= 2 && cycle.dryBeforeTop === cycle.tops, cycle);
  fail('no sawtooth: each re-arm is preceded by a full vent and buys a long healthy span',
    cycle.detuneEpisodes <= 5 && cycle.readySpans.length >= 2
    && cycle.readySpans.every((t) => t > 8) && cycle.ventTo.every((v) => v >= 99.5), cycle);

  // Swap crates: the answer to "one weapon from spawn to screen". The crate has to
  // rotate onto the readiest holstered line, top it up, never be a wasted drop, and
  // actually show up in the loot.
  const swapCrate = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    for (const k of ['pulse', 'prism', 'swarm']) {
      p.weapons[k].unlocked = true; p.weapons[k].level = 3;
      p.weapons[k].charge = 100; p.weapons[k].detuned = false;
    }
    p.selectWeapon('pulse'); p.resonance = 0; p.resT = 0; p.swapIdle = 22;
    p.weapons.prism.charge = 34; p.weapons.swarm.charge = 50;
    const r = p.swapToBest();
    const trio = {
      name: r.name, active: p.activeWeapon, charge: Math.round(p.weapons[r.name].charge),
      res: p.resonance, idle: p.swapIdle, swapped: r.swapped,
    };
    // single line: nothing to rotate to, so the crate refills the gun you have
    p.weapons.prism.unlocked = false; p.weapons.swarm.unlocked = false;
    p.activeWeapon = 'pulse'; p.weapons.pulse.charge = 20; p.weapons.pulse.detuned = true;
    const solo = p.swapToBest();
    const single = { swapped: solo.swapped, charge: Math.round(p.weapons.pulse.charge), detuned: p.weapons.pulse.detuned };
    // a crate must not resurrect a line that is still venting past full
    p.weapons.prism.unlocked = true; p.weapons.swarm.unlocked = true;
    p.weapons.prism.charge = 8; p.weapons.prism.detuned = true;
    p.weapons.swarm.charge = 4; p.weapons.swarm.detuned = true;
    const dry = p.swapToBest();
    const drySwap = { name: dry.name, charge: Math.round(p.weapons[dry.name].charge), detuned: p.weapons[dry.name].detuned };
    return { trio, single, drySwap };
  });
  fail('a swap crate rotates onto the readiest line and tops it up',
    swapCrate.trio.name === 'swarm' && swapCrate.trio.active === 'swarm'
    && swapCrate.trio.charge === 80 && swapCrate.trio.res === 1 && swapCrate.trio.idle === 0, swapCrate);
  fail('with one line the crate refills instead of doing nothing',
    swapCrate.single.swapped === false && swapCrate.single.charge === 65
    && swapCrate.single.detuned === false, swapCrate);
  fail('a crate cannot re-arm a line that is still venting below the clear',
    swapCrate.drySwap.charge === 38 && swapCrate.drySwap.detuned === true, swapCrate);

  const swapDrops = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    for (const k of ['pulse', 'prism', 'swarm']) {
      p.weapons[k].unlocked = true; p.weapons[k].charge = 100; p.weapons[k].detuned = false;
    }
    p.level = 5; p.shieldCharges = 3; p.drones.length = 0; p.reflectT = 6; g.lives = 5;
    // 走真实的导演计时器，而不是直接调 collectSupply，保证“周期补给 -> 换线箱”真的接线
    g.director.state = 'waves';
    g.director.script.length = 0;
    g.director.pending.length = 0;
    g.director.waveTimer = 999;
    const tally = {};
    for (let i = 0; i < 200; i++) {
      g.pickups.length = 0;
      p.swapIdle = 40;
      g.director.supplyTimer = 0;
      g.director.update(0.016);
      const type = g.pickups.some((x) => x.type === 'swap') ? 'swap'
        : g.pickups.some((x) => x.type === 'gem') ? 'gems'
          : (g.pickups[0] ? g.pickups[0].type : 'none');
      tally[type] = (tally[type] || 0) + 1;
    }
    g.pickups.length = 0;
    // and every sector clear hands one out once there is somewhere to rotate to
    p.swapIdle = 0;
    g.onBossDefeated({ x: SF.W * 0.5, y: SF.H * 0.3, def: { color: '#fff' } });
    const loot = g.pickups.map((x) => x.type);
    const xs = g.pickups.map((x) => x.x);
    const offscreen = xs.filter((x) => x < 20 || x > SF.W - 20).length;
    g.pickups.length = 0;
    return { tally, hasSwap: loot.indexOf('swap') >= 0, lootCount: loot.length, offscreen };
  });
  fail('swap crates are a common mid-run drop',
    (swapDrops.tally.swap || 0) >= 80, swapDrops);
  fail('every sector clear hands out a swap crate, and the row stays on screen',
    swapDrops.hasSwap && swapDrops.lootCount >= 5 && swapDrops.offscreen === 0, swapDrops);

  // Review pass regressions: the sky used to freeze during play, mashing the swap key
  // farmed resonance, drops expired mid-screen, the result card always read ×1, and a
  // weak slow-mo could downgrade a strong one.
  const review = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    const out = {};
    const bg0 = g.bg.t;
    for (let i = 0; i < 30; i++) g.updatePlaying(1 / 60);
    out.bgAdvance = +(g.bg.t - bg0).toFixed(3);

    for (const k of ['pulse', 'prism', 'swarm']) {
      p.weapons[k].unlocked = true; p.weapons[k].level = 3;
      p.weapons[k].charge = 100; p.weapons[k].detuned = false;
    }
    p.selectWeapon('pulse'); p.resonance = 0; p.resT = 0;
    p.swapIdle = 0;
    p.selectWeapon('prism'); p.selectWeapon('pulse'); p.selectWeapon('prism');
    out.mashRes = p.resonance;
    p.resonance = 0; p.selectWeapon('pulse');
    for (let i = 0; i < 90; i++) p.update(1 / 60, g.input);   // dwell 1.5s
    p.selectWeapon('prism');
    out.timedRes = p.resonance;

    const drop = new SF.Pickup(SF.W * 0.5, -26, 'swap');
    let alive = true;
    for (let i = 0; i < 60 * 30 && alive; i++) alive = drop.update(1 / 60, null);
    out.supplyReach = Math.round(drop.y);

    g.combo = 35; g.maxCombo = 35;
    g.updatePlaying(1 / 60);
    out.peakMult = g.maxMult;
    g.combo = 0;

    g.slowTimer = 0; g.slowScale = 1;
    g.slowmo(3, 0.35); g.slowmo(0.2, 0.9);
    out.slowScale = g.slowScale;

    const h0 = g.hintTime;
    g.boss = new SF.Boss(1);
    for (let i = 0; i < 30; i++) g.updatePlaying(1 / 60);
    out.hintHeld = +(g.hintTime - h0).toFixed(2);
    g.boss = null;
    for (let i = 0; i < 30; i++) g.updatePlaying(1 / 60);
    out.hintResumed = +(g.hintTime - h0).toFixed(2);
    return out;
  });
  fail('the parallax sky advances while playing', review.bgAdvance > 0.3, review);
  fail('mashing swap does not farm resonance, a timed swap still stacks',
    review.mashRes === 0 && review.timedRes === 1, review);
  fail('drops reach the hull before expiring', review.supplyReach > 900, review);
  fail('peak multiplier is kept for the result card', review.peakMult >= 8, review);
  fail('a weak slow-mo cannot downgrade a strong one', review.slowScale === 0.35, review);
  fail('tutorial clock pauses during boss fights and resumes after',
    review.hintHeld === 0 && review.hintResumed > 0.3, review);

  const lowHull = await page.evaluate(() => {
    const g = __SF.game;
    g.lives = 1;
    g.ui.cache.lives = -1;
    g.ui.update(g);
    const inRun = document.body.classList.contains('is-low-hull');
    g.ui.resetRun();
    return { inRun, onMenu: document.body.classList.contains('is-low-hull') };
  });
  fail('the low-hull pulse stops when the run ends',
    lowHull.inRun === true && lowHull.onMenu === false, lowHull);

  // Escorts: one per side, clear of the hull, symmetric no matter how many.
  const escortFormation = await page.evaluate(async () => {
    const g = __SF.game, p = g.player;
    const slot = (i, n) => p.droneSlot(i, n);
    const two = [slot(0, 2), slot(1, 2)];
    const three = [slot(0, 3), slot(1, 3), slot(2, 3)];
    p.drones.length = 0;
    p.x = SF.W * 0.5;
    p.addDrone('gunner'); p.addDrone('lance');
    for (let i = 0; i < 90; i++) p.update(1 / 60, g.input);
    const live = p.drones.map((d) => Math.round(d.x - p.x));
    p.drones.length = 0;
    return {
      two: two.map((s2) => s2.x), three: three.map((s2) => s2.x),
      tailY: three[2].y - three[0].y, live,
    };
  });
  fail('two wingmen fly one on each side, clear of the wings',
    escortFormation.two[0] === -escortFormation.two[1] && Math.abs(escortFormation.two[0]) >= 60
    && escortFormation.live.length === 2 && Math.sign(escortFormation.live[0]) === -Math.sign(escortFormation.live[1])
    && Math.abs(escortFormation.live[0]) > 50, escortFormation);
  fail('a third escort takes the tail slot',
    escortFormation.three[0] === -escortFormation.three[1] && escortFormation.three[2] === 0 && escortFormation.tailY > 20, escortFormation);

  const skies = await page.evaluate(() => {
    const g = __SF.game;
    const hashes = [];
    for (let sec = 1; sec <= 10; sec++) {
      const c = document.createElement('canvas');
      c.width = SF.W; c.height = SF.H;
      const cx = c.getContext('2d');
      g.bg.t = 10;
      g.bg.draw(cx, { mode: 'game', danger: 0, sector: sec });
      const d = cx.getImageData(0, 0, SF.W, SF.H).data;
      let h = 0;
      for (let i = 0; i < d.length; i += 991) h = (h * 31 + d[i]) >>> 0;
      hashes.push(h);
    }
    return { distinct: new Set(hashes).size };
  });
  fail('every sector paints a distinct sky', skies.distinct === 10, skies);

  const hud = await page.evaluate(() => {
    const btn = document.getElementById('btnNova');
    const chip = document.getElementById('weaponChip');
    const strip = document.getElementById('hudBottom');
    const r = btn ? btn.getBoundingClientRect() : null;
    return {
      hasStrip: !!strip, hasRingBtn: !!btn, hasChip: !!chip,
      ringConic: btn ? getComputedStyle(btn, '::before').backgroundImage.includes('conic-gradient') : false,
      novaPctVar: btn ? btn.style.getPropertyValue('--pct') : null,
      legacyGone: !document.getElementById('touchActions') && !document.querySelector('.nova-track'),
      ringBottomGap: r ? Math.round(innerHeight - r.bottom) : null,
      stripH: strip ? Math.round(strip.getBoundingClientRect().height) : null,
    };
  });
  fail('nova charges on the ring itself, legacy cards gone',
    hud.hasRingBtn && hud.ringConic && hud.legacyGone && hud.hasChip, hud);

  // Fixed slots: lives, shield and the resonance badge may light up or go dark, but
  // nothing on the strip is allowed to change width or shift position.
  const stripLock = await page.evaluate(async () => {
    const g = __SF.game;
    const chip = document.getElementById('weaponChip');
    const status = document.querySelector('.dash-status');
    const livesEl = document.getElementById('hudLives');
    const shots = [];
    const snap = (tag) => {
      g.ui.update(g);
      const c = chip.getBoundingClientRect();
      const st = status.getBoundingClientRect();
      shots.push({
        tag,
        chipLeft: +c.left.toFixed(1),
        chipW: +c.width.toFixed(1),
        statusW: +st.width.toFixed(1),
        statusH: +st.height.toFixed(1),
        livesW: +livesEl.getBoundingClientRect().width.toFixed(1),
        slots: livesEl.children.length,
        // A slot that is only a placeholder must actually be invisible. An
        // animation with fill-mode "both" once pinned one back to opacity 1.
        lit: [...livesEl.children].filter((el) => Number(getComputedStyle(el).opacity) > 0.05).length,
      });
    };
    const set = async (lives, shield, res, det) => {
      g.lives = lives;
      g.player.shieldCharges = shield;
      g.player.resonance = res;
      g.player.weapons.pulse.detuned = !!det;
      g.player.weapons.pulse.charge = det ? 47 : 100;
      // hull slots fade over .3s; read them once the transition has settled
      await new Promise((r) => setTimeout(r, 420));
      snap('L' + lives + '/S' + shield + '/R' + res + (det ? '/DET' : ''));
    };
    await set(3, 0, 0);
    await set(1, 0, 0);
    await set(3, 3, 0);
    await set(5, 3, 4);
    await set(2, 1, 3, true);
    const ring = document.getElementById('btnNova').getBoundingClientRect();
    const st = document.querySelector('.dash-status').getBoundingClientRect();
    return { shots, aligned: Math.abs((st.top + st.height / 2) - (ring.top + ring.height / 2)) < 1.5, stripBottom: +st.bottom.toFixed(1), inner: innerHeight };
  });
  const geom = stripLock.shots;
  const same = (k) => new Set(geom.map((g) => g[k])).size === 1;
  fail('lives/shield occupy fixed rows: no reflow as states change',
    same('chipLeft') && same('chipW') && same('statusW') && same('livesW') && same('slots') && geom[0].slots === 5, stripLock);
  fail('only revealed hull slots are visible',
    JSON.stringify(geom.map((g) => g.lit)) === JSON.stringify([3, 3, 3, 5, 3]), stripLock);
  fail('the status stack sits on the same centre line as the action rings',
    stripLock.aligned && geom[0].statusH >= 28 && geom[0].statusH <= 36, stripLock);

  // Picking a difficulty rewrites the note under the picker. The card, and the start
  // button below it, must not slide when the wording changes length.
  const diffNote = await page.evaluate(() => {
    const g = __SF.game;
    g.ui.showScreen('menu');
    const note = document.getElementById('diffNote');
    const btn = document.getElementById('btnStart');
    const card = document.querySelector('.menu-wrap');
    const seen = {};
    for (const id of ['cruise', 'standard', 'abyss']) {
      g.setSetting('difficulty', id);
      g.ui.applySettings();
      const n = note.getBoundingClientRect();
      seen[id] = {
        noteH: +n.height.toFixed(1),
        startTop: +btn.getBoundingClientRect().top.toFixed(1),
        cardH: +card.getBoundingClientRect().height.toFixed(1),
      };
    }
    g.setSetting('difficulty', 'standard');
    g.ui.applySettings();
    return seen;
  });
  // Type and layout of the redesigned strip: one button module, no label may be
  // ellipsized (the vent readout once was), and the stage rail must sit in the gap
  // without either end of the strip overlapping it.
  const stripType = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    const name = document.getElementById('weaponName');
    const chip = document.getElementById('weaponChip').getBoundingClientRect();
    const rail = document.querySelector('.dash-progress').getBoundingClientRect();
    const right = document.querySelector('.dash-right').getBoundingClientRect();
    const clipped = [];
    for (const k of ['pulse', 'prism', 'swarm']) {
      p.weapons[k].unlocked = true;
      for (let lv = 1; lv <= 5; lv++) {
        p.weapons[k].level = lv;
        p.activeWeapon = k; p.level = lv;
        p.weapons[k].detuned = false; p.weapons[k].charge = 100;
        g.ui.cache.weapon = -1;
        g.ui.update(g);
        if (name.scrollWidth > name.clientWidth + 0.5) clipped.push(k + 'L' + lv + '=' + name.textContent);
      }
      p.weapons[k].detuned = true; p.weapons[k].charge = 41;
      g.ui.cache.weaponDetuned = false;
      g.ui.update(g);
      if (name.textContent !== '失谐排气' || name.scrollWidth > name.clientWidth + 0.5) {
        clipped.push(k + ' vent=' + name.textContent);
      }
      p.weapons[k].detuned = false;
    }
    const sizes = ['btnFocus', 'btnDash', 'btnNova'].map((id) => {
      const r = document.getElementById(id).getBoundingClientRect();
      return r.width + 'x' + r.height;
    });
    const captions = ['btnFocus', 'btnDash', 'btnNova'].map((id) => Number(getComputedStyle(document.getElementById(id).querySelector('small')).fontSize.replace('px', '')));
    return {
      clipped, sizes, captions,
      overlap: chip.right > rail.left + 0.5 || rail.right > right.left + 0.5,
      novaReady: document.getElementById('novaPct').textContent,
    };
  });
  fail('every weapon label fits the pill without ellipsis',
    stripType.clipped.length === 0 && new Set(stripType.sizes).size === 1
    && Math.min(...stripType.captions) >= 7 && !stripType.overlap, stripType);

  const rail = await page.evaluate(() => {
    const g = __SF.game;
    const el = document.getElementById('dashProgress');
    // the director's wave script is per-sector and may be empty right now, so give it
    // a six-wave one and put the real one back at the end
    const savedScript = g.director.script;
    g.director.script = [{ after: 2 }, { after: 2 }, { after: 2 }, { after: 2 }, { after: 2 }, { after: 2 }];
    const read = () => Math.round(Number(getComputedStyle(el).getPropertyValue('--p')) * 100);
    g.director.state = 'waves'; g.director.waveIndex = 0;
    g.ui.cache.progress = -1; g.ui.update(g);
    const start = read();
    g.director.waveIndex = 3;
    g.ui.cache.progress = -1; g.ui.update(g);
    const mid = read();
    g.director.state = 'boss';
    g.ui.cache.progress = -1; g.ui.update(g);
    const boss = { p: read(), cls: el.className };
    // the lull after a clear must not snap an earned bar back to empty
    g.director.state = 'idle';
    g.ui.cache.progress = -1; g.ui.update(g);
    const held = read();
    g.director.state = 'waves'; g.director.waveIndex = 0;
    g.director.script = savedScript;
    return { start, mid, boss, held, total: 6 };
  });
  fail('the stage rail tracks the sector timeline and lights the boss marker',
    rail.start === 0 && rail.mid > 40 && rail.mid < 90
    && rail.boss.p === 100 && rail.boss.cls.includes('is-boss') && rail.held === 100, rail);

  // The same class of bug the strip had: a readout that grows with its content and
  // drags the rest of the HUD along. Anchors are checked at their extremes — score
  // digits, combo, and every boss label — left-anchored items may only grow right,
  // right-anchored items only left, and the boss row may not change height at all.
  const topLock = await page.evaluate(() => {
    const g = __SF.game;
    const box = (id) => document.getElementById(id).getBoundingClientRect();
    const grab = () => ({
      score: [+box('hudScore').left.toFixed(1), +box('hudScore').top.toFixed(1)],
      best: [+box('hudBest').left.toFixed(1), +box('hudBest').top.toFixed(1)],
      graze: [+box('hudGraze').left.toFixed(1), +box('hudGraze').top.toFixed(1)],
      comboRight: +document.querySelector('.combo-box').getBoundingClientRect().right.toFixed(1),
      comboTop: +document.querySelector('.combo-mult').getBoundingClientRect().top.toFixed(1),
      metaH: +document.querySelector('.boss-meta').getBoundingClientRect().height.toFixed(1),
      trackTop: +document.querySelector('.boss-track').getBoundingClientRect().top.toFixed(1),
      nameLeft: +box('bossName').left.toFixed(1),
      phaseTop: +box('bossPhase').top.toFixed(1),
      phaseH: +box('bossPhase').height.toFixed(1),
      wrapped: [...document.querySelectorAll('.boss-meta > *')].filter((el) => el.getBoundingClientRect().height > 18).length,
    });
    const shots = [];
    const snap = (tag) => { g.ui.update(g); shots.push(Object.assign({ tag }, grab())); };
    const realBoss = g.boss;
    g.boss = { name: '门神', title: '星门守望者', en: 'GATEKEEPER', phase: 1, maxHp: 400, hp: 400, attack: { t: 0 }, attackLabel: '扇形齐射', mercy: 0, sector: 1, desperate: false };
    g.combo = 12; g.multiplier = () => 1.4;
    snap('base');
    for (const [score, grazes, combo] of [[0, 0, 1], [9, 7, 87], [99, 999, 999], [1234, 8, 3]]) {
      g.score = score * 100000 + grazes * 137;
      g.grazes = grazes;
      g.combo = combo;
      g.ui.cache.mult = -1;
      g.multiplier = () => (combo > 500 ? 12 : 1 + combo / 100);
      snap('S' + score + 'G' + grazes + 'C' + combo);
    }
    for (const [name, title, en, phase, desperate, label] of [
      ['门神', '星门守望者', 'GATEKEEPER', 1, false, '扇形齐射'],
      ['织时者', '克洛诺斯回响', 'CHRONOWEAVER', 3, true, '预告 · 逆向双旋'],
      ['赫利俄斯', '终焉', 'HELIOS', 2, false, '核心重载'],
      ['赫利俄斯', '终焉', 'HELIOS', 3, true, '蓄招'],
    ]) {
      g.boss.name = name; g.boss.title = title; g.boss.en = en;
      g.boss.phase = phase; g.boss.desperate = desperate;
      g.boss.attackLabel = label; g.boss.attack = { t: 0 }; g.boss.mercy = 0;
      g.ui.cache.bossName = '';
      g.ui.cache.bossPhase = 0;
      snap(name + 'P' + phase + (desperate ? 'D' : ''));
    }
    g.boss = realBoss;
    g.combo = 0;
    delete g.multiplier;
    const keys = ['score', 'best', 'graze', 'comboRight', 'comboTop', 'metaH', 'trackTop', 'nameLeft', 'phaseH'];
    const moved = keys.map((k) => ({ k, variants: [...new Set(shots.map((r) => JSON.stringify(r[k])))] }))
      .filter((m) => m.variants.length > 1);
    return { moved, rows: shots.length, wrapped: Math.max(...shots.map((r) => r.wrapped)), phaseTops: [...new Set(shots.map((r) => r.phaseTop))] };
  });
  fail('no HUD readout drags its neighbours when its content grows',
    topLock.moved.length === 0 && topLock.wrapped === 0 && topLock.phaseTops.length <= 2, topLock);

  fail('difficulty note never resizes the menu card',
    new Set(Object.values(diffNote).map((d) => d.cardH)).size === 1
    && new Set(Object.values(diffNote).map((d) => d.startTop)).size === 1, diffNote);

  const clampY = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    p.y = SF.H - 10;
    p.update(1 / 60, g.input);
    return {
      maxY: Math.round(p.y),
      floor: SF.H - 104,
      stripH: Math.round(document.getElementById('hudBottom').getBoundingClientRect().height),
    };
  });
  fail('the hull stays clear of the instrument strip',
    clampY.maxY <= clampY.floor + 2 && clampY.stripH <= 70, clampY);

  /* ---------- 4. weapon levels are worth something ---------- */
  const dps = {};
  for (const weapon of ['pulse', 'prism', 'swarm']) {
    for (const level of [1, 3, 5]) {
      const dmg = await page.evaluate(({ weapon, level }) => {
        const g = __SF.game, p = g.player;
        g.enemies.length = 0; g.playerBullets.length = 0; g.enemyBullets.length = 0;
        p.drones.length = 0;
        p.weapons[weapon].unlocked = true;
        p.selectWeapon(weapon);
        p.level = level; p.weapons[weapon].level = level; p.overdrive = 0;
        p.resonance = 0; p.resT = 0;
        for (const k of ['pulse', 'prism', 'swarm']) { p.weapons[k].charge = 100; p.weapons[k].detuned = false; }
        __SF.startBoss();
        const waitBoss = async () => {
          for (let i = 0; i < 60 && !g.boss; i++) await new Promise(r => setTimeout(r, 50));
          return g.boss;
        };
        return waitBoss().then((b) => {
          b.entering = false; b.y = b.targetY;
          b.maxHp = b.hp = 1e9;
          b.hit = function (d) { if (!this.entering && !this.dying) this.hp -= d; };
          b.update = function () {};
          p.x = SF.W * 0.5; p.y = SF.H * 0.8;
          const before = b.hp;
          const dt = 1 / 60;
          for (let i = 0; i < 300; i++) {
            p.update(dt, g.input);
            for (let j = g.playerBullets.length - 1; j >= 0; j--) if (!g.playerBullets[j].update(dt, p)) g.playerBullets.splice(j, 1);
            g.collide();
          }
          const out = Math.round(before - b.hp);
          g.boss = null;
          return { weapon, level, dmg5s: out, dps: +(out / 5).toFixed(1) };
        });
      }, { weapon, level });
      dps[weapon + '_L' + level] = dmg;
    }
  }
  console.log('\nDPS 5s window');
  for (const k of Object.keys(dps)) console.log('  ' + k.padEnd(12) + JSON.stringify(dps[k]));
  const l1 = ['pulse', 'prism', 'swarm'].map(w => dps[w + '_L1'].dps);
  const l5 = ['pulse', 'prism', 'swarm'].map(w => dps[w + '_L5'].dps);
  fail('levels scale damage', l5.every((v, i) => v > l1[i] * 1.35), { lv1: l1, lv5: l5 });
  fail('no weapon is a dead pick', Math.max(...l5) / Math.min(...l5) < 2.2, { spread: +(Math.max(...l5) / Math.min(...l5)).toFixed(2), lv5: l5 });

  /* ---------- 5. every boss spell fires ---------- */
  await page.evaluate(() => {
    __SF.startBoss();
  });
  await page.waitForTimeout(3200);
  const allAttacks = await page.evaluate(() => Object.keys(SF.ATTACK_LABELS));
  const spellReport = [];
  for (const name of allAttacks) {
    const r = await page.evaluate((n) => {
      const g = __SF.game;
      g.enemyBullets.length = 0; g.lasers.length = 0;
      g.boss.maxHp = g.boss.hp = 1e9;
      g.boss.entering = false; g.boss.dying = false;
      g.boss.y = g.boss.targetY;
      g.boss.attack = null;
      g.boss.startAttack(n);
      const dt = 1 / 60;
      let lasers = 0, fired = 0, firedAt = null;
      const seen = new Set();
      const eBefore = g.enemies.length;
      for (let i = 0; i < 240; i++) {
        g.boss.update(dt);
        for (let j = g.enemyBullets.length - 1; j >= 0; j--) {
          const b = g.enemyBullets[j];
          if (!seen.has(b)) { seen.add(b); fired++; if (firedAt === null) firedAt = i; }
          if (!b.update(dt, g.player)) g.enemyBullets.splice(j, 1);
        }
        for (let j = g.enemies.length - 1; j >= 0; j--) if (!g.enemies[j].update(dt, g.player)) g.enemies.splice(j, 1);
        for (const l of g.lasers) lasers = Math.max(lasers, g.lasers.length);
        for (let j = g.lasers.length - 1; j >= 0; j--) if (!g.lasers[j].update(dt)) g.lasers.splice(j, 1);
      }
      const kinds = {};
      for (const b of g.enemyBullets) kinds[b.kind] = (kinds[b.kind] || 0) + 1;
      const spawned = Math.max(0, g.enemies.length - eBefore);
      const out = {
        spell: n, label: SF.ATTACK_LABELS[n], fired, firedAt, live: g.enemyBullets.length,
        kinds, lasers, spawned,
        state: { attack: g.boss.attack && g.boss.attack.name, phase: g.boss.phase, entering: g.boss.entering, dying: g.boss.dying },
      };
      g.enemies.length = 0;
      return out;
    }, name);
    spellReport.push(r);
    console.log('  BOSS ' + name.padEnd(14) + JSON.stringify(r));
  }
  const dead = spellReport.filter(r => r.fired === 0 && r.lasers === 0 && r.spawned === 0);
  fail('all ' + allAttacks.length + ' spells fire', dead.length === 0, dead);
  await page.screenshot({ path: path.join(SHOTS, '220-boss-spells.png') });

  /* ---------- 6. rotation, mercy and phase pressure ---------- */
  const rotation = await page.evaluate(() => {
    const g = __SF.game, b = g.boss;
    g.enemyBullets.length = 0; g.lasers.length = 0; g.enemies.length = 0;
    b.maxHp = b.hp = 1e9; b.phase = 1; b.seqIdx = 0; b.entering = false; b.dying = false;
    b.y = b.targetY; b.attack = null; b.attackCd = 0.2; b.desperate = false;
    const dt = 1 / 60;
    // 20s per HP tier: full health, phase 2, phase 3, desperation
    const tiers = [9e8, 6e8, 2.5e8, 1e8];
    const order = [];
    let lastCount = -1, mercy = null;
    const fired = { 1: 0, 2: 0, 3: 0 };
    const peak = { 1: 0, 2: 0, 3: 0 };
    const secs = { 1: 0, 2: 0, 3: 0 };
    const seenB = new Set();
    for (let i = 0; i < 60 * 80; i++) {
      b.hp = tiers[Math.min(3, (i / 1200) | 0)];
      const ph = b.phase;
      b.update(dt);
      if (b.spellCount !== lastCount) { lastCount = b.spellCount; if (b.attack) order.push(b.attack.name); }
      if (b.mercy > 1.1 && mercy === null) mercy = { phase: b.phase, liveAfterClear: g.enemyBullets.length };
      for (let j = g.enemyBullets.length - 1; j >= 0; j--) {
        const bl = g.enemyBullets[j];
        if (!seenB.has(bl)) { seenB.add(bl); fired[ph] = (fired[ph] || 0) + 1; }
        if (!bl.update(dt, g.player)) g.enemyBullets.splice(j, 1);
      }
      peak[b.phase] = Math.max(peak[b.phase] || 0, g.enemyBullets.length);
      secs[b.phase] = (secs[b.phase] || 0) + dt;
      for (let j = g.enemies.length - 1; j >= 0; j--) if (!g.enemies[j].update(dt, g.player)) g.enemies.splice(j, 1);
      for (let j = g.lasers.length - 1; j >= 0; j--) if (!g.lasers[j].update(dt)) g.lasers.splice(j, 1);
    }
    const rate = {};
    for (const k of Object.keys(fired)) rate[k] = +(fired[k] / Math.max(1, secs[k])).toFixed(1);
    return { order: order.slice(0, 12), finalPhase: b.phase, desperate: b.desperate, mercy, fireRatePerSec: rate, peakLive: peak };
  });
  console.log('\nROTATION ' + JSON.stringify(rotation));
  const expected = await page.evaluate(() => {
    const b = __SF.game.boss;
    return { def: b.def.phases, id: b.type };
  });
  const rot1 = rotation.order.slice(0, expected.def[0].length);
  const seqOk = rot1.every((n, i) => n === expected.def[0][i % expected.def[0].length]);
  fail('phase 1 rotation is scripted', seqOk, { boss: expected.id, seen: rot1, want: expected.def[0] });
  fail('phase change clears the board', !!rotation.mercy && rotation.mercy.liveAfterClear <= 4, rotation.mercy);
  const rate = rotation.fireRatePerSec;
  fail('pressure rises per phase', rate[2] > rate[1] && rate[3] >= rate[2], rotation.fireRatePerSec);
  fail('desperation overload fires', rotation.desperate === true, { desperate: rotation.desperate, phase: rotation.finalPhase });

  /* ---------- 7. a real fight ends in a sane window ---------- */
  async function fightRun(label, cfg) {
    const out = await page.evaluate((cfg) => {
      const g = __SF.game, p = g.player;
      g.boss = null; g.enemies.length = 0; g.enemyBullets.length = 0; g.playerBullets.length = 0; g.lasers.length = 0;
      g.sector = cfg.sector;
      __SF.startBoss();
      const waitBoss = async () => {
        for (let i = 0; i < 60 && !g.boss; i++) await new Promise(r => setTimeout(r, 50));
        return g.boss;
      };
      return waitBoss().then((b) => {
        b.entering = false; b.y = b.targetY;
        p.drones.length = 0;
        cfg.drones.forEach(k => p.addDrone(k));
        p.x = SF.W * 0.5; p.y = SF.H * 0.82;
        b.x = SF.W * 0.5;
        for (const w of Object.keys(cfg.levels)) { p.weapons[w].unlocked = true; p.weapons[w].level = cfg.levels[w]; }
        p.selectWeapon(cfg.weapon); p.level = cfg.levels[cfg.weapon];
        const dt = 1 / 60;
        let t = 0, hits = 0;
        g.playerHit = () => { hits++; };          // immortal rig: only measure the clock
        while (!b.dying && t < 120) {
          // rotate lines the way the charge economy demands
          const cur = p.weapons[p.activeWeapon];
          if (cur.detuned || cur.charge < 22) {
            let best = null;
            for (const k of p.unlockedWeapons()) {
              if (k !== p.activeWeapon && p.weapons[k].charge > 55 && (!p.weapons[k].detuned)) {
                if (!best || p.weapons[k].charge > p.weapons[best].charge) best = k;
              }
            }
            if (best) p.selectWeapon(best);
          }
          p.x = SF.W * 0.5; p.y = SF.H * 0.82;      // stationary rig: pure race-against-clock
          p.update(dt, g.input); b.update(dt); t += dt;
          for (let j = g.enemyBullets.length - 1; j >= 0; j--) if (!g.enemyBullets[j].update(dt, p)) g.enemyBullets.splice(j, 1);
          for (let j = g.playerBullets.length - 1; j >= 0; j--) if (!g.playerBullets[j].update(dt, p)) g.playerBullets.splice(j, 1);
          for (let j = g.enemies.length - 1; j >= 0; j--) if (!g.enemies[j].update(dt, p)) g.enemies.splice(j, 1);
          for (let j = g.lasers.length - 1; j >= 0; j--) if (!g.lasers[j].update(dt)) g.lasers.splice(j, 1);
          g.collide();
        }
        return { maxHp: Math.round(b.maxHp), seconds: +t.toFixed(1), kills: b.dying, phaseReached: b.phase, hullHits: hits };
      });
    }, cfg);
    console.log('  FIGHT ' + label.padEnd(22) + JSON.stringify(out));
    return out;
  }
  const fresh = await fightRun('S1 rookie L2, no wingmen', { sector: 1, weapon: 'pulse', levels: { pulse: 2 }, drones: [] });
  const mid = await fightRun('S3 mid, 3 lines L3 + 2 wingmen', { sector: 3, weapon: 'pulse', levels: { pulse: 3, prism: 3, swarm: 3 }, drones: ['gunner', 'lance'] });
  const maxed = await fightRun('S5 maxed + 3 wingmen', { sector: 5, weapon: 'prism', levels: { pulse: 5, prism: 5, swarm: 5 }, drones: ['gunner', 'lance', 'aegis'] });
  fail('rookie sees all 3 phases in 15-60s', fresh.kills && fresh.seconds > 15 && fresh.seconds < 60 && fresh.phaseReached >= 3, fresh);
  fail('mid build fight is 20-65s', mid.kills && mid.seconds > 20 && mid.seconds < 65 && mid.phaseReached >= 3, mid);
  fail('maxed build fight is 20-60s', maxed.kills && maxed.seconds > 20 && maxed.seconds < 60 && maxed.phaseReached >= 3, maxed);

  /* ---------- 8. an idle hull actually dies ---------- */
  const risk = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    g.boss = null; g.enemies.length = 0; g.enemyBullets.length = 0; g.playerBullets.length = 0; g.lasers.length = 0;
    g.godMode = false;
    __SF.startBoss();
    const waitBoss = async () => {
      for (let i = 0; i < 60 && !g.boss; i++) await new Promise(r => setTimeout(r, 50));
      return g.boss;
    };
    return waitBoss().then((b) => {
      b.entering = false; b.y = b.targetY;
      b.maxHp = 1e9;                              // pin the hull to the desperation window
      p.drones.length = 0; p.shoot = () => {};    // measure the boss alone
      let hits = 0;
      g.playerHit = function () { hits++; p.grantInvuln(0.2); };
      const dt = 1 / 60;
      for (let i = 0; i < 60 * 30; i++) {
        p.x = SF.W * 0.5; p.y = SF.H * 0.82;      // a player who never dodges
        p.invuln = 0; p.shieldCharges = 0; p.reflectT = 0; p.dashT = 0;
        b.hp = 1e8;                               // 10% => phase 3, then overload
        b.update(dt);
        for (let j = g.enemyBullets.length - 1; j >= 0; j--) if (!g.enemyBullets[j].update(dt, p)) g.enemyBullets.splice(j, 1);
        for (let j = g.lasers.length - 1; j >= 0; j--) if (!g.lasers[j].update(dt)) g.lasers.splice(j, 1);
        g.collide();
      }
      g.godMode = true;
      return { seconds: 30, hitsWhileStationary: hits, phase: b.phase, desperate: b.desperate };
    });
  });
  console.log('RISK ' + JSON.stringify(risk));
  fail('boss punishes a hull that never dodges', risk.hitsWhileStationary >= 4, risk);

  /* ---------- 9. precise mode, graze economy, clear bonus, result card ---------- */
  const precise = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    delete p.shoot;                       // earlier rigs stubbed it out
    p.x = SF.W * 0.5; p.y = SF.H * 0.8;
    p.weapons.pulse.unlocked = true; p.selectWeapon('pulse');
    p.level = 5; p.weapons.pulse.level = 5;
    p.weapons.pulse.charge = 100; p.weapons.pulse.detuned = false;
    p.overdrive = 0; p.resonance = 0; p.invuln = 9999;
    const spread = () => {
      g.playerBullets.length = 0;
      p.shoot();
      return Math.round(Math.max(...g.playerBullets.map((b) => Math.abs(b.x - p.x))));
    };
    p.focus = false;
    const wide = spread();
    p.focus = true;
    const tight = spread();
    p.focus = false;
    g.playerBullets.length = 0;
    return { wide, tight };
  });
  fail('precise mode tightens the pattern', precise.tight > 0 && precise.tight < precise.wide * 0.6, precise);

  const graze = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    p.x = SF.W * 0.5; p.y = SF.H * 0.6;
    p.invuln = 0; p.dashT = 0;
    g.grazes = 0; g.score = 0;
    g.enemyBullets.length = 0;
    g.enemyBullets.push(new SF.Bullet(p.x + 16, p.y, 0, 0, { kind: 'orb', r: 5, life: 4 }));
    const before = g.grazes;
    g.collide();
    if (g.uiReady) g.ui.update(g);
    return {
      gained: g.grazes - before,
      hud: document.getElementById('hudGraze').textContent,
      score: g.score,
    };
  });
  fail('grazing counts on the HUD and pays score', graze.gained === 1 && graze.hud === '1' && graze.score >= 4, graze);

  const clear = await page.evaluate(() => {
    const g = __SF.game;
    g.sector = 2; g.score = 0;
    g.sectorHits = 0; g.sectorGrazes = 40;
    g.sectorStartTime = g.runTime - 30;
    g.pickups.length = 0;
    g.onBossDefeated({ x: SF.W * 0.5, y: SF.H * 0.3, def: { color: '#fff' } });
    const el = document.getElementById('announce');
    return {
      gained: g.score,
      title: document.getElementById('announceTitle').textContent,
      sub: document.getElementById('announceSub').textContent,
      bottom: el.classList.contains('is-bottom'),
    };
  });
  fail('sector clear pays time/clean/graze bonuses in a bottom banner',
    clear.gained > 9000 && clear.title === 'SECTOR CLEAR' && clear.sub.includes('无伤')
    && clear.sub.includes('擦弹') && clear.bottom, clear);

  const result = await page.evaluate(() => {
    const g = __SF.game;
    g.grazes = 123; g.novasFired = 2; g.hitsTaken = 1;
    g.player.weapons.prism.unlocked = true;
    g.player.drones.length = 0;
    g.player.addDrone('lance');
    g.gameOver();
    const q = (id) => document.getElementById(id);
    return {
      graze: q('finalGraze').textContent,
      novas: q('finalNovas').textContent,
      hits: q('finalHits').textContent,
      mult: q('finalMult').textContent,
      rank: document.querySelector('#resultRank b').textContent,
      rule: q('rankRule').textContent,
      build: q('resultBuild').textContent,
      overflow: document.documentElement.scrollHeight > innerHeight + 1,
    };
  });
  fail('result card shows graze/nova/hits/rank/build',
    result.graze === '123' && result.novas === '2' && result.hits === '1' && result.mult.startsWith('×')
    && ['S', 'A', 'B', 'C'].includes(result.rank) && result.rule.length > 4
    && result.build.includes('脉冲') && result.build.includes('僚机') && !result.overflow, result);

  /* ---------- 10. boss tell, music mood/phase, difficulty tiers ---------- */
  const tell = await page.evaluate(() => {
    const g = __SF.game;
    g.boss = null; g.lasers.length = 0; g.enemyBullets.length = 0;
    g.sector = 1; g.settings.difficulty = 'standard'; g.diff = SF.DIFFICULTY.standard;
    g.bossWarnTimer = 0;
    g.spawnBoss();
    const b = g.boss;
    b.entering = false; b.y = b.targetY;
    b.maxHp = b.hp = 1e9;
    b.attack = null;
    b.startAttack('fan');
    const atStart = { t: +b.attack.t.toFixed(2), label: b.attackLabel, bullets: g.enemyBullets.length, tellColor: b.tellColor };
    const dt = 1 / 60;
    for (let i = 0; i < 20; i++) b.update(dt);          // ~0.33s into the charge
    const mid = { bullets: g.enemyBullets.length, label: b.attackLabel };
    for (let i = 0; i < 30; i++) b.update(dt);          // past the tell
    const after = { bullets: g.enemyBullets.length, label: b.attackLabel, telegraphing: b.telegraphing };
    return { atStart, mid, after };
  });
  fail('boss charges visibly before every spell',
    tell.atStart.t < 0 && tell.atStart.label.startsWith('预告') && tell.mid.bullets === 0
    && tell.after.bullets > 0 && tell.after.label === '扇形齐射' && tell.after.telegraphing === false, tell);

  const audio = await page.evaluate(() => {
    const g = __SF.game;
    const before = { mood: SF.Audio.mood, intensity: SF.Audio.intensity };
    g.onBossPhase({ phase: 3 });
    const boss3 = { intensity: SF.Audio.intensity, mood: SF.Audio.mood };
    g.onBossDesperation({ x: 0, y: 0, size: 40, def: { color: '#fff' }, phase: 3 });
    const desperate = SF.Audio.intensity;
    g.advanceSector();
    const waves = { mood: SF.Audio.mood, intensity: SF.Audio.intensity };
    return { before, boss3, desperate, waves };
  });
  fail('music follows boss phase and sector',
    audio.boss3.intensity === 3 && audio.boss3.mood === 'boss' && audio.desperate === 3 && audio.waves.mood === 'waves', audio);

  const diff = await page.evaluate(() => {
    const g = __SF.game;
    const probe = () => {
      g.enemies.length = 0;
      const e = g.spawnEnemy('drifter', SF.W * 0.5, SF.H * 0.3, { sector: 1 });
      const boss = new SF.Boss(1);
      return {
        rate: +e.fireTimer === 99 ? null : 0,
        hp: boss.maxHp,
        spd: Math.round(boss.bulletSpeed()),
        scoreMul: g.diff.scoreMul,
        nova: g.diff.nova,
      };
    };
    g.settings.difficulty = 'cruise'; g.setSetting('difficulty', 'cruise');
    const cruise = probe();
    g.setSetting('difficulty', 'abyss');
    const abyss = probe();
    g.setSetting('difficulty', 'standard');
    const standard = probe();
    return { cruise, abyss, standard };
  });
  fail('difficulty scales hulls, boss speed and score',
    diff.abyss.hp > diff.standard.hp && diff.cruise.hp < diff.standard.hp
    && diff.abyss.spd > diff.standard.spd && diff.cruise.spd < diff.standard.spd
    && diff.abyss.scoreMul > 1 && diff.cruise.scoreMul < 1
    && diff.abyss.nova < 1 && diff.cruise.nova > 1, diff);

  /* ---------- 11. 五轮迭代：平衡、掉落保底、相位、词缀、破防、UI ---------- */
  const iter = await page.evaluate(() => {
    const g = __SF.game, p = g.player;
    const out = {};
    const clearFx = () => {
      g.enemyBullets.length = 0;
      g.playerBullets.length = 0;
      g.enemies.length = 0;
      g.pickups.length = 0;
      g.boss = null;
    };
    // --- 掉落保底：连续 4 次补给内必出缺失武器 / 僚机 ---
    const supplyTypes = (n) => {
      const seq = [];
      for (let i = 0; i < n; i++) {
        g.pickups.length = 0;
        g.director.supplyTimer = 0;
        g.director.update(0.016);
        const t = g.pickups.some((x) => x.type === 'swap') ? 'swap'
          : g.pickups.some((x) => x.type === 'gem') ? 'gems'
            : (g.pickups[0] ? g.pickups[0].type : 'none');
        seq.push(t);
      }
      return seq;
    };
    g.director.state = 'waves';
    g.director.script.length = 0;
    g.director.pending.length = 0;
    g.director.waveTimer = 999;
    p.weapons.prism.unlocked = false;
    p.weapons.swarm.unlocked = false;
    p.level = 5;
    p.drones.length = 3;
    p.shieldCharges = 3;
    p.reflectT = 10;
    g.lives = 5;
    p.swapIdle = 0;
    g.director.pityWeapon = 0;
    const weaponSeq = supplyTypes(6);
    out.pityWeapon = { seq: weaponSeq, at: weaponSeq.findIndex((t) => t === 'prism' || t === 'swarm') };

    p.weapons.prism.unlocked = true;
    p.weapons.swarm.unlocked = true;
    p.drones.length = 0;
    p.shieldCharges = 3;
    p.reflectT = 10;
    g.director.pityDrone = 0;
    const droneSeq = supplyTypes(6);
    out.pityDrone = { seq: droneSeq, at: droneSeq.findIndex((t) => t === 'drone') };

    // --- Boss 血量曲线封顶 ---
    const b1 = new SF.Boss(1), b9 = new SF.Boss(9), b12 = new SF.Boss(12);
    // 9 与 12 都是赫利俄斯（同基础血量），用来验证曲线封顶
    out.bossScale = { b1: b1.maxHp, b9: b9.maxHp, b12: b12.maxHp, capped: b9.maxHp === b12.maxHp && b12.maxHp <= SF.BOSS_TYPES[2].hp * 4.5 };

    // --- 棱镜对巨构增伤 ---
    p.level = 5;
    p.weapons.prism.level = 5;
    p.activeWeapon = 'prism';
    p.overdrive = 0;
    g.playerBullets.length = 0;
    p.shoot();
    const lance = g.playerBullets.find((b) => b.kind === 'lance');
    const giant = new SF.Boss(1);
    giant.entering = false;
    giant.maxHp = giant.hp = 1000;
    giant.staggerMax = 1e9;
    const beforeHp = giant.hp;
    giant.hit(lance.dmg * lance.bossBonus);
    out.prism = { bonus: lance.bossBonus, dealt: +(beforeHp - giant.hp).toFixed(2), base: lance.dmg };

    // --- 相位冲刺 ---
    clearFx();
    p.x = SF.W / 2; p.y = SF.H * 0.6; p.invuln = 0; p.dashT = 0.17;
    p.nova = 0; p.overdrive = 0; g.phases = 0;
    const graze0 = g.grazes, score0 = g.score;
    for (let i = 0; i < 6; i++) {
      g.enemyBullets.push(new SF.Bullet(p.x + (i - 2.5) * 5, p.y + 2, 0, 0, { kind: 'orb', r: 6, life: 8 }));
    }
    g.collide();
    out.phase = { phases: g.phases, nova: +p.nova.toFixed(1), grazes: g.grazes - graze0, score: g.score - score0 };

    // --- 超频击杀延时 ---
    clearFx();
    p.overdrive = 3;
    const eOd = g.spawnEnemy('drifter', p.x, SF.H * 0.3, { sector: 1 });
    eOd.entryDelay = 0; eOd.age = 1; eOd.hp = 1;
    g.killEnemy(eOd);
    out.overdrive = { before: 3, after: +p.overdrive.toFixed(2) };

    // --- 精英词缀 ---
    clearFx();
    const armored = g.spawnEnemy('drifter', SF.W * 0.4, SF.H * 0.3, { sector: 5, affix: 'armored' });
    armored.entryDelay = 0; armored.age = 1;
    const hp0 = armored.hp;
    armored.hit(10, armored.x, armored.y);
    const rapid = g.spawnEnemy('drifter', SF.W * 0.5, SF.H * 0.3, { sector: 5, affix: 'rapid' });
    const volatile = g.spawnEnemy('drifter', SF.W * 0.6, SF.H * 0.3, { sector: 5, affix: 'volatile' });
    volatile.entryDelay = 0; volatile.age = 1; volatile.hp = 1;
    g.killEnemy(volatile);
    out.affix = {
      armorLoss: +(hp0 - armored.hp).toFixed(1),
      rapidMul: rapid.fireMul,
      volatileBullet: g.enemyBullets.some((b) => b.kind === 'plasma' && b.fuse > 0 && b.fuse <= 0.5),
    };

    // --- Boss 破防 ---
    clearFx();
    g.sector = 5;
    g.spawnBoss();
    const b = g.boss;
    b.entering = false; b.y = b.targetY;
    b.maxHp = b.hp = 4000;
    b.staggerMax = 100; b.stagger = 0; b.broken = 0; b.breaks = 0;
    g.bossBreaks = 0;
    b.hit(120);
    const hpBeforeBroken = b.hp;
    b.hit(10);
    out.break = {
      broken: b.broken > 0, breaks: b.breaks, attack: b.attack,
      dealt: +(hpBeforeBroken - b.hp).toFixed(2), bossBreaks: g.bossBreaks,
    };
    for (let i = 0; i < 80; i++) b.update(1 / 60);
    out.break.after = b.broken;
    b.broken = 1.2;
    g.boss = b;
    g.ui.update(g);
    out.breakUI = {
      cls: document.getElementById('bossBreak').classList.contains('is-broken'),
      pattern: document.getElementById('bossPattern').textContent,
      pct: document.getElementById('bossBreak').style.getPropertyValue('--b'),
    };
    b.broken = 0;

    // --- 暂停装配摘要 / 星区预告 / 结算新统计 ---
    p.weapons.prism.unlocked = true;
    p.weapons.swarm.unlocked = false;
    p.activeWeapon = 'pulse';
    p.level = 4; p.weapons.pulse.level = 4;
    g.ui.renderBuildSummary();
    out.summary = document.getElementById('buildSummary').textContent;
    g.boss = null;
    g.sector = 2;
    g.advanceSector();
    out.threat = document.getElementById('announceSub').textContent;
    g.ui.showResult({
      score: 1234, sector: 3, kills: 9, maxCombo: 6, time: 61000, grazes: 12,
      novas: 2, phases: 7, breaks: 3, hits: 1, maxMult: 2,
      build: { weapons: [{ k: 'pulse', lv: 4 }], drones: [] }, newRecord: false,
    });
    out.result = {
      phases: document.getElementById('finalPhases').textContent,
      breaks: document.getElementById('finalBreaks').textContent,
    };

    out.contrast = typeof g.highContrast === 'boolean';
    return out;
  });
  fail('pity guarantees a missing weapon within 4 crates', iter.pityWeapon.at >= 0 && iter.pityWeapon.at <= 3, iter.pityWeapon);
  fail('pity guarantees a wingman within 4 crates', iter.pityDrone.at >= 0 && iter.pityDrone.at <= 3, iter.pityDrone);
  fail('boss hp curve stops growing late game', iter.bossScale.capped, iter.bossScale);
  fail('prism lances hit giants harder', iter.prism.bonus >= 1.15 && iter.prism.dealt > iter.prism.base, iter.prism);
  fail('phase dash pays nova and score', iter.phase.phases === 6 && iter.phase.nova > 0 && iter.phase.score > 0 && iter.phase.grazes === 6, iter.phase);
  fail('overdrive kills extend the burst window', iter.overdrive.after > 3, iter.overdrive);
  fail('elite affixes change defense, rate and death', iter.affix.armorLoss === 7.2 && iter.affix.rapidMul < 1 && iter.affix.volatileBullet, iter.affix);
  fail('boss stagger opens a break window', iter.break.broken && iter.break.breaks === 1 && iter.break.attack === null && iter.break.dealt > 13 && iter.break.bossBreaks === 1 && iter.break.after === 0, iter.break);
  fail('boss break shows on the HUD', iter.breakUI.cls && iter.breakUI.pattern.includes('核心破防') && iter.breakUI.pct === '1', iter.breakUI);
  fail('pause summary compares all weapon lines', iter.summary.includes('速射') && iter.summary.includes('贯穿') && iter.summary.includes('追踪') && iter.summary.includes('未解锁'), iter.summary);
  fail('sector intro previews the boss', iter.threat.includes('巨构') && iter.threat.includes('赫利俄斯'), iter.threat);
  fail('result card shows phase and break stats', iter.result.phases === '7' && iter.result.breaks === '3', iter.result);
  fail('high-contrast preference is wired', iter.contrast === true, iter.contrast);

  console.log('\nERRORS', errors.length, errors.slice(0, 6).join('\n---\n'));
  console.log('ASSERTIONS', failures === 0 ? 'ALL PASS' : failures + ' FAILED');
  await browser.close();
  if (failures || errors.length) process.exit(1);
})().catch(e => { console.error('FAIL', e); process.exit(1); });
