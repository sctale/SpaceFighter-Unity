/* ============================================================
   entities.js — player, bullets, enemies, bosses, pickups, fx
   ============================================================ */
(function () {
  'use strict';
  const SF = window.SF;
  const P = SF.PALETTE;
  const TAU = SF.TAU;
  const clamp = SF.clamp;

  const spriteFor = {
    bolt: 'bolt', drone: 'droneBolt', orb: 'orb', shard: 'shard', heavy: 'heavy', homing: 'homing',
    needle: 'needle', mine: 'mine', wave: 'wave', plasma: 'plasma',
    lance: 'lance', micro: 'micro', beam: 'beam',
  };

  /* ---------------- Bullet ---------------- */
  class Bullet {
    constructor(x, y, vx, vy, opts) {
      opts = opts || {};
      this.x = x; this.y = y;
      // 上一帧坐标：碰撞用线段扫掠，低帧率下高速弹不会直接穿过战机/敌机
      this.prevX = x; this.prevY = y;
      this.vx = vx; this.vy = vy;
      this.kind = opts.kind || 'orb';
      this.friendly = !!opts.friendly;
      this.r = opts.r || (this.kind === 'heavy' ? 10 : this.kind === 'bolt' ? 5 : 6);
      this.dmg = opts.dmg || 1;
      // 对巨构目标的额外伤害倍率（棱镜贯穿的武器身份）
      this.bossBonus = opts.bossBonus || 1;
      this.life = opts.life || 8;
      this.maxLife = this.life;
      this.grazed = false;
      this.phased = false;
      this.age = 0;
      this.angle = Math.atan2(vy, vx) + Math.PI / 2;
      this.spin = opts.spin || 0;
      this.seed = Math.random() * TAU;
      this.dead = false;
      this.homing = opts.homing || 0;
      this.turn = opts.turn || 2.4;
      this.lockDelay = opts.lockDelay || 0;
      this.scale = opts.scale || 1;
      this.pierce = opts.pierce || 0;
      this.hitSet = this.pierce > 0 ? new Set() : null;
      this.fuse = opts.fuse || 0;
      this.split = opts.split || 0;
      this.wave = opts.wave || 0;
      this.burstKind = opts.burstKind || 'shard';
      this.tint = opts.tint || null;
      this.mineArmed = false;
      this.trailT = 0;
    }

    findFriendlyTarget() {
      const g = SF.Game;
      if (!g) return null;
      let best = null, bd = 1e9;
      if (g.boss && !g.boss.entering && !g.boss.dying) {
        const d = (g.boss.x - this.x) ** 2 + (g.boss.y - this.y) ** 2;
        if (d < bd) { bd = d; best = g.boss; }
      }
      for (const e of g.enemies) {
        if (!e.targetable()) continue;
        const d = (e.x - this.x) ** 2 + (e.y - this.y) ** 2;
        if (d < bd) { bd = d; best = e; }
      }
      return best;
    }

    burst() {
      if (this.friendly) return;
      if (this.kind === 'mine') {
        const n = this.split || 10;
        const sp = 128 + (SF.Game ? SF.Game.sector * 2 : 0);
        for (let i = 0; i < n; i++) {
          const a = (i / n) * TAU + this.seed * 0.3;
          SF.Game.enemyBullets.push(new Bullet(this.x, this.y, Math.cos(a) * sp, Math.sin(a) * sp, {
            kind: 'shard', r: 5, life: 7, scale: 0.85,
          }));
        }
        SF.Game.fx.ring(this.x, this.y, P.gold, 8, 0.4, 82, 0.75);
        SF.Game.fx.spark(this.x, this.y, P.gold, 6, 110, 0.3);
        SF.Audio.sfx('hit');
      } else if (this.kind === 'plasma') {
        const n = this.split || 6;
        for (let i = 0; i < n; i++) {
          const a = (i / n) * TAU + this.seed;
          SF.Game.enemyBullets.push(new Bullet(this.x, this.y, Math.cos(a) * 105, Math.sin(a) * 105, {
            kind: 'orb', r: 6, life: 7,
          }));
        }
        SF.Game.fx.ring(this.x, this.y, P.violet, 10, 0.45, 90, 0.7);
      }
    }

    update(dt, player) {
      this.age += dt;
      this.prevX = this.x;
      this.prevY = this.y;
      if (this.fuse > 0) {
        this.fuse -= dt;
        if (this.fuse <= 0) { this.burst(); return false; }
      }
      this.life -= dt;
      if (this.life <= 0) {
        if (this.kind === 'plasma') this.burst();
        return false;
      }

      if (this.kind === 'mine') {
        this.vx *= Math.exp(-3.8 * dt);
        this.vy *= Math.exp(-3.8 * dt);
        if (!this.mineArmed && this.fuse < 0.9) {
          this.mineArmed = true;
          if (SF.Game) SF.Game.fx.ring(this.x, this.y, P.gold, 4, 0.25, 34, 0.6);
        }
      } else if (this.wave) {
        const spd = Math.hypot(this.vx, this.vy);
        const cur = Math.atan2(this.vy, this.vx);
        const na = cur + Math.sin(this.age * 7.2 + this.seed) * this.wave * dt;
        this.vx = Math.cos(na) * spd;
        this.vy = Math.sin(na) * spd;
      }

      if (this.homing > 0) {
        if (this.lockDelay > 0) this.lockDelay -= dt;
        let tx = null, ty = null;
        if (this.friendly) {
          if (this.lockDelay <= 0) {
            const t = this.findFriendlyTarget();
            if (t) { tx = t.x; ty = t.y; }
          }
        } else if (player && player.alive) {
          tx = player.x; ty = player.y;
        }
        if (tx !== null) {
          this.homing -= dt;
          const target = Math.atan2(ty - this.y, tx - this.x);
          const cur = Math.atan2(this.vy, this.vx);
          let diff = ((target - cur + Math.PI * 3) % TAU) - Math.PI;
          const maxTurn = this.turn * dt;
          diff = clamp(diff, -maxTurn, maxTurn);
          const speed = Math.hypot(this.vx, this.vy);
          const na = cur + diff;
          this.vx = Math.cos(na) * speed;
          this.vy = Math.sin(na) * speed;
        }
      }

      this.x += this.vx * dt;
      this.y += this.vy * dt;
      this.angle = Math.atan2(this.vy, this.vx) + Math.PI / 2;
      if (this.kind === 'bolt' || this.kind === 'drone' || this.kind === 'lance' || this.kind === 'beam') {
        this.angle = 0;
      }

      this.trailT -= dt;
      if (this.trailT <= 0 && SF.Game && !SF.Game.settings.lowFx) {
        const special = this.kind === 'needle' || this.kind === 'wave' || this.kind === 'plasma'
          || this.kind === 'lance' || this.kind === 'micro' || this.kind === 'beam';
        if (special) {
          this.trailT = this.kind === 'plasma' ? 0.055 : 0.026;
          const col = this.friendly
            ? (this.kind === 'lance' ? P.teal : this.kind === 'beam' ? P.rose : P.gold)
            : (this.kind === 'wave' ? P.violet : this.kind === 'plasma' ? P.violet : P.white);
          const own = this.tint || col;
          SF.Game.fx.ember(
            this.x, this.y, own,
            this.kind === 'plasma' ? 3 : 1.4,
            this.kind === 'plasma' ? 0.34 : 0.17,
            -this.vx * 0.012, -this.vy * 0.012
          );
        }
      }

      const m = 70;
      return this.x > -m && this.x < SF.W + m && this.y > -m && this.y < SF.H + m;
    }

    draw(ctx) {
      const spr = SF.Art.sprites[spriteFor[this.kind] || 'orb'];
      if (!spr) return;
      let a = 0;
      if (this.kind === 'shard' || this.kind === 'needle' || this.kind === 'wave' || this.kind === 'micro') {
        a = this.angle;
      }
      let sc = this.scale;
      if (this.kind === 'mine') sc *= 1 + Math.sin(this.age * 9) * 0.08 + (this.mineArmed ? 0.1 : 0);
      if (this.kind === 'plasma') {
        ctx.save();
        ctx.globalCompositeOperation = 'lighter';
        SF.Art.drawGlow(ctx, this.x, this.y, 30 * sc, P.violet, 0.18);
        ctx.restore();
      }
      if (!this.friendly && SF.Game && SF.Game.highContrast) {
        // 高对比偏好下给敌弹加白色外圈，低视力/强光环境更容易读弹
        const rr = spr._w * 0.5 * sc + 2;
        ctx.save();
        ctx.globalCompositeOperation = 'lighter';
        ctx.strokeStyle = 'rgba(255,255,255,.55)';
        ctx.lineWidth = 1.2;
        ctx.beginPath();
        ctx.arc(this.x, this.y, rr, 0, TAU);
        ctx.stroke();
        ctx.restore();
      }
      SF.Art.drawSprite(ctx, spr, this.x, this.y, a, sc);
    }
  }
  SF.Bullet = Bullet;

  /* ---------------- Particle ---------------- */
  class Particle {
    constructor(x, y, opts) {
      opts = opts || {};
      this.x = x; this.y = y;
      this.vx = opts.vx || 0;
      this.vy = opts.vy || 0;
      this.life = opts.life || 0.6;
      this.maxLife = this.life;
      this.size = opts.size || 3;
      this.grow = opts.grow || 0;
      this.color = opts.color || P.teal;
      this.kind = opts.kind || 'ember';
      this.drag = opts.drag === undefined ? 1.6 : opts.drag;
      this.gravity = opts.gravity || 0;
      this.alpha = opts.alpha === undefined ? 1 : opts.alpha;
      this.rot = Math.random() * TAU;
      this.streak = opts.streak || 1;
    }

    update(dt) {
      this.life -= dt;
      if (this.life <= 0) return false;
      this.x += this.vx * dt;
      this.y += this.vy * dt;
      this.vx *= Math.exp(-this.drag * dt);
      this.vy *= Math.exp(-this.drag * dt);
      this.vy += this.gravity * dt;
      this.size += this.grow * dt;
      return true;
    }

    draw(ctx) {
      const t = Math.max(0, this.life / this.maxLife);
      const alpha = this.alpha * (this.kind === 'smoke' ? t * 0.55 : t);
      ctx.globalAlpha = alpha;
      if (this.kind === 'flash') {
        ctx.save();
        ctx.globalCompositeOperation = 'lighter';
        // 当前 globalAlpha 已经是淡出值，drawGlow 内部会再乘一次，这里传 1
        SF.Art.drawGlow(ctx, this.x, this.y, this.size, this.color, 1);
        ctx.globalAlpha = alpha * 0.9;
        ctx.fillStyle = 'rgba(255,255,255,.85)';
        ctx.beginPath();
        ctx.arc(this.x, this.y, Math.max(1.4, this.size * 0.12), 0, TAU);
        ctx.fill();
        ctx.restore();
      } else if (this.kind === 'ring') {
        ctx.strokeStyle = this.color;
        ctx.lineWidth = 1.6 * t + 0.4;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.size, 0, TAU);
        ctx.stroke();
      } else if (this.kind === 'spark') {
        const speed = Math.hypot(this.vx, this.vy) || 1;
        const nx = this.vx / speed, ny = this.vy / speed;
        ctx.strokeStyle = this.color;
        ctx.lineWidth = Math.max(0.6, this.size * 0.35) * (0.4 + t * 0.8);
        ctx.beginPath();
        ctx.moveTo(this.x, this.y);
        ctx.lineTo(this.x - nx * this.size * this.streak, this.y - ny * this.size * this.streak);
        ctx.stroke();
      } else if (this.kind === 'smoke') {
        ctx.fillStyle = this.color;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.size, 0, TAU);
        ctx.fill();
      } else {
        ctx.fillStyle = this.color;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.size * (0.35 + t * 0.65), 0, TAU);
        ctx.fill();
      }
      ctx.globalAlpha = 1;
    }
  }
  SF.Particle = Particle;

  /* ---------------- Floating text ---------------- */
  class FloatText {
    constructor(x, y, text, opts) {
      opts = opts || {};
      this.x = x; this.y = y;
      this.text = text;
      this.color = opts.color || P.gold;
      this.size = opts.size || 12;
      this.life = opts.life || 0.8;
      this.maxLife = this.life;
      this.vy = opts.vy === undefined ? -46 : opts.vy;
      this.vx = opts.vx || 0;
      this.drift = opts.drift || 0;
      this.outline = opts.outline || 'rgba(4,6,15,.7)';
      this.weight = opts.weight || 600;
    }
    update(dt) {
      this.life -= dt;
      this.x += this.vx * dt;
      this.y += this.vy * dt;
      this.vy *= Math.exp(-2.4 * dt);
      if (this.drift) this.x += Math.sin(this.life * 12) * this.drift * dt;
      return this.life > 0;
    }
    draw(ctx) {
      const t = clamp(this.life / this.maxLife, 0, 1);
      const alpha = t > 0.72 ? (1 - t) / 0.28 : t / 0.72;
      ctx.globalAlpha = clamp(alpha, 0, 1);
      ctx.font = `${this.weight} ${this.size}px "Cascadia Code", "SF Mono", Consolas, monospace`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillStyle = this.outline;
      ctx.fillText(this.text, this.x + 1, this.y + 1);
      ctx.fillStyle = this.color;
      ctx.fillText(this.text, this.x, this.y);
      ctx.globalAlpha = 1;
    }
  }
  SF.FloatText = FloatText;

  /* ---------------- Weapon lines ---------------- */
  // The classic speed/power triangle: the fast generalist bites the least, the
  // slow piercing lance bites the hardest, seekers sit in the middle. Each line
  // also carries the hull tint and the wingmen's ammo form, so swapping weapons
  // changes how the whole flight looks and behaves.
  const WEAPON_LINES = {
    pulse: {
      id: 'pulse', label: '脉冲', tint: P.teal, deep: P.tealDeep,
      speed: 1250, drain: 7.4, recover: 15.5, droneAmmo: 'drone',
    },
    prism: {
      id: 'prism', label: '棱镜', tint: P.rose, deep: P.roseDeep,
      speed: 560, drain: 10.5, recover: 12, droneAmmo: 'beam',
    },
    swarm: {
      id: 'swarm', label: '蜂群', tint: P.gold, deep: P.goldDeep,
      speed: 830, drain: 8.6, recover: 13.5, droneAmmo: 'micro',
    },
  };
  SF.WEAPON_LINES = WEAPON_LINES;
  // Charge is spent while a line fires and refills while it rests. Running a
  // line dry detunes it (weaker and slower) until it has been recharged, so
  // holding one weapon forever is never the right play. Resonance rewards rotation.
  const RESONANCE_MAX = 5;
  const RESONANCE_WINDOW = 7;
  // Re-arm rules, with hysteresis. Autofire never stops, so a line that runs dry
  // while held has to vent on its own: it climbs back to the TOP (100) before the
  // gun re-arms, so the ring makes one clean sweep instead of a 0→30 sawtooth that
  // re-detunes the moment it fires. A rested (holstered) line is already off the
  // trigger, so it comes back as soon as it holds a real charge.
  const DETUNE_REARM = 100;
  const DETUNE_CLEAR = 55;
  // Minimum time on a line before a swap may stack resonance: mashing the swap
  // key used to farm the whole chain in a second.
  const SWAP_RESONANCE_GAP = 1.2;
  // Vent speed is tied to drain, so every line spends ~1/3 of its cycle rearming
  // instead of one line being punished far harder than another.
  const VENT_RATIO = 2.4;

  /* ---------------- Player ---------------- */
  class Player {
    constructor() {
      this.x = SF.W / 2;
      this.y = SF.H - 130;
      this.w = 46; this.h = 54;
      this.alive = true;
      this.level = 2;
      this.activeWeapon = 'pulse';
      this.weapons = this.freshWeapons();
      this.resonance = 0;
      this.resT = 0;
      this.swapIdle = 0;
      this.shieldCharges = 1;
      this.reflectT = 0;
      this.drones = [];
      this.fireTimer = 0;
      this.focus = false;
      this.invuln = 1.6;
      this.bank = 0;
      this.t = 0;
      this.nova = 0;
      this.overdrive = 0;
      this.dashT = 0;
      this.dashCd = 0;
      this.dashDirX = 0;
      this.dashDirY = -1;
      this.dragging = false;
      this.dragDX = 0;
      this.dragDY = 0;
      this.lastMX = 0; this.lastMY = 0;
      this.after = [];
      this.muzzle = 0;
      this.hitFlash = 0;
      this.trailTimer = 0;
    }

    freshWeapons() {
      return {
        pulse: { level: 2, unlocked: true, charge: 100, detuned: false },
        prism: { level: 1, unlocked: false, charge: 100, detuned: false },
        swarm: { level: 1, unlocked: false, charge: 100, detuned: false },
      };
    }

    get hitR() { return 4.6; }
    get grazeR() { return 28; }

    grantInvuln(s) { this.invuln = Math.max(this.invuln, s); }

    line() { return WEAPON_LINES[this.activeWeapon] || WEAPON_LINES.pulse; }

    addNova(v) {
      if (this.overdrive > 0) return;
      // A detuned hull feeds the star core far less: sloppy mono-weapons play
      // should not bank nova charges as fast as clean rotation.
      const mul = this.weapons[this.activeWeapon].detuned ? 0.45 : 1 + this.resonance * 0.06;
      const diffNova = SF.Game && SF.Game.diff ? SF.Game.diff.nova : 1;
      this.nova = clamp(this.nova + v * mul * diffNova, 0, 100);
    }

    // Pain-charging ignores the line's state: getting hit always pays out in full.
    addNovaRaw(v) {
      if (this.overdrive > 0) return;
      this.nova = clamp(this.nova + v, 0, 100);
    }

    unlockedWeapons() {
      return Object.keys(this.weapons).filter((k) => this.weapons[k].unlocked);
    }

    // Charge economy: every shot spends charge, holstered lines refill. Running
    // a line dry detunes it until it is swapped out and rested, so holding one
    // weapon forever is never the right play. Resonance rewards the rotation.
    updateWeapons(dt) {
      for (const key of Object.keys(this.weapons)) {
        const w = this.weapons[key];
        if (!w.unlocked) continue;
        const line = WEAPON_LINES[key];
        if (key !== this.activeWeapon) {
          // Rested in the holster: refills at the line's own rate and is ready
          // again as soon as it actually holds charge.
          w.charge = clamp(w.charge + line.recover * dt, 0, 100);
          if (w.detuned && w.charge >= DETUNE_CLEAR) this.rearm(key, w, line);
        } else if (w.detuned) {
          // Still held and dry: vent upward until the magazine is genuinely full,
          // then re-arm. Anything lower and the next burst empties it again.
          w.charge = clamp(w.charge + line.drain * VENT_RATIO * dt, 0, DETUNE_REARM);
          if (w.charge >= DETUNE_REARM) this.rearm(key, w, line);
        }
      }
      if (this.resT > 0) {
        this.resT -= dt;
        if (this.resT <= 0) {
          this.resonance = Math.max(0, this.resonance - 1);
          if (this.resonance) this.resT = RESONANCE_WINDOW;
        }
      }
    }

    // Bringing a line back online is a beat the player should feel: the ring hits
    // 100, the pill flares, and the gun is whole again.
    rearm(key, w, line) {
      w.detuned = false;
      if (key !== this.activeWeapon) return;
      const g = SF.Game;
      if (g && g.fx) {
        SF.Audio.sfx('ui');
        g.fx.ring(this.x, this.y, line.tint, 12, 0.34, 74, 0.7);
      }
    }

    // A swap crate rotates onto the readiest holstered line and tops that line up,
    // so the gun you land on is immediately usable instead of half-dry. With only one
    // line there is nothing to rotate to, so it refills that one — never a wasted drop.
    swapToBest(topUp) {
      const holstered = this.unlockedWeapons().filter((k) => k !== this.activeWeapon);
      if (!holstered.length) {
        const key = this.activeWeapon;
        const w = this.weapons[key];
        w.charge = clamp(w.charge + (topUp === undefined ? 45 : topUp), 0, 100);
        if (w.detuned && w.charge >= DETUNE_CLEAR) this.rearm(key, w, WEAPON_LINES[key]);
        return { swapped: false, name: key, charge: Math.round(w.charge) };
      }
      let best = holstered[0];
      for (const k of holstered) if (this.weapons[k].charge > this.weapons[best].charge) best = k;
      const target = this.weapons[best];
      target.charge = clamp(target.charge + (topUp === undefined ? 30 : topUp), 0, 100);
      if (target.detuned && target.charge >= DETUNE_CLEAR) this.rearm(best, target, WEAPON_LINES[best]);
      const ok = this.selectWeapon(best);
      return { swapped: ok, name: best, charge: Math.round(target.charge) };
    }

    spendCharge() {
      const w = this.weapons[this.activeWeapon];
      if (w.detuned) return;   // venting: do not keep it pinned at zero
      // With a single unlocked line there is nowhere to rotate, so it drains
      // slower — the mechanic bites once the arsenal actually exists.
      const solo = this.unlockedWeapons().length < 2 ? 0.55 : 1;
      const line = WEAPON_LINES[this.activeWeapon];
      w.charge = clamp(w.charge - line.drain * this.fireRate() * solo, 0, 100);
      if (w.charge <= 0.5) w.detuned = true;
    }

    damageMul() {
      const w = this.weapons[this.activeWeapon];
      const res = 1 + this.resonance * 0.08;
      const detune = w.detuned ? 0.55 : 1;
      const od = this.overdrive > 0 ? 1.8 : 1;
      return res * detune * od;
    }

    calibrate(base) {
      return Math.max(1, Math.round(base * this.damageMul()));
    }

    addPower() {
      if (this.level < 5) {
        this.level++;
        this.weapons[this.activeWeapon].level = this.level;
        return true;
      }
      return false;
    }

    downgradeActive() {
      this.level = Math.max(1, this.level - 1);
      this.weapons[this.activeWeapon].level = this.level;
    }

    selectWeapon(name) {
      const target = this.weapons[name];
      if (!target || !target.unlocked || name === this.activeWeapon) return false;
      this.weapons[this.activeWeapon].level = this.level;
      this.activeWeapon = name;
      this.level = target.level;
      // read the dwell time before this swap resets it
      const wellTimed = this.swapIdle >= SWAP_RESONANCE_GAP;
      this.swapIdle = 0;
      this.fireTimer = Math.min(this.fireTimer, 0.07);
      // Rotating onto a healthy line stacks resonance; rotating onto a dry one
      // buys nothing, so the swap has to be timed. Swapping faster than the chain
      // can breathe (same frame, double-tap) drops it instead of farming it.
      if (wellTimed && target.charge >= DETUNE_CLEAR && !target.detuned) {
        this.resonance = Math.min(RESONANCE_MAX, this.resonance + 1);
        this.resT = RESONANCE_WINDOW;
      } else {
        this.resonance = 0;
        this.resT = 0;
      }
      SF.Audio.sfx('ui');
      const g = SF.Game;
      if (g && g.fx) {
        g.fx.ring(this.x, this.y, WEAPON_LINES[name].tint, 14, 0.32, 62, 0.8);
      }
      return true;
    }

    cycleWeapon() {
      const list = this.unlockedWeapons();
      if (list.length < 2) return false;
      const index = list.indexOf(this.activeWeapon);
      return this.selectWeapon(list[(index + 1) % list.length]);
    }

    unlockWeapon(name) {
      const target = this.weapons[name];
      if (!target) return null;
      const first = !target.unlocked;
      target.unlocked = true;
      if (first) target.level = 1;
      else if (target.level < 5) target.level++;
      else return 'max';
      if (name === this.activeWeapon) this.level = target.level;
      else this.selectWeapon(name);
      return first ? 'unlocked' : 'upgraded';
    }

    // Taking a hit costs more than a level: the active line loses charge, the
    // resonance chain breaks, and a wingman is blown off the wing.
    applyHitPenalty() {
      const w = this.weapons[this.activeWeapon];
      w.charge = Math.max(0, w.charge - 45);
      if (w.charge <= 0.5) w.detuned = true;
      this.resonance = 0;
      this.resT = 0;
      this.downgradeActive();
      return this.destroyDrone();
    }

    destroyDrone() {
      if (!this.drones.length) return null;
      const d = this.drones.pop();
      SF.Game.fx.explosion(d.x, d.y, 0.85, this.line().tint);
      SF.Game.fx.ring(d.x, d.y, this.line().tint, 8, 0.42, 74, 0.85);
      SF.Game.fx.spark(d.x, d.y, P.white, 10, 190, 0.3);
      SF.Game.fx.text(d.x, d.y - 26, '僚机失联', P.rose, 12, { life: 0.9 });
      SF.Audio.sfx('hit');
      return d.kind;
    }

    addDrone(kind) {
      if (this.drones.length >= 3) return null;
      const kinds = ['gunner', 'lance', 'aegis', 'swarm'];
      const have = this.drones.map((d) => d.kind);
      const pool = kinds.filter((k) => !have.includes(k));
      const type = kind && kinds.includes(kind) ? kind : (pool.length ? SF.pick(pool) : SF.pick(kinds));
      const slot = this.droneSlot(this.drones.length, this.drones.length + 1);
      this.drones.push({
        kind: type, x: this.x + slot.x, y: this.y + slot.y,
        angle: 0, fireT: SF.rand(0.15, 0.5), pdT: 0,
      });
      return type;
    }

    // Escorts flank the hull, one per side. The old ±44 put them inside the swept
    // wings, so two wingmen read as part of the ship instead of one on each side.
    droneSlot(index, count) {
      if (count <= 1) return { x: -64, y: 8 };
      if (count === 2) return index === 0 ? { x: -64, y: 8 } : { x: 64, y: 8 };
      if (index === 0) return { x: -68, y: 2 };
      if (index === 1) return { x: 68, y: 2 };
      return { x: 0, y: 48 };
    }

    addShield(n) {
      const before = this.shieldCharges;
      this.shieldCharges = Math.min(3, this.shieldCharges + (n || 1));
      return this.shieldCharges > before;
    }

    tryDash() {
      if (this.dashCd > 0 || !this.alive) return false;
      let dx = this.lastMX, dy = this.lastMY;
      // 没有任何移动记录时统一向上冲，不再按“离屏幕中心的偏移”斜冲
      if (Math.hypot(dx, dy) < 0.01) {
        dx = 0;
        dy = -1;
      }
      const len = Math.hypot(dx, dy) || 1;
      this.dashDirX = dx / len;
      this.dashDirY = dy / len;
      this.dashT = 0.17;
      this.dashCd = 1.7;
      this.invuln = Math.max(this.invuln, 0.36);
      this.after = [];
      SF.Audio.sfx('dash');
      SF.Game.fx.ring(this.x, this.y, P.teal, 12, 0.35, 70);
      SF.Game.fx.rays(this.x, this.y, P.teal, 12, 330, 0.24);
      SF.Game.fx.spark(this.x, this.y, P.teal, 8, 120);
      SF.Game.fx.shake(3);
      return true;
    }

    fireNova() {
      if (this.nova < 100 || this.overdrive > 0 || !this.alive) return false;
      this.nova = 0;
      this.overdrive = 7;
      this.grantInvuln(0.9);
      SF.Game.novaBlast();
      return true;
    }

    takeHit() {
      if (!this.alive || this.invuln > 0 || this.dashT > 0) return false;
      SF.Game.playerHit();
      return true;
    }

    update(dt, input) {
      this.t += dt;
      // How long the hull has been riding the same line — the director uses it to
      // decide when to hand out a swap crate.
      this.swapIdle += dt;
      this.muzzle = Math.max(0, this.muzzle - dt);
      this.hitFlash = Math.max(0, this.hitFlash - dt);
      if (this.invuln > 0) this.invuln -= dt;
      if (this.overdrive > 0) {
        this.overdrive -= dt;
        if (this.overdrive <= 0) {
          this.overdrive = 0;
        }
      }
      // The star core refills on its own, slowly — combat action tops it up faster.
      if (this.overdrive <= 0 && this.alive) {
        this.nova = clamp(this.nova + 1.8 * dt, 0, 100);
      }
      if (this.dashCd > 0) this.dashCd = Math.max(0, this.dashCd - dt);
      if (this.dashT > 0) this.dashT -= dt;
      if (this.reflectT > 0) this.reflectT = Math.max(0, this.reflectT - dt);

      const mv = input.moveVector();
      const focus = input.focusHeld();
      this.focus = focus;
      const speed = focus ? 185 : 440;
      let movedX = 0, movedY = 0;
      const px = this.x, py = this.y;
      if (mv.active) {
        this.dragging = false;
        this.x += mv.x * speed * dt;
        this.y += mv.y * speed * dt;
        movedX = mv.x; movedY = mv.y;
        this.lastMX = mv.x; this.lastMY = mv.y;
      } else {
        const pt = input.pointer;
        if (pt.type === 'mouse' && pt.inside) {
          this.dragging = false;
          const tx = pt.x, ty = pt.y;
          const k = 1 - Math.exp(-26 * dt);
          this.x += (tx - this.x) * k;
          this.y += (ty - this.y) * k;
        } else if (pt.down && pt.type !== 'mouse') {
          if (!this.dragging) {
            this.dragging = true;
            this.dragDX = this.x - pt.x;
            this.dragDY = this.y - pt.y;
            // keep drag offset within a comfortable range so the ship never hides under the thumb
            this.dragDX = clamp(this.dragDX, -SF.W * 0.3, SF.W * 0.3);
            this.dragDY = clamp(this.dragDY, -140, 60);
          }
          const tx = clamp(pt.x + this.dragDX, 0, SF.W);
          const ty = clamp(pt.y + this.dragDY, 0, SF.H);
          const k = 1 - Math.exp(-30 * dt);
          this.x += (tx - this.x) * k;
          this.y += (ty - this.y) * k;
        } else if (!pt.down) {
          this.dragging = false;
        }
      }

      if (this.dashT > 0) {
        const k = this.dashT / 0.17;
        this.x += this.dashDirX * 1250 * dt;
        this.y += this.dashDirY * 1250 * dt;
        this.lastMX = this.dashDirX; this.lastMY = this.dashDirY;
        if (this.after.length < 7) {
          this.after.push({ x: this.x, y: this.y, bank: this.bank, life: 0.3 });
        }
        this.fireTimer = Math.min(this.fireTimer, 0.02);
      }

      // Keep the hull clear of the bottom instrument strip so the HUD never
      // covers the ship or a bullet that could hit it.
      this.x = clamp(this.x, 31, SF.W - 31);
      this.y = clamp(this.y, SF.H * 0.18, SF.H - 104);

      const movedDX = this.x - px;
      const movedDY = this.y - py;
      if (Math.abs(movedDX) + Math.abs(movedDY) > 0.5) {
        this.lastMX = movedDX;
        this.lastMY = movedDY;
      }
      // per-frame pixels made the bank frame-rate dependent (half the tilt at 120Hz)
      this.bank = SF.damp(this.bank, clamp(movedDX / Math.max(dt, 1e-3) / 420, -1, 1), 8, dt);

      if (this.after.length) {
        for (let i = this.after.length - 1; i >= 0; i--) {
          this.after[i].life -= dt;
          if (this.after[i].life <= 0) this.after.splice(i, 1);
        }
      }

      // engine trail — tinted by the active weapon line
      this.trailTimer -= dt;
      if (this.trailTimer <= 0 && this.alive) {
        this.trailTimer = 0.045;
        const col = this.overdrive > 0 ? P.gold : this.line().tint;
        SF.Game.fx.ember(this.x + SF.rand(-9, 9), this.y + 30, col, SF.rand(1, 2.4), SF.rand(0.2, 0.4), SF.rand(10, 40), SF.rand(-40, -10));
      }

      // input actions
      if (input.consume('dash')) this.tryDash();
      if (input.consume('nova')) this.fireNova();
      if (input.consume('weapon')) this.cycleWeapon();
      if (input.consume('weapon1')) this.selectWeapon('pulse');
      if (input.consume('weapon2')) this.selectWeapon('prism');
      if (input.consume('weapon3')) this.selectWeapon('swarm');

      // autofire
      this.fireTimer -= dt;
      if (this.fireTimer <= 0 && this.alive) {
        this.shoot();
        this.fireTimer = this.fireRate();
      }
      this.updateWeapons(dt);

      // escort drones hold fixed wing slots and trail with a short spring
      const line = this.line();
      for (let i = 0; i < this.drones.length; i++) {
        const d = this.drones[i];
        const slot = this.droneSlot(i, this.drones.length);
        // a hull hugging the screen edge must not shove its wingman off the field
        const tx = clamp(this.x + slot.x, 18, SF.W - 18);
        const ty = this.y + slot.y;
        d.x = SF.damp(d.x, tx, 13, dt);
        d.y = SF.damp(d.y, ty, 13, dt);
        d.angle = this.bank * 0.13;
        d.pdT = Math.max(0, d.pdT - dt);
        d.fireT -= dt;
        if (d.fireT > 0) continue;
        this.droneFire(d);
        if (d.kind !== 'aegis') SF.Game.fx.spark(d.x, d.y - 8, line.tint, 2, 44, 0.16);
      }

      // overdrive ready pulse
      if (this.nova >= 100 && Math.random() < dt * 8) {
        SF.Game.fx.ember(this.x + SF.rand(-26, 26), this.y + SF.rand(-20, 26), P.gold, SF.rand(1, 2), 0.5, 8, -20);
      }
    }

    // Wingmen re-load for whatever the hull is running: pulse gives them rate,
    // prism gives them weight, swarm gives them seekers. The drone's own kind
    // then decides volume and targeting, so a lance is still a lance.
    droneFire(d) {
      const g = SF.Game;
      const line = this.line();
      const lean = clamp((d.x - this.x) / 55, -0.18, 0.18);
      const dmg = (base) => Math.max(1, Math.round(base * this.damageMul()));

      if (d.kind === 'aegis') {
        d.fireT = 0.7;
        g.fx.ring(d.x, d.y, line.tint, 3, 0.28, 32, 0.5);
        return;
      }
      if (this.activeWeapon === 'swarm' || d.kind === 'swarm') {
        d.fireT = d.kind === 'gunner' ? 0.86 : 1.02;
        const n = d.kind === 'lance' ? 2 : 1;
        for (let k = 0; k < n; k++) {
          g.playerBullets.push(new Bullet(d.x, d.y - 5, lean * 520 + (k - (n - 1) / 2) * 140, -700, {
            friendly: true, kind: 'micro', r: 4, dmg: dmg(2), life: 2.1, tint: line.tint,
            homing: 1.6, turn: 4.4, lockDelay: 0.12,
          }));
        }
        return;
      }
      if (this.activeWeapon === 'prism') {
        d.fireT = d.kind === 'lance' ? 0.82 : 1.05;
        g.playerBullets.push(new Bullet(d.x, d.y - 9, lean * 150, -600, {
          friendly: true, kind: 'beam', r: 6, dmg: dmg(d.kind === 'lance' ? 4 : 3), tint: line.tint,
          life: 2.4, pierce: d.kind === 'lance' ? 2 : 1,
        }));
        return;
      }
      d.fireT = d.kind === 'gunner' ? 0.34 : 0.5;
      g.playerBullets.push(new Bullet(d.x, d.y - 7, lean * 260, -1180, {
        friendly: true, kind: 'drone', r: 4, dmg: dmg(1), life: 1.9, tint: line.tint,
      }));
      if (d.kind === 'lance') {
        g.playerBullets.push(new Bullet(d.x - 5, d.y - 4, lean * 320 - 60, -1140, {
          friendly: true, kind: 'drone', r: 4, dmg: dmg(1), life: 1.9, tint: line.tint,
        }));
      }
    }

    fireRate() {
      const lv = clamp(this.level, 1, 5);
      const w = this.weapons[this.activeWeapon];
      const od = this.overdrive > 0 ? 0.74 : 1;
      // A dry line does not just hit softer, it also cycles slower.
      const detune = w && w.detuned ? 1.45 : 1;
      const table = {
        pulse: [0.120, 0.108, 0.098, 0.090, 0.082],
        // 棱镜抬升到与另外两条线同一 DPS 量级，单发重击与对巨构加成保留为身份
        prism: [0.360, 0.330, 0.305, 0.280, 0.245],
        swarm: [0.250, 0.210, 0.180, 0.160, 0.145],
      };
      return (table[this.activeWeapon] || table.pulse)[lv - 1] * od * detune;
    }

    shoot() {
      const lv = clamp(this.level, 1, 5);
      const mode = this.activeWeapon;
      const line = this.line();
      const sp = line.speed;
      const dmg = (base) => this.calibrate(base);
      const bx = this.x, by = this.y - 26;
      const fire = (x, y, vx, vy, opts) => {
        opts = opts || {};
        // precise mode concentrates the pattern instead of widening it
        if (this.focus) { x = bx + (x - bx) * 0.42; vx *= 0.2; }
        SF.Game.playerBullets.push(new Bullet(x, y, vx, vy, {
          friendly: true,
          kind: opts.kind || 'bolt',
          r: opts.r || 5,
          dmg: opts.dmg === undefined ? 1 : opts.dmg,
          life: opts.life || 2.2,
          pierce: opts.pierce || 0,
          homing: opts.homing || 0,
          turn: opts.turn || 2.4,
          lockDelay: opts.lockDelay || 0,
          scale: opts.scale || 1,
          bossBonus: opts.bossBonus || 1,
          tint: opts.tint || line.tint,
        }));
      };

      if (mode === 'prism') {
        // Slowest and heaviest. Piercing lances carry the best single-target bite
        // in the game, but the travel time punishes sloppy positioning and the
        // volley is too sparse to carpet a wide swarm.
        const b = [4, 4, 5, 6, 6][lv - 1];
        const pierce = [1, 1, 2, 2, 3][lv - 1];
        fire(bx, by - 4, 0, -sp, { kind: 'lance', r: 8, dmg: dmg(b), pierce, life: 3, bossBonus: 1.18 });
        if (lv >= 2) {
          fire(bx - 10, by + 2, 0, -sp * 0.97, { kind: 'lance', r: 6, dmg: dmg(Math.max(2, b - 1)), pierce, life: 3, bossBonus: 1.18 });
          fire(bx + 10, by + 2, 0, -sp * 0.97, { kind: 'lance', r: 6, dmg: dmg(Math.max(2, b - 1)), pierce, life: 3, bossBonus: 1.18 });
        }
        if (lv >= 4) {
          fire(bx - 20, by + 6, -95, -sp * 0.94, { kind: 'lance', r: 5, dmg: dmg(Math.max(2, b - 2)), pierce: 1, life: 3, bossBonus: 1.18 });
          fire(bx + 20, by + 6, 95, -sp * 0.94, { kind: 'lance', r: 5, dmg: dmg(Math.max(2, b - 2)), pierce: 1, life: 3, bossBonus: 1.18 });
        }
      } else if (mode === 'swarm') {
        // Mid speed, mid damage, and they never miss: micro seekers that only
        // acquire legally visible targets, so they cannot clear a wave early.
        const count = [2, 3, 4, 4, 4][lv - 1];
        const base = [2, 2, 2, 3, 3][lv - 1];
        for (let i = 0; i < count; i++) {
          const off = (i - (count - 1) / 2);
          fire(bx + off * 6, by + 6, off * 120, -sp, {
            kind: 'micro', r: 4, dmg: dmg(base), homing: 1.45, turn: 4.6,
            lockDelay: 0.11, life: 2.2,
          });
        }
      } else {
        // Fastest and lightest. Always lands, covers the screen, never overkills
        // an armoured hull — the safe answer that stays useful all run.
        const v = -sp;
        if (lv === 1) {
          fire(bx, by, 0, v, { dmg: dmg(1) });
        } else if (lv === 2) {
          fire(bx - 7, by, 0, v, { dmg: dmg(1) });
          fire(bx + 7, by, 0, v, { dmg: dmg(1) });
        } else if (lv === 3) {
          fire(bx, by - 4, 0, v, { dmg: dmg(1) });
          fire(bx - 9, by + 4, -45, v, { dmg: dmg(1) });
          fire(bx + 9, by + 4, 45, v, { dmg: dmg(1) });
        } else if (lv === 4) {
          fire(bx - 6, by - 2, 0, v, { dmg: dmg(1) });
          fire(bx + 6, by - 2, 0, v, { dmg: dmg(1) });
          fire(bx - 15, by + 5, -85, v, { dmg: dmg(1) });
          fire(bx + 15, by + 5, 85, v, { dmg: dmg(1) });
        } else {
          fire(bx, by - 6, 0, v, { kind: 'bolt', r: 6, dmg: dmg(2) });
          fire(bx - 8, by + 2, 0, v, { dmg: dmg(1) });
          fire(bx + 8, by + 2, 0, v, { dmg: dmg(1) });
          fire(bx - 17, by + 7, -105, v, { dmg: dmg(1) });
          fire(bx + 17, by + 7, 105, v, { dmg: dmg(1) });
        }
        if (this.overdrive > 0) {
          fire(bx - 24, by + 10, -160, v * 0.94, { dmg: dmg(1) });
          fire(bx + 24, by + 10, 160, v * 0.94, { dmg: dmg(1) });
        }
      }

      this.muzzle = 0.06;
      this.spendCharge();
      SF.Game.fx.spark(bx, by, this.overdrive > 0 ? P.gold : line.tint, this.overdrive > 0 ? 3 : 2, 40, 0.14);
      SF.Audio.sfx('shoot');
    }

    draw(ctx) {
      if (!this.alive) return;
      const line = this.line();
      if (this.invuln > 0 && Math.floor(this.invuln * 18) % 2 === 0) {
        // still draw a ghost to keep feedback
        SF.Art.drawShip(ctx, this.x, this.y, { scale: 1, bank: this.bank, t: this.t, thrust: 0.5, alpha: 0.45, tint: line.tint, deep: line.deep });
      } else {
        // afterimages
        for (const a of this.after) {
          const al = a.life / 0.3;
          SF.Art.drawShip(ctx, a.x, a.y, { scale: 1, bank: a.bank, t: this.t, thrust: 0, alpha: al * 0.22, tint: line.tint, deep: line.deep });
        }
        if (this.overdrive > 0) {
          ctx.save();
          ctx.globalCompositeOperation = 'lighter';
          SF.Art.drawGlow(ctx, this.x, this.y, 62, P.gold, 0.14 + Math.sin(this.t * 9) * 0.04);
          ctx.save();
          ctx.translate(this.x, this.y);
          ctx.rotate(this.t * 2.4);
          ctx.strokeStyle = 'rgba(255,210,122,.35)';
          ctx.lineWidth = 1;
          ctx.setLineDash([8, 12]);
          ctx.beginPath();
          ctx.arc(0, 0, 40, 0, TAU);
          ctx.stroke();
          ctx.restore();
          ctx.restore();
        }
        SF.Art.drawShip(ctx, this.x, this.y, { scale: 1, bank: this.bank, t: this.t, thrust: 1, tint: line.tint, deep: line.deep });
        if (this.muzzle > 0) {
          ctx.save();
          ctx.globalCompositeOperation = 'lighter';
          SF.Art.drawGlow(ctx, this.x, this.y - 30, 16, this.overdrive > 0 ? P.gold : line.tint, 0.6);
          ctx.restore();
        }

        // shield bubble — segmented, one lit arc per remaining charge
        if (this.shieldCharges > 0) {
          const rr = 35 + this.shieldCharges * 2;
          ctx.save();
          ctx.translate(this.x, this.y);
          ctx.globalCompositeOperation = 'lighter';
          // 渐变按护盾层数缓存，避免每帧重新创建
          if (!this._bubbleGrad || this._bubbleCtx !== ctx || this._bubbleCharge !== this.shieldCharges) {
            const bubble = ctx.createRadialGradient(0, 0, rr * 0.5, 0, 0, rr + 4);
            bubble.addColorStop(0, 'rgba(155,140,255,0)');
            bubble.addColorStop(0.72, 'rgba(155,140,255,.09)');
            bubble.addColorStop(1, 'rgba(105,245,220,.22)');
            this._bubbleGrad = bubble;
            this._bubbleCtx = ctx;
            this._bubbleCharge = this.shieldCharges;
          }
          ctx.fillStyle = this._bubbleGrad;
          ctx.beginPath();
          ctx.arc(0, 0, rr + 4, 0, TAU);
          ctx.fill();
          ctx.rotate(this.t * 1.4);
          ctx.lineWidth = 1.8;
          for (let i = 0; i < 3; i++) {
            const a0 = i * (TAU / 3) + 0.16;
            ctx.strokeStyle = i < this.shieldCharges ? 'rgba(105,245,220,.85)' : 'rgba(155,140,255,.16)';
            ctx.beginPath();
            ctx.arc(0, 0, rr, a0, a0 + 0.72);
            ctx.stroke();
          }
          ctx.strokeStyle = 'rgba(155,140,255,.32)';
          ctx.lineWidth = 1;
          ctx.beginPath();
          ctx.arc(0, 0, rr, 0, TAU);
          ctx.stroke();
          ctx.restore();
        }

        // prism field — destroys nearby bullets while active
        if (this.reflectT > 0) {
          const pulse = 0.55 + Math.sin(this.t * 8) * 0.16;
          ctx.save();
          ctx.translate(this.x, this.y);
          ctx.globalCompositeOperation = 'lighter';
          SF.Art.drawGlow(ctx, 0, 0, 66, P.gold, 0.13 + pulse * 0.08);
          ctx.rotate(-this.t * 2.2);
          ctx.strokeStyle = `rgba(255,210,122,${0.42 + pulse * 0.2})`;
          ctx.lineWidth = 1.4;
          ctx.setLineDash([13, 9]);
          ctx.beginPath();
          ctx.arc(0, 0, 50 + Math.sin(this.t * 4) * 2, 0, TAU);
          ctx.stroke();
          ctx.setLineDash([]);
          ctx.strokeStyle = 'rgba(105,245,220,.5)';
          ctx.rotate(this.t * 3.4);
          for (let i = 0; i < 4; i++) {
            ctx.beginPath();
            ctx.arc(0, 0, 50, i * (TAU / 4), i * (TAU / 4) + 0.28);
            ctx.stroke();
          }
          ctx.restore();
        }

        // drone escorts
        for (const d of this.drones) {
          if (d.kind === 'aegis') {
            ctx.save();
            ctx.translate(d.x, d.y);
            ctx.globalCompositeOperation = 'lighter';
            SF.Art.drawGlow(ctx, 0, 0, 28, P.violet, 0.2);
            ctx.strokeStyle = 'rgba(155,140,255,.45)';
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.arc(0, 0, 19 + Math.sin(this.t * 5) * 2, 0, TAU);
            ctx.stroke();
            ctx.restore();
          }
          SF.Art.drawDrone(ctx, d.x, d.y, this.t, d.angle, d.kind, line.tint);
        }

        // hitbox core
        const pulse = 0.6 + Math.sin(this.t * 6) * 0.18;
        ctx.save();
        ctx.globalCompositeOperation = 'lighter';
        SF.Art.drawGlow(ctx, this.x, this.y, 12, P.white, pulse * 0.18);
        ctx.fillStyle = 'rgba(255,255,255,.85)';
        ctx.beginPath();
        ctx.arc(this.x, this.y + 2, 2.2, 0, TAU);
        ctx.fill();
        ctx.restore();

        // precise-mode reticle: the true hitbox plus a slow dashed ring
        if (this.focus) {
          ctx.save();
          ctx.translate(this.x, this.y + 2);
          ctx.globalCompositeOperation = 'lighter';
          ctx.strokeStyle = 'rgba(243,248,255,.72)';
          ctx.lineWidth = 1.2;
          ctx.beginPath();
          ctx.arc(0, 0, 12, 0, TAU);
          ctx.stroke();
          ctx.strokeStyle = SF.hexA(line.tint, 0.55);
          ctx.setLineDash([4, 6]);
          ctx.lineDashOffset = -this.t * 26;
          ctx.beginPath();
          ctx.arc(0, 0, 17, 0, TAU);
          ctx.stroke();
          ctx.restore();
        }
      }

      if (this.hitFlash > 0) {
        ctx.save();
        ctx.globalCompositeOperation = 'lighter';
        SF.Art.drawGlow(ctx, this.x, this.y, 70, P.rose, this.hitFlash * 1.6);
        ctx.restore();
      }
    }
  }
  SF.Player = Player;

  /* ---------------- Enemy types ---------------- */
  const ENEMY_TYPES = {
    drifter: {
      size: 17, hp: 4, score: 100,
      color: '#ff6f91', accent: '#ffd1dc', glow: '#ff6f91',
      pattern: 'sine', fire: 'aimed', fireCd: [1.6, 2.2], speed: 78, amp: 44,
      drop: 0.1,
    },
    wasp: {
      size: 15, hp: 3, score: 150,
      color: '#ff9ab4', accent: '#ffffff', glow: '#ff6f91',
      pattern: 'dive', fire: null, speed: 185, drop: 0.06,
    },
    lancer: {
      size: 23, hp: 15, score: 280,
      color: '#a597ff', accent: '#ffd27a', glow: '#9b8cff',
      pattern: 'hover', fire: 'fan', fireCd: [1.6, 2.1], speed: 55, targetY: 0.23,
      drop: 0.22,
    },
    orbiter: {
      size: 21, hp: 19, score: 340,
      color: '#9b8cff', accent: '#ffd27a', glow: '#9b8cff',
      pattern: 'orbit', fire: 'ring', fireCd: [2.0, 2.7], speed: 50, targetY: 0.27,
      drop: 0.26,
    },
    splitter: {
      size: 20, hp: 11, score: 230,
      color: '#ff6f91', accent: '#ffd27a', glow: '#ff6f91',
      pattern: 'sine', fire: 'spread', fireCd: [1.8, 2.4], speed: 64, amp: 32,
      drop: 0.2,
    },
    bulwark: {
      size: 30, hp: 46, score: 520,
      color: '#c3cbe0', accent: '#ffd27a', glow: '#ffd27a',
      pattern: 'hover', fire: 'heavy', fireCd: [1.8, 2.5], speed: 36, targetY: 0.2,
      drop: 0.4,
    },
    rock: {
      size: 23, hp: 14, score: 70,
      color: '#aab6cc', accent: '#ffffff', glow: '#9b8cff',
      pattern: 'rock', fire: null, speed: 48, drop: 0,
    },
    sniper: {
      size: 22, hp: 20, score: 320,
      color: '#69f5dc', accent: '#ffffff', glow: '#69f5dc',
      pattern: 'hover', fire: 'needle', fireCd: [1.6, 2.1], speed: 58, targetY: 0.17,
      charge: 0.55, drop: 0.24,
    },
    minekeeper: {
      size: 24, hp: 28, score: 380,
      color: '#ffd27a', accent: '#ff6f91', glow: '#ffd27a',
      pattern: 'hover', fire: 'mines', fireCd: [2.4, 3.1], speed: 42, targetY: 0.25,
      charge: 0.3, drop: 0.3,
    },
    sidewinder: {
      size: 21, hp: 17, score: 310,
      color: '#9b8cff', accent: '#ff6f91', glow: '#9b8cff',
      pattern: 'strafe', fire: 'wavefire', fireCd: [1.25, 1.75], speed: 135, targetY: 0.33,
      drop: 0.22,
    },
    carrier: {
      size: 34, hp: 78, score: 680,
      color: '#8fa4c9', accent: '#ffd27a', glow: '#9b8cff',
      pattern: 'hover', fire: 'carrier', fireCd: [2.0, 2.6], speed: 32, targetY: 0.13,
      drop: 0.55,
    },
    shardling: {
      size: 13, hp: 5, score: 90,
      color: '#ff9ab4', accent: '#ffffff', glow: '#ff6f91',
      pattern: 'dive', fire: null, speed: 215, drop: 0.03,
    },
  };
  SF.ENEMY_TYPES = ENEMY_TYPES;

  class Enemy {
    constructor(type, x, y, opts) {
      opts = opts || {};
      const def = ENEMY_TYPES[type] || ENEMY_TYPES.drifter;
      this.type = type;
      this.def = def;
      this.elite = !!opts.elite;
      this.sector = opts.sector || 1;
      // Intensity climbs in 5-sector blocks, so early runs stay readable and
      // each new tier is a step up you can feel.
      this.tier = Math.min(3, Math.floor((this.sector - 1) / 5));
      this.size = def.size * (this.elite ? 1.22 : 1);
      this.maxHp = Math.round(def.hp * (1 + (this.sector - 1) * 0.16) * (this.elite ? 2.4 : 1));
      // 精英词缀：同一套敌机轮廓，靠装甲/急速/爆裂改变解法
      this.affix = ['armored', 'rapid', 'volatile'].includes(opts.affix) ? opts.affix : null;
      this.fireMul = 1;
      if (this.affix === 'armored') {
        this.maxHp = Math.round(this.maxHp * 1.45);
        this.size *= 1.06;
        this.fireMul = 1.12;
      } else if (this.affix === 'rapid') {
        this.size *= 0.92;
        this.fireMul = 0.72;
      } else if (this.affix === 'volatile') {
        this.maxHp = Math.max(1, Math.round(this.maxHp * 0.9));
      }
      this.hp = this.maxHp;
      this.score = Math.round(def.score * (this.elite ? 3.2 : 1) * (this.affix ? 1.18 : 1));
      this.color = this.elite ? P.gold : (this.affix === 'armored' ? P.steel : def.color);
      this.accent = def.accent;
      this.glowColor = this.elite ? P.gold
        : this.affix === 'armored' ? P.steel
          : this.affix === 'rapid' ? P.gold
            : this.affix === 'volatile' ? P.rose : def.glow;
      this.x = x;
      this.baseX = x;
      this.y = y;
      this.vx = 0;
      this.vy = def.speed || 60;
      this.t = 0;
      this.age = 0;
      this.phase = opts.phase === undefined ? Math.random() * TAU : opts.phase;
      this.flash = 0;
      this.spin = Math.random() * TAU;
      this.seed = Math.random() * 10;
      this.angle = 0;
      this.fireTimer = def.fireCd ? SF.rand(0.9, 1.6) : 99;
      this.dead = false;
      this.leaving = false;
      this.life = opts.life || 16;
      this.drop = opts.drop === undefined ? def.drop : opts.drop;
      this.hoverDir = SF.chance(0.5) ? 1 : -1;
      this.diveTarget = null;
      this.entryDelay = opts.delay || 0.35;
      this.side = opts.side || 0;
      this.targetY = def.targetY ? SF.H * def.targetY : SF.H * 0.24;
      this.amp = def.amp || 40;
      this.rock = type === 'rock';
      this.dir = opts.dir || (x < SF.W / 2 ? 1 : -1);
      this.chargeT = 0;
      this.summonT = SF.rand(2.6, 3.6);
    }

    get radius() { return this.size * 0.78; }

    targetable() {
      return !this.dead && this.age > (this.entryDelay + 0.18)
        && this.y > -10 && this.y < SF.H * 0.94;
    }

    update(dt, player) {
      this.t += dt;
      this.age += dt;
      this.flash = Math.max(0, this.flash - dt);
      this.spin += dt * 0.4;
      const speedMul = 1 + this.tier * 0.14;
      const def = this.def;

      switch (def.pattern) {
        case 'sine':
          this.y += this.vy * speedMul * dt;
          this.x = this.baseX + Math.sin(this.t * 1.5 + this.phase) * this.amp;
          this.angle = Math.sin(this.t * 1.5 + this.phase) * 0.2;
          break;
        case 'dive':
          if (this.age > this.entryDelay) {
            if (!this.diveTarget && player) {
              this.diveTarget = { x: player.x, y: player.y };
              const ang = Math.atan2(this.diveTarget.y - this.y, this.diveTarget.x - this.x);
              this.vx = Math.cos(ang) * this.vy * 1.5;
              this.vy = Math.sin(ang) * this.vy * 1.5;
            }
            this.x += this.vx * dt;
            this.y += this.vy * dt;
            this.angle = Math.atan2(this.vy, this.vx) - Math.PI / 2;
          } else {
            this.y += 40 * dt;
          }
          break;
        case 'hover': {
          if (this.age > this.life) {
            this.y += 170 * dt;
            this.leaving = true;
          } else if (this.y < this.targetY) {
            this.y = Math.min(this.targetY, this.y + this.vy * dt);
            this.x = this.baseX + Math.sin(this.t * 0.9 + this.phase) * 12;
          } else {
            this.x = this.baseX + Math.sin(this.t * 0.7 + this.phase) * (this.type === 'bulwark' ? 55 : 88) + Math.sin(this.t * 0.23) * 20;
            this.y = this.targetY + Math.sin(this.t * 1.1 + this.phase) * 8;
            this.angle = Math.cos(this.t * 0.7 + this.phase) * 0.15;
          }
          break;
        }
        case 'strafe': {
          if (this.y < this.targetY) {
            this.y = Math.min(this.targetY, this.y + 120 * dt);
          } else {
            this.y = this.targetY + Math.sin(this.t * 1.8 + this.phase) * 16;
            this.x += this.dir * this.vy * speedMul * dt;
            this.angle = this.dir > 0 ? Math.PI / 2 : -Math.PI / 2;
          }
          break;
        }
        case 'orbit': {
          if (this.age > this.life) {
            this.y += 160 * dt;
            this.leaving = true;
          } else if (this.y < this.targetY) {
            this.y = Math.min(this.targetY, this.y + this.vy * dt);
            this.x = this.baseX + Math.sin(this.t * 0.9 + this.phase) * 12;
          } else {
            const r = 62;
            this.x = this.baseX + Math.cos(this.t * 1.15 + this.phase) * r;
            this.y = this.targetY + Math.sin(this.t * 1.15 + this.phase) * 24;
            this.angle = 0;
          }
          break;
        }
        case 'rock':
          this.y += this.vy * speedMul * dt;
          this.x += Math.sin(this.t * 0.6 + this.phase) * 12 * dt;
          this.angle = 0;
          break;
      }

      // carrier launches small escorts from its hangar
      if (this.type === 'carrier' && !this.leaving && this.y > 20 && this.y < SF.H * 0.45) {
        this.summonT -= dt;
        if (this.summonT <= 0) {
          this.summonT = 3.6;
          if (SF.Game.enemies.length < 38) {
            const child = SF.Game.spawnEnemy(SF.chance(0.55) ? 'shardling' : 'wasp',
              clamp(this.x + SF.rand(-this.size, this.size), 30, SF.W - 30),
              this.y + this.size * 0.6, { sector: this.sector });
            if (child) child.entryDelay = 0.15;
            SF.Game.fx.ring(this.x, this.y + this.size * 0.5, P.violet, 5, 0.35, 44, 0.6);
            SF.Audio.sfx('hit');
          }
        }
      }

      // fire
      if (def.fire && !this.leaving && this.y > 20 && this.y < SF.H * 0.8) {
        this.fireTimer -= dt;
        if (this.chargeT > 0) {
          this.chargeT -= dt;
          if (this.chargeT <= 0) {
            this.fire(this.sector);
            const base = def.fireCd;
            this.fireTimer = SF.rand(base[0], base[1]) * this.fireMul * Math.max(0.7, 1 - this.tier * 0.1) * (this.elite ? 0.82 : 1) * (SF.Game && SF.Game.diff ? SF.Game.diff.enemyRate : 1);
            this.chargeT = 0;
          }
        } else if (this.fireTimer <= 0) {
          if (def.charge) {
            this.chargeT = def.charge * (this.elite ? 0.8 : 1);
            SF.Game.fx.ring(this.x, this.y, this.glowColor, 4, 0.3, this.size * 1.5, 0.7);
          } else {
            this.fire(this.sector);
            const base = def.fireCd;
            this.fireTimer = SF.rand(base[0], base[1]) * this.fireMul * Math.max(0.7, 1 - this.tier * 0.1) * (this.elite ? 0.82 : 1) * (SF.Game && SF.Game.diff ? SF.Game.diff.enemyRate : 1);
          }
        }
      }

      const m = 90;
      if (this.y > SF.H + m || this.y < -m * 2 || this.x < -m * 1.5 || this.x > SF.W + m * 1.5) return false;
      return true;
    }

    fire(sector) {
      if (!SF.Game.player) return;
      const pl = SF.Game.player;
      const dif = 1 + this.tier * 0.14;
      const sp = Math.min(300, 150 * dif) * (SF.Game && SF.Game.diff ? SF.Game.diff.bulletSpeed : 1);
      const bx = this.x, by = this.y + this.size * 0.5;
      const aim = Math.atan2(pl.y - this.y, pl.x - this.x);
      const add = (ang, speed, kind, r, scale, extra) => {
        const o = { kind: kind || 'orb', r: r || 6, life: 9, scale: scale || 1 };
        if (extra) Object.assign(o, extra);
        SF.Game.enemyBullets.push(new Bullet(this.x, this.y + this.size * 0.4,
          Math.cos(ang) * speed, Math.sin(ang) * speed, o));
      };
      const fireAim = () => add(aim, sp, 'orb');
      const fireFan = (n, spread) => {
        for (let i = 0; i < n; i++) {
          const a = aim + (i - (n - 1) / 2) * spread;
          add(a, sp * 0.95, 'shard');
        }
      };
      const fireRing = (n, offset) => {
        for (let i = 0; i < n; i++) {
          const a = (i / n) * TAU + offset;
          add(a, sp * 0.72, 'orb');
        }
      };
      const fireHeavy = () => {
        if (this.elite) {
          for (let i = 0; i < 2; i++) {
            const a = aim + (i ? 0.22 : -0.22);
            add(a, sp * 0.42, 'plasma', 13, 1.1, { life: 3.2, split: 6 });
          }
        }
        for (let i = 0; i < 3; i++) {
          const a = aim + (i - 1) * 0.26;
          add(a, sp * 0.55, 'heavy', 10);
        }
      };

      switch (this.def.fire) {
        case 'aimed':
          fireAim();
          if (this.elite) fireFan(3, 0.2);
          break;
        case 'fan':
          fireFan(this.elite ? 5 : 3, 0.18);
          SF.Audio.sfx('hit');
          break;
        case 'ring':
          fireRing(this.elite ? 14 : 10, this.t * 0.5);
          SF.Audio.sfx('hit');
          break;
        case 'spread':
          add(aim - 0.32, sp * 0.8, 'orb');
          add(aim + 0.32, sp * 0.8, 'orb');
          break;
        case 'heavy':
          fireHeavy();
          SF.Audio.sfx('hit');
          break;
        case 'needle': {
          const n = this.elite ? 5 : 3;
          for (let i = 0; i < n; i++) add(aim + (i - (n - 1) / 2) * 0.075, 330, 'needle', 5, 1.2);
          SF.Game.fx.ring(this.x, this.y, this.glowColor, 3, 0.25, this.size * 2.2, 0.6);
          SF.Game.fx.spark(this.x, this.y, P.white, 4, 110, 0.2);
          SF.Audio.sfx('hit');
          break;
        }
        case 'mines': {
          const n = this.elite ? 4 : 3;
          for (let i = 0; i < n; i++) {
            const a = aim + (i - (n - 1) / 2) * 0.55;
            add(a, 105, 'mine', 9, 1.1, { fuse: SF.rand(1.8, 2.4), split: 10, life: 8 });
          }
          SF.Audio.sfx('hit');
          break;
        }
        case 'wavefire':
          add(aim - 0.36, sp * 0.82, 'wave', 7, 1.2, { wave: 2.6 });
          add(aim + 0.36, sp * 0.82, 'wave', 7, 1.2, { wave: 2.6 });
          add(aim, sp * 1.1, 'needle', 5, 1.2);
          SF.Audio.sfx('hit');
          break;
        case 'carrier':
          add(aim - 0.16, sp * 0.6, 'orb', 7);
          add(aim + 0.16, sp * 0.6, 'orb', 7);
          break;
      }
      // muzzle flash
      SF.Game.fx.ember(bx, by, this.glowColor, this.size * 0.4, 0.25, 0, 0);
    }

    hit(dmg, x, y) {
      if (this.dead || !this.targetable()) return false;
      // 装甲词缀吃伤害更少，但体型/血量更大，逼玩家换索敌顺序
      this.hp -= dmg * (this.affix === 'armored' ? 0.72 : 1);
      this.flash = 0.07;
      SF.Audio.sfx('hit');
      SF.Game.fx.impact(x || this.x, y || this.y, this.glowColor, this.size > 25 ? 1.35 : 1);
      if (this.hp <= 0) {
        this.dead = true;
        return true;
      }
      return false;
    }

    draw(ctx, t) {
      if (this.chargeT > 0) {
        const k = clamp(1 - this.chargeT / (this.def.charge || 1), 0, 1);
        const pl = SF.Game.player;
        if (this.type === 'sniper' && pl) {
          ctx.save();
          ctx.strokeStyle = `rgba(105,245,220,${0.1 + k * 0.4})`;
          ctx.lineWidth = 0.8 + k * 1.2;
          ctx.setLineDash([5, 6]);
          ctx.beginPath();
          ctx.moveTo(this.x, this.y);
          ctx.lineTo(pl.x, pl.y);
          ctx.stroke();
          ctx.setLineDash([]);
          ctx.restore();
        } else {
          ctx.save();
          ctx.globalCompositeOperation = 'lighter';
          SF.Art.drawGlow(ctx, this.x, this.y, this.size * (1.2 + k * 0.8), this.glowColor, 0.2 + k * 0.25);
          ctx.restore();
        }
      }
      SF.Art.drawEnemy(ctx, this, t);
      if (this.affix) {
        // 词缀标记：装甲=青色弧段、急速=金色双箭头、爆裂=玫红虚线环
        const ac = this.affix === 'armored' ? P.steel : this.affix === 'rapid' ? P.gold : P.rose;
        const r = this.size * 1.08;
        ctx.save();
        ctx.translate(this.x, this.y);
        ctx.globalCompositeOperation = 'lighter';
        ctx.strokeStyle = SF.hexA(ac, 0.7);
        ctx.lineWidth = 1.1;
        if (this.affix === 'armored') {
          for (let i = 0; i < 3; i++) {
            ctx.beginPath();
            ctx.arc(0, 0, r, i * (TAU / 3) + 0.2, i * (TAU / 3) + 0.95);
            ctx.stroke();
          }
        } else if (this.affix === 'rapid') {
          for (const dir of [-1, 1]) {
            ctx.beginPath();
            ctx.moveTo(dir * r, -4);
            ctx.lineTo(dir * (r + 5), 0);
            ctx.lineTo(dir * r, 4);
            ctx.stroke();
          }
        } else {
          const k = 0.5 + Math.sin(t * 6) * 0.25;
          ctx.strokeStyle = SF.hexA(ac, k);
          ctx.setLineDash([3, 5]);
          ctx.beginPath();
          ctx.arc(0, 0, r, 0, TAU);
          ctx.stroke();
        }
        ctx.restore();
      }
      const showBar = this.elite || this.type === 'bulwark' || this.type === 'carrier';
      if (showBar && this.hp < this.maxHp) {
        const w = Math.max(24, this.size * 1.25);
        const y = this.y - this.size - 13;
        const ratio = clamp(this.hp / this.maxHp, 0, 1);
        ctx.save();
        ctx.fillStyle = 'rgba(4,6,15,.58)';
        ctx.fillRect(this.x - w / 2 - 1, y - 1, w + 2, 4);
        ctx.fillStyle = this.elite ? P.gold : P.rose;
        ctx.fillRect(this.x - w / 2, y, w * ratio, 2);
        ctx.globalCompositeOperation = 'lighter';
        ctx.fillStyle = this.elite ? 'rgba(255,210,122,.22)' : 'rgba(255,111,145,.18)';
        ctx.fillRect(this.x - w / 2, y, w * ratio, 2);
        ctx.restore();
      }
    }
  }
  SF.Enemy = Enemy;

  /* ---------------- Pickup ---------------- */
  class Pickup {
    constructor(x, y, type) {
      this.x = x; this.y = y;
      this.type = type;
      this.vy = type === 'gem' ? 66 : 60;
      this.t = Math.random() * 4;
      this.seed = Math.random() * 6;
      this.life = 20;
      this.dead = false;
      this.magnet = type === 'gem' ? 170 : 130;
    }

    update(dt, player) {
      this.t += dt;
      this.life -= dt;
      if (this.life <= 0) return false;
      if (player && player.alive) {
        const dx = player.x - this.x;
        const dy = player.y - this.y;
        const d = Math.hypot(dx, dy);
        if (d < this.magnet) {
          const k = this.type === 'gem' ? 9 : 5.5;
          const pull = (1 - d / this.magnet) * k * 60;
          this.x += (dx / (d || 1)) * pull * dt;
          this.y += (dy / (d || 1)) * pull * dt;
        } else {
          this.y += this.vy * dt;
          this.x += Math.sin(this.t * 1.8 + this.seed) * 14 * dt;
        }
      } else {
        this.y += this.vy * dt;
      }
      if (this.type !== 'gem' && SF.Game && SF.chance(dt * 1.6)) {
        const col = this.type === 'power' ? SF.PALETTE.teal
          : this.type === 'swap' ? SF.PALETTE.teal
          : this.type === 'shield' ? SF.PALETTE.violet
          : this.type === 'drone' ? SF.PALETTE.violet
          : this.type === 'prism' ? SF.PALETTE.violet
          : this.type === 'swarm' ? SF.PALETTE.gold
          : this.type === 'reflect' ? SF.PALETTE.gold : SF.PALETTE.rose;
        SF.Game.fx.ember(this.x + SF.rand(-9, 9), this.y + SF.rand(-8, 8), col, 1.2, 0.34, SF.rand(-8, 8), SF.rand(-18, -4));
      }
      return this.y < SF.H + 40;
    }

    draw(ctx) {
      if (this.type === 'gem') {
        const a = 0.7 + Math.sin(this.t * 5 + this.seed) * 0.2;
        SF.Art.drawSprite(ctx, SF.Art.sprites.gem, this.x, this.y, 0, 0.85, a);
        return;
      }
      SF.Art.drawPickup(ctx, this, this.t);
    }
  }
  SF.Pickup = Pickup;

  /* ---------------- Laser ---------------- */
  class Laser {
    constructor(x, y, angle, opts) {
      opts = opts || {};
      this.x = x; this.y = y;
      this.angle = angle;
      this.warn = opts.warn === undefined ? 0.9 : opts.warn;
      this.active = opts.active === undefined ? 0.7 : opts.active;
      this.t = 0;
      this.width = opts.width || 16;
      this.drawWidth = this.width;
      this.length = opts.length || SF.H * 2;
      this.sweep = opts.sweep || 0;
      this.color = opts.color || P.rose;
      this.dead = false;
      this.boss = opts.boss;
      this.sparks = 0;
    }

    update(dt) {
      this.t += dt;
      if (this.t > this.warn && this.t < this.warn + this.active && this.sweep) {
        this.angle += this.sweep * dt;
      }
      if (this.t > this.warn + this.active + 0.12) return false;
      if (this.boss && this.boss.dying) return false;
      return true;
    }

    isActive() { return this.t >= this.warn && this.t <= this.warn + this.active; }

    draw(ctx) {
      const cx = this.x, cy = this.y;
      const len = this.length;
      const width = this.width * clamp(SF.W / 540, 0.68, 1.08);
      this.drawWidth = width;
      ctx.save();
      ctx.translate(cx, cy);
      ctx.rotate(this.angle);
      if (!this.isActive()) {
        const k = clamp(this.t / this.warn, 0, 1);
        ctx.strokeStyle = `rgba(255,111,145,${0.14 + k * 0.35})`;
        ctx.lineWidth = 1 + k * 1.6;
        ctx.setLineDash([8, 8]);
        ctx.lineDashOffset = -this.t * 42;
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(len, 0);
        ctx.stroke();
        ctx.setLineDash([]);
        ctx.lineDashOffset = 0;
        // charge glow
        ctx.save();
        ctx.globalCompositeOperation = 'lighter';
        SF.Art.drawGlow(ctx, 0, 0, 34 + k * 20, this.color, 0.2 + k * 0.3);
        ctx.restore();
      } else {
        const env = Math.min(1, (this.t - this.warn) * 12, (this.warn + this.active - this.t) * 8);
        ctx.globalCompositeOperation = 'lighter';
        // 光束渐变只和长度有关，缓存后不必每帧重建
        if (!this._grad || this._gradCtx !== ctx || this._gradLen !== len) {
          const grad = ctx.createLinearGradient(0, 0, len, 0);
          grad.addColorStop(0, 'rgba(255,255,255,.95)');
          grad.addColorStop(0.2, 'rgba(255,210,220,.8)');
          grad.addColorStop(1, 'rgba(255,111,145,.05)');
          this._grad = grad;
          this._gradCtx = ctx;
          this._gradLen = len;
        }
        const grad = this._grad;
        ctx.strokeStyle = 'rgba(255,111,145,.24)';
        ctx.lineWidth = width * 2 * env;
        ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(len, 0); ctx.stroke();
        ctx.strokeStyle = grad;
        ctx.lineWidth = width * env;
        ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(len, 0); ctx.stroke();
        ctx.strokeStyle = 'rgba(255,255,255,.95)';
        ctx.lineWidth = Math.max(1.2, width * 0.18) * env;
        ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(len, 0); ctx.stroke();
        ctx.save();
        ctx.globalCompositeOperation = 'lighter';
        ctx.strokeStyle = 'rgba(255,255,255,.28)';
        ctx.lineWidth = Math.max(1, width * 0.22) * env;
        ctx.setLineDash([18, 13]);
        ctx.lineDashOffset = -this.t * 170;
        ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(len, 0); ctx.stroke();
        ctx.restore();
        ctx.save();
        SF.Art.drawGlow(ctx, 0, 0, 50, this.color, 0.35 * env);
        ctx.restore();
      }
      ctx.restore();
    }
  }
  SF.Laser = Laser;

  /* ---------------- Boss ---------------- */
  // Each boss owns three scripted spell rotations — one per phase. The rotation
  // is cycled in order (never random), so a pattern can be learned and predicted;
  // a later phase always adds vocabulary instead of only firing the same moves faster.
  const BOSS_TYPES = [
    {
      id: 'gatekeeper', name: '阿刻戎', title: '守门人', en: 'GATEKEEPER',
      size: 76, hp: 400, color: P.rose, glow: P.rose,
      phases: [
        ['fan', 'wallGap', 'twinBeam', 'mineCurtain'],
        ['spiral', 'wallGap', 'pulsar', 'fan', 'twinBeam', 'sweep'],
        ['charge', 'crossSweep', 'wallGap', 'cascade', 'ring', 'spiral'],
      ],
      shape: 'hex',
    },
    {
      id: 'weaver', name: '薇尔', title: '织星者', en: 'STAR WEAVER',
      size: 80, hp: 380, color: P.violet, glow: P.violet,
      phases: [
        ['ring', 'homing', 'homingBarrage', 'cross', 'waveCurtain'],
        ['spiralDouble', 'blink', 'homingBarrage', 'summon', 'cross'],
        ['crossSweep', 'laserWeb', 'spiralDouble', 'homingBarrage', 'ring', 'waveCurtain'],
      ],
      shape: 'wing',
    },
    {
      id: 'helios', name: '赫利俄斯', title: '新星核心', en: 'NOVA CORE',
      size: 82, hp: 440, color: P.gold, glow: P.gold,
      phases: [
        ['spiral', 'barrage', 'laserWeb', 'mineCurtain'],
        ['novaBurst', 'summon', 'twinBeam', 'spiral', 'barrage'],
        ['novaBurst', 'crossSweep', 'cascade', 'laserWeb', 'mineRing', 'barrage'],
      ],
      shape: 'core',
    },
  ];
  SF.BOSS_TYPES = BOSS_TYPES;

  const ATTACK_LABELS = {
    fan: '扇形齐射', spiral: '星旋回廊', ring: '环状爆发', homing: '追踪星群',
    sweep: '双生扫射', cross: '十字光牢', summon: '母舰召唤', barrage: '重弹压制',
    twinBeam: '双子光矛', mineCurtain: '雷区播种', blink: '相位闪现',
    laserWeb: '星网绞杀', waveCurtain: '波动弹幕',
    wallGap: '裂隙光幕', pulsar: '星脉脉冲', charge: '贯穿冲撞',
    crossSweep: '光牢旋扫', cascade: '陨星暴雨', homingBarrage: '猎群连射',
    spiralDouble: '逆向双旋', mineRing: '环形雷阵', novaBurst: '新星爆发',
  };
  SF.ATTACK_LABELS = ATTACK_LABELS;

  const ATTACK_DURATION = {
    fan: 1.3, spiral: 2.0, ring: 1.05, homing: 1.0, sweep: 2.5, cross: 2.0,
    summon: 1.15, barrage: 1.9, twinBeam: 2.2, mineCurtain: 2.0, blink: 2.2,
    laserWeb: 2.4, waveCurtain: 2.0, wallGap: 2.8, pulsar: 2.2, charge: 3.2,
    crossSweep: 2.9, cascade: 3.1, homingBarrage: 2.7, spiralDouble: 2.4,
    mineRing: 1.9, novaBurst: 2.6,
  };
  // Pre-spell tell: every attack charges for a beat in a colour that names its
  // threat family (bullets / beams / movement / ordnance).
  const TELL_TIME = 0.5;
  const ATTACK_TELL = {
    sweep: P.rose, cross: P.rose, twinBeam: P.rose, laserWeb: P.rose, crossSweep: P.rose,
    blink: P.violet, charge: P.violet,
    summon: P.gold, mineCurtain: P.gold, mineRing: P.gold,
  };
  // Attacks that take over the boss position for their whole duration.
  const MOVING_ATTACKS = { blink: true, charge: true };
  SF.ATTACK_DURATION = ATTACK_DURATION;

  class Boss {
    constructor(sector) {
      const idx = (sector - 1) % BOSS_TYPES.length;
      const def = BOSS_TYPES[idx];
      this.def = def;
      this.type = def.id;
      this.name = def.name;
      this.title = def.title;
      this.en = def.en;
      this.sector = sector;
      this.tier = Math.min(3, Math.floor((sector - 1) / 5));
      this.size = def.size;
      // 血量曲线封顶：玩家火力在 5 级后不再成长，Boss 不应无限变厚
      this.maxHp = Math.round(def.hp * (1 + Math.min(3.4, (sector - 1) * 0.70)) * (SF.Game && SF.Game.diff ? SF.Game.diff.bossHp : 1));
      this.hp = this.maxHp;
      this.glowColor = def.color;
      this.x = SF.W / 2;
      this.y = -this.size * 2.2;
      this.targetY = Math.max(SF.H * 0.27, 252);
      this.t = 0;
      this.flash = 0;
      this.entering = true;
      this.dying = false;
      this.deathT = 0;
      this.burstTimer = 0.2;
      this.attack = null;
      this.attackCd = 1.15;
      this.phase = 1;
      this.seqIdx = 0;
      this.spellCount = 0;
      this.rewarded = false;
      this.bobPhase = Math.random() * TAU;
      this.lockMove = false;
      this.attackLabel = '';
      this.mercy = 0;
      this.telegraphing = false;
      this.tellColor = P.teal;
      this.idleCd = 0.8;
      this.dive = null;
      this.desperate = false;
      // 破防槽：持续输出攒满后 Boss 停招 1.2 秒，期间受到额外伤害
      this.staggerMax = Math.max(60, Math.round(this.maxHp * 0.18));
      this.stagger = 0;
      this.broken = 0;
      this.breaks = 0;
    }

    get radius() { return this.size * 0.8; }

    update(dt) {
      this.t += dt;
      this.flash = Math.max(0, this.flash - dt);
      if (this.entering) {
        this.y += (this.targetY - this.y) * (1 - Math.exp(-3.2 * dt));
        if (this.y > this.targetY - 4) {
          this.y = this.targetY;
          this.entering = false;
          SF.Game.onBossReady(this);
        }
        return;
      }
      if (this.dying) {
        this.deathT += dt;
        this.y += 6 * dt;
        if (SF.chance(dt * 16)) {
          SF.Game.fx.explosion(
            this.x + SF.rand(-this.size, this.size),
            this.y + SF.rand(-this.size * 0.7, this.size * 0.7),
            SF.rand(0.35, 0.95), this.def.color
          );
        }
        this.burstTimer -= dt;
        if (this.burstTimer <= 0) {
          this.burstTimer = 0.42;
          const bx = this.x + SF.rand(-this.size * 0.9, this.size * 0.9);
          const by = this.y + SF.rand(-this.size * 0.6, this.size * 0.6);
          SF.Game.fx.explosion(bx, by, SF.rand(0.9, 1.6), this.def.color);
          SF.Game.fx.ring(bx, by, this.def.color, 12, 0.55, 150);
          SF.Game.fx.shake(5);
        }
        if (this.deathT > 2.6 && !this.rewarded) {
          this.rewarded = true;
          SF.Game.onBossDefeated(this);
        }
        return;
      }

      // phase
      const ratio = this.hp / this.maxHp;
      const newPhase = ratio < 0.30 ? 3 : ratio < 0.66 ? 2 : 1;
      if (newPhase !== this.phase) {
        this.phase = newPhase;
        this.flash = 0.5;
        // A phase change is a mercy window: the board is wiped, the rotation
        // restarts from the top of the new list, and the hull gets a beat of grace.
        this.attack = null;
        this.attackLabel = '';
        this.lockMove = false;
        this.dive = null;
        this.seqIdx = 0;
        this.mercy = 1.15;
        this.attackCd = 1.15;
        this.stagger = 0;
        this.broken = 0;
        SF.Game.onBossPhase(this);
        SF.Game.fx.ring(this.x, this.y, this.def.color, this.size * 0.6, 0.7, this.size * 3);
        SF.Game.fx.rays(this.x, this.y, this.def.color, 12, this.size * 2.6, 0.55);
        SF.Audio.sfx('warn');
      }
      if (this.mercy > 0) this.mercy -= dt;

      // Last-ditch overload: below 12% the hull stops pausing between spells.
      if (this.phase === 3 && !this.desperate && ratio < 0.12) {
        this.desperate = true;
        this.seqIdx = 0;
        this.flash = 0.6;
        this.attack = null;
        this.attackCd = 0.45;
        SF.Game.onBossDesperation(this);
      }

      // hover — faster, wider arcs in later phases (unless a pattern is repositioning us)
      if (!this.lockMove) {
        const sway = this.phase === 3 ? 0.27 : this.phase === 2 ? 0.225 : 0.19;
        const rate = 0.55 + (this.phase - 1) * 0.08;
        this.x = SF.W / 2 + Math.sin(this.t * rate + this.bobPhase) * SF.W * sway;
        this.y = this.targetY + Math.sin(this.t * 1.1) * (this.phase === 3 ? 12 : 7);
      }

      if (this.broken > 0) {
        this.broken -= dt;
        if (this.broken <= 0) {
          this.broken = 0;
          this.attackLabel = '';
          this.attackCd = Math.min(this.attackCd, 0.55);
        }
      } else if (this.attack) {
        this.updateAttack(dt);
      } else {
        this.attackCd -= dt;
        if (this.attackCd <= 0) this.chooseAttack();
        else this.idleFire(dt);
      }

      if (Math.random() < dt * 5) {
        SF.Game.fx.ember(this.x + SF.rand(-this.size, this.size), this.y + SF.rand(-this.size, this.size), this.def.color, SF.rand(1, 2.4), 0.4, 0, -20);
      }
    }

    sequence() {
      const list = this.def.phases[clamp(this.phase, 1, 3) - 1];
      return list && list.length ? list : this.def.phases[0];
    }

    chooseAttack() {
      const list = this.sequence();
      const name = list[this.seqIdx % list.length];
      this.seqIdx++;
      // Late in phase 3 the rotation wraps into a double cast: the boss follows
      // itself straight out of a spell instead of idling.
      this.spellCount++;
      this.startAttack(name);
    }

    startAttack(name) {
      // Negative clock = charging. Every timed volley and emitter naturally waits
      // for t >= 0, so the tell needs no special cases per pattern.
      this.attack = { name, t: -TELL_TIME, fired: {}, emit: 0, angle: Math.random() * TAU, data: null };
      this.tellColor = ATTACK_TELL[name] || P.teal;
      this.attackLabel = '预告 · ' + (ATTACK_LABELS[name] || '');
      this.attackDuration = ATTACK_DURATION[name] || 1.2;
      this.lockMove = !!MOVING_ATTACKS[name];
      this.telegraphing = true;
      SF.Game.fx.ring(this.x, this.y, this.tellColor, this.size * 0.4, TELL_TIME, this.size * 2.6, 0.9);
      SF.Game.fx.spark(this.x, this.y, this.tellColor, 6, 90, 0.3);
      SF.Audio.sfx('warn');
    }

    aimAngle() {
      const pl = SF.Game.player;
      return pl && pl.alive ? Math.atan2(pl.y - this.y, pl.x - this.x) : Math.PI / 2;
    }

    bulletSpeed() {
      const base = 152 + this.tier * 46;
      const ph = this.phase === 3 ? 1.18 : this.phase === 2 ? 1.06 : 1;
      return base * ph * (SF.Game && SF.Game.diff ? SF.Game.diff.bossBullet : 1);
    }

    shot(ang, speed, kind, opts) {
      opts = opts || {};
      this.shotFrom(this.x + (opts.ox || 0), this.y + this.size * 0.35 + (opts.oy || 0), ang, speed, kind, opts);
    }

    shotFrom(x, y, ang, speed, kind, opts) {
      opts = opts || {};
      SF.Game.enemyBullets.push(new Bullet(
        x, y, Math.cos(ang) * speed, Math.sin(ang) * speed,
        { kind: kind || 'orb', r: opts.r || 6, life: opts.life || 10, homing: opts.homing || 0,
          turn: opts.turn || 2.2, scale: opts.scale || 1,
          fuse: opts.fuse || 0, split: opts.split || 0, wave: opts.wave || 0 }
      ));
    }

    fan(n, spread, speed, kind) {
      const aim = this.aimAngle();
      for (let i = 0; i < n; i++) {
        this.shot(aim + (i - (n - 1) / 2) * spread, speed, kind || 'shard');
      }
    }

    ring(n, offset, speed, kind) {
      for (let i = 0; i < n; i++) this.shot((i / n) * TAU + offset, speed, kind || 'orb');
    }

    beam(ang, opts) {
      SF.Game.lasers.push(new Laser(this.x, this.y, ang,
        Object.assign({ boss: this, color: this.def.color }, opts)));
    }

    pulse(color, radius) {
      SF.Game.fx.ring(this.x, this.y, color, 10, 0.4, radius || this.size * 3, 0.75);
    }

    // Seekers with a delayed lock and a capped turn rate: they pressure movement
    // without being unavoidable.
    seekerVolley(n) {
      const spd = this.bulletSpeed();
      const aim = this.aimAngle();
      for (let i = 0; i < n; i++) {
        this.shot(aim + (i - (n - 1) / 2) * 0.42, spd * 0.58, 'homing',
          { r: 7, homing: 2.6, turn: 1.9 });
      }
      this.pulse(this.def.color, this.size * 1.8);
      SF.Audio.sfx('hit');
    }

    // Between spells the hull keeps poking — a boss should never feel passive.
    idleFire(dt) {
      if (this.mercy > 0 || this.phase < 2) return;
      this.idleCd -= dt;
      if (this.idleCd > 0) return;
      const spd = this.bulletSpeed();
      const aim = this.aimAngle();
      const n = this.phase === 3 ? 2 : 1;
      for (let i = 0; i < n; i++) {
        this.shot(aim + (i - (n - 1) / 2) * 0.22, spd * 0.92, 'needle', { r: 5 });
      }
      this.idleCd = this.phase === 3 ? 0.66 : 0.95;
    }

    at(a, time, fn) {
      if (a.t >= time && !a.fired[time]) { a.fired[time] = true; fn(); }
    }

    updateAttack(dt) {
      const a = this.attack;
      a.t += dt;
      if (a.t < 0) {
        // charging: no bullets yet, just the tell
        if (Math.random() < dt * 26) {
          SF.Game.fx.ember(this.x + SF.rand(-this.size, this.size), this.y + SF.rand(-this.size * 0.6, this.size * 0.8),
            this.tellColor, 1.6, 0.3, 0, -50);
        }
        return;
      }
      if (this.telegraphing) {
        this.telegraphing = false;
        this.attackLabel = ATTACK_LABELS[a.name] || '';
        SF.Game.fx.ring(this.x, this.y, this.tellColor, this.size * 0.5, 0.3, this.size * 3.2, 0.85);
      }
      const spd = this.bulletSpeed();
      const aim = this.aimAngle();

      const warpTo = (nx, ny, count) => {
        SF.Game.fx.ring(this.x, this.y, this.def.color, this.size * 0.5, 0.4, this.size * 2.4, 0.8);
        SF.Game.fx.spark(this.x, this.y, this.def.color, 8, 130, 0.35);
        this.x = clamp(nx, this.size * 1.25, SF.W - this.size * 1.25);
        this.y = clamp(ny, this.size * 1.2, SF.H * 0.4);
        this.flash = 0.2;
        SF.Game.fx.ring(this.x, this.y, this.def.color, this.size * 0.4, 0.45, this.size * 3, 0.9);
        SF.Audio.sfx('hit');
        this.ring(count, this.aimAngle(), spd * 0.78);
      };

      switch (a.name) {
        case 'fan': {
          const n = this.phase === 3 ? 9 : this.phase === 2 ? 7 : 5;
          this.at(a, 0.1, () => { this.fan(n, 0.17, spd); SF.Audio.sfx('hit'); });
          this.at(a, 0.5, () => this.fan(n, 0.12, spd));
          this.at(a, 0.9, () => this.fan(n + 2, 0.2, spd));
          this.at(a, 1.18, () => this.ring(this.phase >= 2 ? 12 : 8, aim, spd * 0.58));
          break;
        }
        case 'spiral':
          a.emit -= dt;
          if (a.emit <= 0) {
            a.emit = this.phase === 3 ? 0.052 : this.phase === 2 ? 0.062 : 0.082;
            const arms = this.phase === 3 ? 5 : this.phase === 2 ? 4 : 3;
            for (let i = 0; i < arms; i++) this.shot(a.angle + (i / arms) * TAU, spd * 0.72, 'orb');
            a.angle += 0.24;
          }
          break;
        case 'ring':
          this.at(a, 0.12, () => {
            this.ring(this.phase >= 2 ? 28 : 22, Math.random() * TAU, spd * 0.8);
            this.pulse(this.def.color, this.size * 2.6);
            SF.Audio.sfx('hit');
          });
          this.at(a, 0.55, () => this.ring(this.phase >= 3 ? 30 : 24, 0.22, spd * 0.58));
          this.at(a, 0.86, () => this.fan(this.phase >= 2 ? 5 : 3, 0.26, spd * 1.12));
          break;
        case 'homing':
          this.at(a, 0.15, () => this.seekerVolley(this.phase >= 2 ? 5 : 3));
          if (this.phase >= 2) this.at(a, 0.7, () => this.seekerVolley(3));
          break;
        case 'sweep':
          this.at(a, 0.1, () => {
            this.beam(aim - 0.55, { warn: 0.9, active: 0.9, width: 16, sweep: 0.9 });
            SF.Audio.sfx('warn');
          });
          this.at(a, 1.25, () => {
            this.beam(aim + 0.55, { warn: 0.8, active: 0.85, width: 14, sweep: -0.95 });
            SF.Audio.sfx('warn');
          });
          this.at(a, 2.15, () => this.fan(this.phase >= 2 ? 5 : 3, 0.24, spd));
          break;
        case 'cross':
          this.at(a, 0.15, () => {
            const base = Math.random() * 0.6;
            for (let i = 0; i < 4; i++) {
              this.beam(base + i * Math.PI / 2, { warn: 0.95, active: 0.62, width: 12 });
            }
            SF.Audio.sfx('warn');
          });
          this.at(a, 1.05, () => this.ring(this.phase >= 2 ? 14 : 10, aim, spd * 0.7));
          break;
        case 'summon':
          this.at(a, 0.2, () => {
            const roster = this.sector >= 4
              ? ['sniper', 'sidewinder', 'shardling']
              : this.sector >= 2 ? ['shardling', 'wasp'] : ['wasp'];
            const n = this.phase === 3 ? 4 : this.phase === 2 ? 3 : 2;
            for (let i = 0; i < n; i++) {
              const ex = this.x + (i - (n - 1) / 2) * this.size * 1.5;
              SF.Game.spawnEnemy(roster[i % roster.length], ex, this.y + this.size * 0.8, { sector: this.sector });
            }
            SF.Audio.sfx('hit');
          });
          break;
        case 'barrage':
          this.at(a, 0.12, () => {
            for (let i = 0; i < 5; i++) this.shot(aim + (i - 2) * 0.23, spd * 0.62, 'heavy', { r: 10 });
            if (this.phase >= 2) {
              this.shot(aim - 0.34, spd * 0.4, 'plasma', { r: 13, scale: 1.1, life: 3.4, split: 7 });
              this.shot(aim + 0.34, spd * 0.4, 'plasma', { r: 13, scale: 1.1, life: 3.4, split: 7 });
            }
            SF.Audio.sfx('hit');
          });
          this.at(a, 0.7, () => { for (let i = 0; i < 12; i++) this.shot((i / 12) * TAU + 0.3, spd * 0.72, 'shard'); });
          this.at(a, 1.2, () => this.fan(5, 0.2, spd));
          this.at(a, 1.68, () => this.ring(this.phase >= 2 ? 18 : 12, aim, spd * 0.9));
          break;
        case 'twinBeam':
          this.at(a, 0.12, () => {
            this.beam(aim - 0.65, { warn: 0.95, active: 0.9, width: 14, sweep: 0.6 });
            this.beam(aim + 0.65, { warn: 0.95, active: 0.9, width: 14, sweep: -0.6 });
            SF.Audio.sfx('warn');
          });
          this.at(a, 1.1, () => {
            const n = this.phase >= 2 ? 5 : 3;
            for (let i = 0; i < n; i++) {
              this.shot(aim + (i - (n - 1) / 2) * 0.18, spd * 1.05, 'needle', { r: 5, scale: 1.2 });
            }
            SF.Audio.sfx('hit');
          });
          this.at(a, 1.75, () => this.ring(this.phase === 3 ? 16 : 10, a.angle, spd * 0.66));
          break;
        case 'mineCurtain':
          this.at(a, 0.1, () => {
            for (let i = 0; i < 5; i++) this.shot(aim + (i - 2) * 0.26, 98, 'mine', { r: 9, fuse: 1.7, split: 9 });
            SF.Audio.sfx('hit');
          });
          this.at(a, 0.85, () => {
            const n = this.phase >= 2 ? 7 : 5;
            for (let i = 0; i < n; i++) {
              this.shot(Math.PI / 2 + (i - (n - 1) / 2) * 0.3, 82, 'mine', { r: 8, fuse: 2.1, split: 8 });
            }
            this.pulse(P.gold, 150);
          });
          if (this.phase >= 2) this.at(a, 1.5, () => this.fan(3, 0.42, spd * 1.05));
          break;
        case 'blink':
          this.at(a, 0.15, () => warpTo(SF.W * 0.24, this.targetY - 18, 12));
          this.at(a, 0.82, () => warpTo(SF.W * 0.74, this.targetY + 26, 14));
          this.at(a, 1.5, () => warpTo(SF.W * 0.5, this.targetY - 8, 18));
          break;
        case 'laserWeb':
          this.at(a, 0.12, () => {
            const base = aim - 0.6;
            for (let i = 0; i < 4; i++) {
              this.beam(base + i * Math.PI / 2, { warn: 0.85, active: 0.7, width: 11, sweep: i % 2 ? -0.6 : 0.6 });
            }
            SF.Audio.sfx('warn');
          });
          this.at(a, 1.05, () => {
            this.beam(aim - 0.5, { warn: 0.7, active: 0.6, width: 10 });
            this.beam(aim + 0.5, { warn: 0.7, active: 0.6, width: 10 });
            this.pulse(this.def.color, 130);
          });
          this.at(a, 1.85, () => this.ring(this.phase >= 2 ? 16 : 10, a.angle, spd * 0.82));
          break;
        case 'waveCurtain':
          this.at(a, 0.1, () => {
            for (let i = 0; i < 9; i++) this.shot(aim + (i - 4) * 0.17, spd * 0.72, 'wave', { r: 7, wave: 2.1, scale: 1.15 });
            SF.Audio.sfx('hit');
          });
          this.at(a, 0.95, () => {
            for (let i = 0; i < 7; i++) this.shot(aim + (i - 3) * 0.2, spd * 0.62, 'wave', { r: 6, wave: -2.4, scale: 1.1 });
          });
          if (this.phase >= 2) this.at(a, 1.62, () => {
            for (let i = 0; i < 5; i++) this.shot(aim + (i - 2) * 0.24, spd * 0.9, 'wave', { r: 6, wave: 1.6 });
          });
          break;

        /* ---- new: structured patterns that ask something of the player ---- */

        // Full-width curtain with one marked hole — a positioning puzzle, not a coin flip.
        case 'wallGap': {
          if (!a.data) a.data = { cols: Math.max(9, Math.round(SF.W / 40)), gap: 3, gapIdx: 0 };
          const d = a.data;
          d.gap = this.phase === 3 ? (this.tier === 0 ? 3 : 2) : (this.tier === 0 ? 4 : 3);
          d.cols = Math.max(9, Math.round(SF.W / 40));
          const step = SF.W / d.cols;
          const plan = () => {
            d.gapIdx = SF.randInt(0, Math.max(0, d.cols - d.gap));
            const gx = (d.gapIdx + d.gap / 2) * step;
            for (let i = 0; i < 4; i++) {
              SF.Game.fx.ring(gx, this.y + this.size * 0.5 + i * 30, P.teal, 5, 0.45, 24, 0.9);
            }
            SF.Game.fx.spark(gx, this.y + this.size, P.teal, 4, 70, 0.3);
            SF.Audio.sfx('warn');
          };
          const fire = () => {
            const y = this.y + this.size * 0.5;
            for (let c = 0; c < d.cols; c++) {
              if (c >= d.gapIdx && c < d.gapIdx + d.gap) continue;
              const x = clamp((c + 0.5) * step, 10, SF.W - 10);
              this.shotFrom(x, y, Math.PI / 2, spd * 0.55, 'needle', { r: 5 });
            }
            SF.Audio.sfx('hit');
          };
          this.at(a, 0.1, plan);
          this.at(a, 0.62, () => { fire(); this.fan(3, 0.5, spd * 0.9); });
          this.at(a, 1.4, plan);
          this.at(a, 1.92, fire);
          if (this.phase >= 2) this.at(a, 2.3, plan);
          this.at(a, 2.7, () => { fire(); this.ring(this.phase === 3 ? 14 : 8, aim, spd * 0.85); });
          break;
        }

        // Three rings at three speeds: the layers overtake each other and open lanes.
        case 'pulsar':
          this.at(a, 0.12, () => {
            this.ring(this.phase >= 2 ? 18 : 14, a.angle, spd * 1.12, 'shard');
            this.pulse(this.def.color, this.size * 2.2);
            SF.Audio.sfx('hit');
          });
          this.at(a, 0.7, () => {
            this.ring(this.phase >= 2 ? 24 : 18, a.angle + 0.16, spd * 0.6, 'orb');
            this.pulse(this.def.color, this.size * 2.8);
          });
          this.at(a, 1.28, () => {
            this.ring(this.phase === 3 ? 30 : 24, a.angle + 0.3, spd * 0.86, 'shard');
            this.pulse(this.def.color, SF.W * 0.8);
          });
          this.at(a, 1.8, () => this.fan(this.phase >= 2 ? 5 : 3, 0.3, spd * 1.18));
          break;

        // The hull becomes the hazard: telegraphed climb, aimed dive, point-blank burst.
        case 'charge': {
          if (!a.data) {
            a.data = {
              x0: this.x,
              x1: SF.Game.player ? SF.Game.player.x : SF.W / 2,
              y1: Math.min(SF.H * 0.74, this.targetY + SF.H * 0.42),
            };
          }
          const d = a.data;
          const dx = clamp(d.x1, this.size * 1.1, SF.W - this.size * 1.1);
          if (a.t < 0.9) {
            this.y = SF.damp(this.y, this.targetY - 30, 4, dt);
            this.x = SF.damp(this.x, dx, 1.3, dt);
            if (SF.chance(dt * 16)) {
              SF.Game.fx.ember(this.x + SF.rand(-this.size * 0.5, this.size * 0.5),
                this.y + this.size * 0.8, this.def.color, 2, 0.25, 0, 110);
            }
            if (!d.warned) { d.warned = true; SF.Audio.sfx('warn'); }
          } else if (a.t < 1.5) {
            const p = SF.easeOutCubic(clamp((a.t - 0.9) / 0.6, 0, 1));
            this.x = SF.lerp(d.x0, dx, p);
            this.y = SF.lerp(this.targetY - 30, d.y1, p);
            if (!d.diving) {
              d.diving = true;
              this.fan(3, 0.5, spd * 0.85);
              SF.Game.fx.rays(this.x, this.y, this.def.color, 8, this.size * 2, 0.4);
            }
            if (SF.chance(dt * 30)) SF.Game.fx.spark(this.x, this.y - this.size * 0.7, this.def.color, 2, 110, 0.22);
          } else if (a.t < 2.2) {
            if (!d.braked) {
              d.braked = true;
              this.ring(this.phase >= 2 ? 20 : 14, a.angle, spd * 0.78);
              this.fan(5, 0.24, spd * 0.95);
              SF.Game.fx.shake(9);
              SF.Game.fx.ring(this.x, this.y, this.def.color, 16, 0.5, SF.W, 0.85);
              SF.Audio.sfx('bigExplode');
            }
            this.y = SF.damp(this.y, d.y1 - 6, 5, dt);
            this.x = SF.damp(this.x, dx, 2, dt);
          } else {
            const p = clamp((a.t - 2.2) / 0.95, 0, 1);
            this.y = SF.lerp(d.y1, this.targetY, p);
            this.x = SF.damp(this.x, SF.W / 2, 2.2, dt);
          }
          break;
        }

        // Four arms that keep turning while bullets fill the gaps — outrun, don't memorise.
        case 'crossSweep':
          this.at(a, 0.15, () => {
            for (let i = 0; i < 4; i++) {
              this.beam(aim + i * Math.PI / 2, {
                warn: 1.0, active: 1.35, width: 13, sweep: (i % 2 ? -1 : 1) * 0.72,
              });
            }
            SF.Audio.sfx('warn');
          });
          this.at(a, 1.0, () => {
            this.ring(this.phase >= 2 ? 16 : 12, a.angle, spd * 0.62);
            SF.Audio.sfx('hit');
          });
          this.at(a, 1.62, () => {
            for (let i = 0; i < 4; i++) {
              this.beam(aim + Math.PI / 4 + i * Math.PI / 2, {
                warn: 0.72, active: 1.05, width: 11, sweep: (i % 2 ? 1 : -1) * 0.85,
              });
            }
          });
          this.at(a, 2.4, () => this.fan(this.phase >= 3 ? 7 : 5, 0.22, spd * 1.05));
          break;

        // Starfall from the top edge; one safe lane walks across the screen.
        case 'cascade': {
          if (!a.data) a.data = { col: 0 };
          a.emit -= dt;
          if (a.emit <= 0) {
            a.emit = this.phase === 3 ? 0.095 : this.phase === 2 ? 0.105 : 0.12;
            a.data.col++;
            const cols = 7;
            const step = SF.W / cols;
            const skip = a.data.col % cols;
            for (let c = 0; c < cols; c++) {
              if (c === skip) continue;
              if (this.phase === 3 && c === (skip + 1) % cols) continue;
              const x = clamp((c + 0.5) * step + SF.rand(-9, 9), 12, SF.W - 12);
              this.shotFrom(x, -22, Math.PI / 2 + SF.rand(-0.1, 0.1),
                spd * (0.6 + (a.data.col % 3) * 0.17), 'shard', { r: 5 });
            }
          }
          this.at(a, 0.2, () => SF.Audio.sfx('hit'));
          this.at(a, 1.5, () => {
            this.ring(this.phase >= 2 ? 14 : 10, aim, spd * 0.52);
            this.fan(3, 0.42, spd * 1.1);
          });
          break;
        }

        // Seekers in waves — delayed lock-on and a capped turn rate keep them jukable.
        case 'homingBarrage':
          this.at(a, 0.15, () => this.seekerVolley(3));
          this.at(a, 0.95, () => this.seekerVolley(3));
          this.at(a, 1.75, () => this.seekerVolley(this.phase >= 2 ? 4 : 3));
          if (this.phase === 3) this.at(a, 2.4, () => this.seekerVolley(3));
          break;

        // Counter-rotating arms weave a lattice that closes and reopens.
        case 'spiralDouble':
          a.emit -= dt;
          if (a.emit <= 0) {
            a.emit = this.phase >= 2 ? 0.075 : 0.09;
            const arms = 2;
            for (let i = 0; i < arms; i++) {
              this.shot(a.angle + (i / arms) * TAU, spd * 0.66, 'orb');
              this.shot(-a.angle + (i / arms) * TAU, spd * 0.82, 'shard');
            }
            a.angle += 0.2;
          }
          this.at(a, 1.25, () => this.fan(this.phase >= 2 ? 5 : 3, 0.3, spd * 1.05));
          break;

        // A ring of timed mines that shatter into a second pattern.
        case 'mineRing':
          this.at(a, 0.15, () => {
            const n = this.phase >= 2 ? 12 : 9;
            for (let i = 0; i < n; i++) {
              this.shot((i / n) * TAU + a.angle, 122, 'mine', { r: 8, fuse: 1.5, split: 7 });
            }
            this.pulse(P.gold, this.size * 2.4);
            SF.Audio.sfx('hit');
          });
          this.at(a, 1.05, () => {
            const n = this.phase >= 2 ? 8 : 6;
            for (let i = 0; i < n; i++) {
              this.shot((i / n) * TAU + a.angle + 0.4, 70, 'mine', { r: 8, fuse: 1.1, split: 6 });
            }
            this.fan(3, 0.42, spd);
          });
          break;

        // Signature overload: charge, then a triple shockwave with layered speeds.
        case 'novaBurst':
          if (a.t < 0.9 && SF.chance(dt * 32)) {
            SF.Game.fx.ember(this.x + SF.rand(-this.size, this.size),
              this.y + SF.rand(-this.size, this.size), this.def.color, 2.4, 0.3, 0, -70);
          }
          this.at(a, 0.9, () => {
            SF.Game.fx.flash('255,220,140', 0.16);
            SF.Game.fx.shake(12);
            SF.Game.fx.ring(this.x, this.y, this.def.color, 12, 0.55, SF.W * 0.9, 0.9);
            SF.Audio.sfx('bigExplode');
            this.ring(this.phase >= 2 ? 22 : 18, a.angle, spd * 1.15, 'shard');
          });
          this.at(a, 1.45, () => {
            this.ring(this.phase === 3 ? 26 : 22, a.angle + 0.12, spd * 0.72, 'orb');
            this.pulse(this.def.color, SF.W);
          });
          this.at(a, 2.05, () => {
            this.ring(this.phase >= 2 ? 18 : 12, a.angle + 0.25, spd * 0.95, 'shard');
            this.fan(this.phase >= 3 ? 7 : 5, 0.2, spd * 1.1);
          });
          break;
      }

      if (a.t >= this.attackDuration) {
        if (a.name === 'blink') {
          SF.Game.fx.ring(this.x, this.y, this.def.color, this.size * 0.5, 0.4, this.size * 2.4, 0.8);
          SF.Game.fx.spark(this.x, this.y, this.def.color, 8, 130, 0.35);
        }
        this.attack = null;
        this.attackLabel = '';
        this.lockMove = false;
        // Phase 3 chains every third spell, and a desperate hull never pauses at all.
        const chain = this.phase === 3 && (this.desperate || this.spellCount % (this.tier === 0 ? 4 : 3) === 0);
        this.attackCd = chain ? 0.16 : Math.max(0.3,
          this.phase === 3 ? 0.42 : this.phase === 2 ? 0.62 : 0.85);
        if (this.phase === 3 && SF.chance(this.desperate ? (this.tier === 0 ? 0.7 : 0.9) : (this.tier === 0 ? 0.45 : 0.65))) {
          this.ring(this.desperate ? 22 : 16, aim, spd * 0.75);
        }
      }
    }

    hit(dmg) {
      if (this.entering || this.dying) return;
      // 破防窗口内受到 1.35 倍伤害，奖励攒槽后的爆发
      const actual = dmg * (this.broken > 0 ? 1.35 : 1);
      this.hp -= actual;
      this.flash = 0.055;
      if (this.hp <= 0) {
        this.hp = 0;
        this.dying = true;
        this.attack = null;
        SF.Game.onBossDying(this);
        return;
      }
      if (this.broken <= 0) {
        this.stagger += actual;
        if (this.stagger >= this.staggerMax) {
          this.stagger = 0;
          this.broken = 1.2;
          this.breaks++;
          this.attack = null;
          this.attackLabel = '核心破防';
          this.lockMove = false;
          this.telegraphing = false;
          SF.Game.onBossBreak(this);
        }
      }
    }

    draw(ctx) {
      SF.Art.drawBoss(ctx, this, this.t);
      // charging tell: a tightening ring in the attack's threat colour
      if (this.attack && this.attack.t < 0) {
        const k = 1 + this.attack.t / TELL_TIME;      // 1 -> 0
        ctx.save();
        ctx.translate(this.x, this.y);
        ctx.globalCompositeOperation = 'lighter';
        ctx.strokeStyle = SF.hexA(this.tellColor || P.teal, 0.28 + k * 0.5);
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(0, 0, this.size * (0.6 + k * 1.5), 0, TAU);
        ctx.stroke();
        ctx.setLineDash([6, 10]);
        ctx.lineDashOffset = -this.t * 40;
        ctx.strokeStyle = SF.hexA(this.tellColor || P.teal, 0.4);
        ctx.lineWidth = 1.2;
        ctx.beginPath();
        ctx.arc(0, 0, this.size * (0.5 + k * 1.1), 0, TAU);
        ctx.stroke();
        ctx.restore();
      }
      if (this.entering) {
        ctx.save();
        ctx.strokeStyle = 'rgba(255,111,145,.25)';
        ctx.setLineDash([6, 10]);
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.size * 1.35, this.t * 0.5, this.t * 0.5 + Math.PI * 1.5);
        ctx.stroke();
        ctx.restore();
      }
    }
  }
  SF.Boss = Boss;
})();
