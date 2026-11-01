/* ============================================================
   game.js — state machine, combat, collisions, fx, loop
   ============================================================ */
(function () {
  'use strict';
  const SF = window.SF;
  const P = SF.PALETTE;
  const clamp = SF.clamp;
  const TAU = SF.TAU;

  const DRONE_LABELS = {
    gunner: '脉冲僚机', lance: '棱镜僚机', aegis: '守护僚机', swarm: '蜂群僚机',
  };
  // 线段-点最近距离：子弹用上一帧到当前帧的位移做扫掠判定，
  // 这样即使 20 FPS 下高速弹单帧位移超过判定直径，也不会直接穿过目标。
  function segClosest(ax, ay, bx, by, px, py) {
    const vx = bx - ax, vy = by - ay;
    const len2 = vx * vx + vy * vy;
    let t = 0;
    if (len2 > 0) t = ((px - ax) * vx + (py - ay) * vy) / len2;
    t = t < 0 ? 0 : t > 1 ? 1 : t;
    const x = ax + vx * t, y = ay + vy * t;
    const dx = x - px, dy = y - py;
    return { x, y, d2: dx * dx + dy * dy };
  }

  class Game {
    constructor() {
      this.canvas = null;
      this.ctx = null;
      this.stage = null;
      this.input = null;
      this.bg = null;
      this.ui = SF.UI;
      this.director = null;

      this.state = 'menu';
      const reduceMotion = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
      const moreContrast = !!(window.matchMedia && window.matchMedia('(prefers-contrast: more)').matches);
      const saved = SF.Storage.get('settings', {});
      this.settings = Object.assign({ sound: true, shake: true, lowFx: reduceMotion, haptic: true, difficulty: 'standard' },
        saved && typeof saved === 'object' ? saved : {});
      this.highContrast = moreContrast;
      this.diff = SF.DIFFICULTY[this.settings.difficulty] || SF.DIFFICULTY.standard;
      this.viewScale = 1;
      this.lastTime = 0;
      this.runTime = 0;
      this.maxMult = 1;
      this.hintTime = 0;
      this.godMode = false;
      this.uiReady = false;

      this.player = null;
      this.boss = null;
      this.playerBullets = [];
      this.enemyBullets = [];
      this.enemies = [];
      this.pickups = [];
      this.particles = [];
      this.texts = [];
      this.lasers = [];

      this.score = 0;
      this.lives = 3;
      this.kills = 0;
      this.grazes = 0;
      this.phases = 0;
      this.bossBreaks = 0;
      this.novasFired = 0;
      this.hitsTaken = 0;
      this.sectorHits = 0;
      this.sectorGrazes = 0;
      this.sectorStartTime = 0;
      this.combo = 0;
      this.comboTimer = 0;
      this.comboMax = 2.8;
      this.maxCombo = 0;
      this.sector = 1;

      this.shakeMag = 0;
      this.flashA = 0;
      this.flashColor = '255,255,255';
      this.freeze = 0;
      this.timeScale = 1;
      this.slowTimer = 0;
      this.slowScale = 1;
      this.bossWarnTimer = 0;
      this.sectorClearTimer = 0;
      this.deathTimer = 0;
      this.hints = [];
      this.lastExplodeSound = 0;
      this.resizeTimer = null;
      this.pickupMsgCount = 0;
      this.pickupMsgTimer = 0;

      this.fx = this.makeFx();
    }

    /* ---------------- setup ---------------- */
    init() {
      this.canvas = document.getElementById('game');
      this.ctx = this.canvas.getContext('2d', { alpha: false });
      this.stage = document.getElementById('stage');
      SF.Audio.setMuted(!this.settings.sound);
      this.measure(true);
      this.bg = new SF.Background();
      this.director = new SF.Director(this);
      this.input = new SF.Input(this.canvas);
      // 原生包不暴露作弊/调试接口；浏览器端保留给自动化回归使用
      const cap = window.Capacitor;
      const capNative = !!(cap && (cap.isNativePlatform ? cap.isNativePlatform() : cap.platform && cap.platform !== 'web'));
      const localHost = location.protocol === 'https:' && location.hostname === 'localhost';
      if (!capNative && !localHost) this.exposeDebug();
      this.hookEvents();
    }

    measure(initial) {
      const rect = this.canvas.getBoundingClientRect();
      if (!rect.width || !rect.height) return false;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const bw = Math.round(rect.width * dpr);
      const bh = Math.round(rect.height * dpr);
      if (this.canvas.width === bw && this.canvas.height === bh && !initial) return false;
      this.canvas.width = bw;
      this.canvas.height = bh;
      this.viewScale = bw / SF.W;
      SF.H = bh / this.viewScale;
      this.ctx.imageSmoothingEnabled = true;
      this.ctx.setTransform(this.viewScale, 0, 0, this.viewScale, 0, 0);
      return true;
    }

    onResize() {
      const changed = this.measure(false);
      if (!changed) return;
      if (this.bg) this.bg.resize();
      if (this.player) {
        this.player.x = clamp(this.player.x, 31, SF.W - 31);
        // 与 Player.update 使用同一个底部下限，避免窗口变化时战机掉进仪表带
        this.player.y = clamp(this.player.y, SF.H * 0.18, SF.H - 104);
      }
      if (this.boss) {
        this.boss.targetY = Math.max(SF.H * 0.27, 252);
        this.boss.y = Math.min(this.boss.y, this.boss.targetY);
      }
      this.enemies.forEach((e) => { e.targetY = Math.min(e.targetY, SF.H * 0.35); });
      this.pickups.forEach((p) => { p.y = Math.min(p.y, SF.H - 20); });
      this.render();
    }

    hookEvents() {
      const schedule = () => {
        if (this.resizeTimer) clearTimeout(this.resizeTimer);
        this.resizeTimer = setTimeout(() => this.onResize(), 140);
      };
      window.addEventListener('resize', schedule);
      window.addEventListener('orientationchange', () => setTimeout(schedule, 200));
      if (window.ResizeObserver) {
        this.resizeObserver = new ResizeObserver(schedule);
        this.resizeObserver.observe(this.stage);
      }
      document.addEventListener('visibilitychange', () => {
        if (document.hidden && this.state === 'playing') this.pause();
      });
    }

    startLoop() {
      this.lastTime = performance.now();
      const loop = (ts) => {
        let dt = (ts - this.lastTime) / 1000;
        this.lastTime = ts;
        if (!isFinite(dt) || dt < 0) dt = 0;
        if (dt > 0.05) dt = 0.05;

        this.handleGlobalInput();
        if (this.state === 'playing') {
          this.updatePlaying(dt);
          if (this.uiReady) this.ui.update(this);
        } else if (this.state === 'menu') {
          this.bg.update(dt, { mode: 'menu' });
        } else if (this.state === 'over') {
          this.bg.update(dt * 0.25, { mode: 'game' });
        }
        this.render();
        requestAnimationFrame(loop);
      };
      requestAnimationFrame(loop);
    }

    handleGlobalInput() {
      const inp = this.input;
      if (!inp) return;
      if (inp.consume('mute')) this.toggleMute();

      if (this.state === 'playing') {
        if (inp.consume('pause')) { this.pause(); return; }
      } else if (this.state === 'paused') {
        if (inp.consume('pause')) { this.resume(); return; }
        if (inp.consume('restart')) { this.start(); return; }
      } else if (this.state === 'menu') {
        if (inp.consume('start') || inp.consume('restart')) { this.start(); return; }
      } else if (this.state === 'over') {
        if (inp.consume('restart') || inp.consume('start')) { this.start(); return; }
        if (inp.consume('pause')) { this.toMenu(); return; }
      }
    }

    onBlur() {
      if (this.state === 'playing') this.pause();
    }

    onPointerDown() {
      SF.Audio.init();
      if (SF.Audio.ctx && SF.Audio.ctx.state === 'suspended' && !SF.Audio.paused) SF.Audio.ctx.resume();
      if (this.input) this.input.clearPressed();
    }

    onPointerUp() {}

    /* ---------------- settings ---------------- */
    setSetting(key, value) {
      this.settings[key] = value;
      SF.Storage.set('settings', this.settings);
      if (key === 'sound') SF.Audio.setMuted(!value);
      if (key === 'difficulty') this.diff = SF.DIFFICULTY[value] || SF.DIFFICULTY.standard;
      if (this.uiReady) this.ui.applySettings();
    }

    toggleMute() {
      this.setSetting('sound', !this.settings.sound);
      if (this.uiReady) this.ui.toast(this.settings.sound ? '音乐与音效已开启' : '音乐与音效已关闭');
    }

    tryDash() {
      if (this.state !== 'playing' || !this.player) return;
      if (!this.player.tryDash()) {
        if (this.player.dashCd > 0 && this.uiReady) this.ui.toast('冲刺冷却中');
      }
    }

    tryNova() {
      if (this.state !== 'playing' || !this.player) return;
      if (this.player.overdrive > 0) {
        if (this.uiReady) this.ui.toast('超频进行中');
        return;
      }
      if (this.player.nova < 100) {
        if (this.uiReady) this.ui.toast('NOVA 未充能 · 贴近弹幕擦弹');
        SF.Audio.sfx('ui');
        return;
      }
      this.player.fireNova();
    }

    /* ---------------- run lifecycle ---------------- */
    start() {
      SF.Audio.init();
      SF.Audio.setMuted(!this.settings.sound);
      SF.Audio.setPaused(false);
      SF.Audio.setIntensity(1);
      // 上一局可能停在 Boss 调式，新一局必须从波次音轨开始
      SF.Audio.setMood('waves');
      SF.Audio.startMusic();

      this.runStartBest = SF.Storage.num('best', 0, 0, 99999999);
      this.resetRun();
      this.state = 'playing';
      this.input.clearPressed();
      this.lastTime = performance.now();
      if (this.uiReady) {
        this.ui.setState('playing');
        this.ui.showScreen(null);
        this.ui.resetRun();
        this.ui.updateMenuStats();
        const info = SF.sectorInfo(1);
        const boss = SF.BOSS_TYPES[0];
        this.ui.update(this);
        this.ui.announce('SECTOR 01', info.name + ' // 巨构 ' + boss.name + ' 接近', { duration: 2200, bottom: true });
      }
      const touchUI = document.body.classList.contains('is-touch');
      this.hints = [
        { t: 2.4, html: '<b>拖动</b> 驾驶战机 · <b>自动开火</b>', duration: 3200 },
        { t: 8.4, html: '贴近弹幕边缘 <b>擦弹</b> · 积攒 NOVA', duration: 3800 },
        { t: 15.6, html: touchUI ? 'NOVA 就绪后点击 <b>星爆</b>' : 'NOVA 就绪后按 <b>X</b> 释放星爆', duration: 3600 },
        { t: 22.5, html: touchUI ? '危险时点 <b>冲刺</b> · 短暂无敌穿越弹幕' : '<b>SPACE</b> 冲刺穿越弹幕 · 短暂无敌', duration: 3800 },
        { t: 31, html: '拾取 <b>棱镜 / 蜂群</b> 解锁武器线 · 各自升到 <b>LV.5</b>', duration: 4600 },
        { t: 40, html: touchUI ? '武器线打空会 <b>失谐</b> · 排气到 <b>满</b> 才重新就绪，点武器胶囊换线攒 <b>共鸣</b>' : '武器线打空会 <b>失谐</b> · 排气到 <b>满</b> 才重新就绪，按 <b>Q</b> 换线更快、还能攒 <b>共鸣</b>', duration: 5200 },
        { t: 46, html: '青色 <b>换线弹箱</b> · 立刻切到蓄能最满的武器线并回充，过关必掉', duration: 4400 },
        { t: 50, html: touchUI ? '按住 <b>精准</b> 键慢速移动 · 弹道收束、判定点显形' : '按住 <b>SHIFT</b> 精准移动 · 弹道收束、判定点显形', duration: 4200 },
        { t: 56, html: touchUI ? '<b>冲刺</b>穿过弹幕触发 <b>相位</b> · 额外星爆充能' : '<b>SPACE</b> 穿过弹幕触发 <b>相位</b> · 额外星爆充能', duration: 4400 },
        { t: 63, html: '青色弧段 <b>装甲</b> · 金色箭头 <b>急速</b> · 玫红虚线 <b>爆裂</b>', duration: 4800 },
      ];
      this.render();
    }

    resetRun() {
      this.player = new SF.Player();
      this.boss = null;
      this.playerBullets = [];
      this.enemyBullets = [];
      this.enemies = [];
      this.pickups = [];
      this.particles = [];
      this.texts = [];
      this.lasers = [];
      this.score = 0;
      this.lives = 3;
      this.kills = 0;
      this.grazes = 0;
      this.phases = 0;
      this.bossBreaks = 0;
      this.novasFired = 0;
      this.hitsTaken = 0;
      this.sectorHits = 0;
      this.sectorGrazes = 0;
      this.sectorStartTime = 0;
      this.combo = 0;
      this.comboTimer = 0;
      this.maxCombo = 0;
      this.sector = 1;
      this.runTime = 0;
      this.maxMult = 1;
      this.hintTime = 0;
      this.shakeMag = 0;
      this.flashA = 0;
      this.freeze = 0;
      this.timeScale = 1;
      this.slowTimer = 0;
      this.bossWarnTimer = 0;
      this.sectorClearTimer = 0;
      this.deathTimer = 0;
      this.godMode = false;
      this.pickupMsgCount = 0;
      this.pickupMsgTimer = 0;
      this.diff = SF.DIFFICULTY[this.settings.difficulty] || SF.DIFFICULTY.standard;
      this.director.startSector(1);
    }

    pause() {
      if (this.state !== 'playing') return;
      this.state = 'paused';
      if (this.input) this.input.touchFocus = false;
      if (this.uiReady && this.ui.releaseHolds) this.ui.releaseHolds();
      SF.Audio.setPaused(true);
      this.input.clearPressed();
      if (this.uiReady) {
        this.ui.setState('paused');
        this.ui.showScreen('pause');
      }
    }

    resume() {
      if (this.state !== 'paused') return;
      this.state = 'playing';
      SF.Audio.setPaused(false);
      this.input.clearPressed();
      if (this.uiReady) {
        this.ui.setState('playing');
        this.ui.showScreen(null);
      }
      this.lastTime = performance.now();
    }

    toMenu() {
      if (this.state === 'playing' || this.state === 'paused') this.saveProgress();
      this.state = 'menu';
      this.boss = null;
      this.player = null;
      this.enemyBullets = [];
      this.playerBullets = [];
      this.enemies = [];
      this.pickups = [];
      this.particles = [];
      this.texts = [];
      this.lasers = [];
      this.hints = [];
      this.flashA = 0;
      this.shakeMag = 0;
      this.freeze = 0;
      this.slowTimer = 0;
      this.timeScale = 1;
      this.deathTimer = 0;
      this.director.stop();
      SF.Audio.setPaused(false);
      SF.Audio.setIntensity(0);
      SF.Audio.stopMusic();
      this.input.clearPressed();
      if (this.uiReady) {
        this.ui.setState('menu');
        this.ui.showScreen('menu');
        this.ui.updateMenuStats();
      }
    }

    gameOver() {
      if (this.state === 'over') return;
      this.state = 'over';
      this.deathTimer = 0;
      // R/Enter pressed mid-run would otherwise be consumed by the over screen on
      // its very first frame and restart before the result can be read
      this.input.clearPressed();
      SF.Audio.stopMusic();
      SF.Audio.setIntensity(0);
      SF.Audio.setPaused(false);
      SF.Audio.sfx('gameover');
      this.shakeMag = 0;
      this.flashA = 0;

      const prevSector = SF.Storage.num('bestSector', 1, 1, 999);
      const newRecord = this.score > (this.runStartBest || 0);
      if (newRecord) SF.Storage.set('best', this.score);
      SF.Storage.set('bestSector', Math.max(prevSector, this.sector));

      if (this.uiReady) {
        this.ui.update(this);
        this.ui.setState('over');
        this.ui.showResult({
          score: this.score,
          sector: this.sector,
          kills: this.kills,
          maxCombo: this.maxCombo,
          time: this.runTime * 1000,
          grazes: this.grazes,
          novas: this.novasFired,
          phases: this.phases,
          breaks: this.bossBreaks,
          hits: this.hitsTaken,
          maxMult: Math.max(1, this.maxMult),
          build: this.player ? {
            weapons: this.player.unlockedWeapons().map((k) => ({ k, lv: this.player.weapons[k].level })),
            drones: this.player.drones.map((d) => d.kind),
          } : null,
          newRecord,
        });
        this.ui.showScreen('over');
      }
    }

    saveProgress() {
      const prevBest = SF.Storage.num('best', 0, 0, 99999999);
      if (this.score > prevBest) SF.Storage.set('best', this.score);
      const prevSector = SF.Storage.num('bestSector', 1, 1, 999);
      SF.Storage.set('bestSector', Math.max(prevSector, this.sector));
      if (this.uiReady) this.ui.updateMenuStats();
    }

    /* ---------------- fx factory ---------------- */
    makeFx() {
      const g = this;
      const add = (p) => {
        if (g.particles.length < 480) g.particles.push(p);
      };
      const fx = {
        spark(x, y, color, count, speed, life) {
          const n = Math.round((count || 5) * (g.settings.lowFx ? 0.5 : 1));
          for (let i = 0; i < n; i++) {
            const a = Math.random() * TAU;
            const sp = (speed || 140) * SF.rand(0.35, 1.1);
            add(new SF.Particle(x, y, {
              vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
              life: (life || 0.35) * SF.rand(0.6, 1.3),
              size: SF.rand(2, 5), color, kind: 'spark', drag: 3.4,
            }));
          }
        },
        ember(x, y, color, size, life, vx, vy) {
          add(new SF.Particle(x, y, {
            vx: vx === undefined ? SF.rand(-18, 18) : vx,
            vy: vy === undefined ? SF.rand(-26, 4) : vy,
            life: life || 0.5,
            size: size || 2,
            color,
            kind: 'ember',
            drag: 0.6,
          }));
        },
        smoke(x, y, color, size, life) {
          add(new SF.Particle(x, y, {
            vx: SF.rand(-16, 16), vy: SF.rand(-20, 2),
            life: life || 0.8, size: size || 8, grow: 18,
            color, kind: 'smoke', drag: 1.2,
          }));
        },
        ring(x, y, color, r0, life, r1, alpha) {
          add(new SF.Particle(x, y, {
            life: life || 0.45, size: r0 || 8,
            grow: ((r1 || 60) - (r0 || 8)) / (life || 0.45),
            color, kind: 'ring', drag: 0,
            alpha: alpha === undefined ? 0.8 : alpha,
          }));
        },
        impact(x, y, color, power) {
          power = power || 1;
          const low = g.settings.lowFx;
          add(new SF.Particle(x, y, {
            life: 0.11, size: 7 * power, grow: 28 * power,
            color: P.white, kind: 'flash', drag: 0,
          }));
          const n = low ? 3 : Math.round(7 * power);
          for (let i = 0; i < n; i++) {
            const a = Math.random() * TAU;
            const sp = SF.rand(70, 230) * power;
            add(new SF.Particle(x, y, {
              vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
              life: SF.rand(0.12, 0.28), size: SF.rand(1.4, 3.2) * power,
              color: Math.random() < 0.45 ? P.white : color, kind: 'spark',
              drag: 4.2, streak: 1.8,
            }));
          }
          fx.ring(x, y, color, 2, 0.18, 20 * power, 0.55);
        },
        rays(x, y, color, count, length, life) {
          const n = Math.round((count || 16) * (g.settings.lowFx ? 0.45 : 1));
          for (let i = 0; i < n; i++) {
            const a = (i / n) * TAU + SF.rand(-0.08, 0.08);
            const sp = SF.rand(length * 0.55, length);
            add(new SF.Particle(x, y, {
              vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
              life: (life || 0.45) * SF.rand(0.65, 1.25),
              size: SF.rand(2, 5), color, kind: 'spark', drag: 2.1, streak: 3.2,
            }));
          }
        },
        sparkle(x, y, color, count) {
          const n = count || 8;
          for (let i = 0; i < n; i++) {
            const a = Math.random() * TAU;
            const sp = SF.rand(25, 105);
            add(new SF.Particle(x, y, {
              vx: Math.cos(a) * sp, vy: Math.sin(a) * sp - 20,
              life: SF.rand(0.28, 0.65), size: SF.rand(1.5, 4),
              color, kind: 'ember', drag: 1.7,
            }));
          }
        },
        nova(x, y) {
          const low = g.settings.lowFx;
          fx.ring(x, y, P.white, 8, 0.34, Math.max(SF.W, SF.H) * 0.72, 0.75);
          fx.ring(x, y, P.gold, 24, 0.58, Math.max(SF.W, SF.H) * 1.05, 0.55);
          fx.ring(x, y, P.teal, 48, 0.82, Math.max(SF.W, SF.H) * 1.35, 0.35);
          fx.rays(x, y, P.white, low ? 12 : 28, 1250, 0.42);
          fx.rays(x, y, P.gold, low ? 10 : 20, 850, 0.62);
          for (let i = 0; i < (low ? 8 : 24); i++) {
            const a = Math.random() * TAU;
            const d = SF.rand(20, Math.max(SF.W, SF.H) * 0.45);
            add(new SF.Particle(x + Math.cos(a) * d, y + Math.sin(a) * d, {
              vx: Math.cos(a) * 140, vy: Math.sin(a) * 140,
              life: SF.rand(0.25, 0.65), size: SF.rand(2, 6),
              color: i % 3 === 0 ? P.teal : P.gold, kind: 'flash', drag: 1.1,
            }));
          }
        },
        explosion(x, y, scale, color) {
          scale = Math.max(0.9, scale || 1);
          const low = g.settings.lowFx;
          const tint = color || P.gold;

          // hot core + soft coloured bloom
          add(new SF.Particle(x, y, {
            life: 0.18, size: 26 * scale, grow: 48 * scale,
            color: P.white, kind: 'flash', drag: 0,
          }));
          add(new SF.Particle(x, y, {
            life: 0.34, size: 18 * scale, grow: 72 * scale,
            color: tint, kind: 'flash', drag: 0,
          }));

          const sparks = Math.round((low ? 9 : 20) * Math.min(2.3, scale));
          for (let i = 0; i < sparks; i++) {
            const a = Math.random() * TAU;
            const sp = SF.rand(90, 470) * Math.min(1.7, scale);
            add(new SF.Particle(x, y, {
              vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
              life: SF.rand(0.34, 0.9) * Math.min(1.45, scale),
              size: SF.rand(2, 5) * Math.min(1.5, scale),
              color: Math.random() < 0.3 ? P.white : Math.random() < 0.5 ? P.gold : tint,
              kind: 'spark', drag: 1.9, streak: 2.4,
            }));
          }

          const embers = low ? 3 : 7;
          for (let i = 0; i < embers; i++) {
            add(new SF.Particle(x + SF.rand(-8, 8) * scale, y + SF.rand(-8, 8) * scale, {
              vx: SF.rand(-70, 70), vy: SF.rand(-120, -10),
              life: SF.rand(0.45, 1.0), size: SF.rand(2.5, 7) * Math.min(1.6, scale),
              color: tint, kind: 'ember', drag: 1.2,
            }));
          }
          for (let i = 0; i < (low ? 1 : 2); i++) {
            fx.smoke(x + SF.rand(-12, 12), y + SF.rand(-12, 12), 'rgba(120,100,180,.36)', SF.rand(6, 13) * scale, SF.rand(0.7, 1.2));
          }
          fx.ring(x, y, tint, 4 * scale, 0.36, 62 * scale, 0.42);
          fx.ring(x, y, P.white, 2 * scale, 0.24, 34 * scale, 0.5);
          fx.ring(x, y, tint, 10 * scale, 0.52, 96 * scale, 0.18);
          if (!low) fx.rays(x, y, tint, Math.round(10 * Math.min(2, scale)), 430 * scale, 0.3);
          fx.shake(4.2 * Math.min(2, scale));
        },
        shake(mag) {
          if (!g.settings.shake) return;
          g.shakeMag = Math.max(g.shakeMag, mag * (g.settings.lowFx ? 0.6 : 1));
        },
        flash(color, alpha) {
          if (g.settings.lowFx) alpha *= 0.5;
          g.flashColor = color || '255,255,255';
          g.flashA = Math.max(g.flashA, alpha);
        },
        text(x, y, text, color, size, opts) {
          g.texts.push(new SF.FloatText(x, y, text, Object.assign({ color, size }, opts || {})));
        },
        shock(x, y, color) {
          fx.ring(x, y, color, 20, 0.7, Math.max(SF.W, SF.H) * 0.9);
          fx.ring(x, y, P.white, 10, 0.4, 200);
          for (let i = 0; i < (g.settings.lowFx ? 8 : 22); i++) {
            const a = (i / 22) * TAU;
            add(new SF.Particle(x, y, {
              vx: Math.cos(a) * SF.rand(500, 900), vy: Math.sin(a) * SF.rand(500, 900),
              life: SF.rand(0.3, 0.7), size: SF.rand(2, 4), color,
              kind: 'spark', drag: 2.8, streak: 2.4,
            }));
          }
        },
      };

      return fx;
    }

    haptic(pattern) {
      if (this.settings.haptic) SF.vibrate(pattern);
    }

    hitstop(seconds) {
      this.freeze = Math.max(this.freeze, seconds);
    }

    slowmo(duration, scale) {
      // only a call that outlasts the current slow-mo may change its strength
      if (duration >= this.slowTimer) this.slowScale = scale;
      this.slowTimer = Math.max(this.slowTimer, duration);
    }

    /* ---------------- playing update ---------------- */
    updatePlaying(dt) {
      this.runTime += dt;
      // the parallax sky used to advance only in the menu and on the game-over
      // screen, so the whole starfield stood still during play
      if (this.bg) this.bg.update(dt, { mode: 'game' });
      // tutorials pause during boss fights: the clock they run on stops with them
      if (!this.boss) this.hintTime += dt;
      const liveMult = this.multiplier();
      if (liveMult > this.maxMult) this.maxMult = liveMult;
      this.shakeMag *= Math.exp(-7.5 * dt);
      if (this.shakeMag < 0.15) this.shakeMag = 0;
      this.flashA *= Math.exp(-6.5 * dt);
      if (this.flashA < 0.004) this.flashA = 0;

      if (this.slowTimer > 0) {
        this.slowTimer -= dt;
        this.timeScale = SF.damp(this.timeScale, this.slowScale, 9, dt);
      } else {
        this.timeScale = SF.damp(this.timeScale, 1, 7, dt);
      }

      if (this.freeze > 0) {
        this.freeze -= dt;
        return;
      }

      if (this.deathTimer > 0) {
        this.deathTimer -= dt;
        if (this.deathTimer <= 0) { this.gameOver(); return; }
      }

      // hints
      if (this.hints && this.hints.length) {
        for (let i = this.hints.length - 1; i >= 0; i--) {
          if (this.hintTime >= this.hints[i].t) {
            const h = this.hints.splice(i, 1)[0];
            if (this.uiReady) this.ui.hint(h.html, h.duration);
            if (h.t > 15 && this.input.hasTouch) {
              // point out the on-screen buttons once
              this.ui.toast('右下角：冲刺 / 星爆');
            }
          }
        }
      }

      if (this.bossWarnTimer > 0) {
        this.bossWarnTimer -= dt;
        if (this.bossWarnTimer <= 0) this.spawnBoss();
      }
      if (this.sectorClearTimer > 0) {
        this.sectorClearTimer -= dt;
        if (this.sectorClearTimer <= 0) this.advanceSector();
      }

      const w = dt * this.timeScale;

      this.director.update(w);

      if (this.player && this.player.alive) {
        this.player.update(w, this.input);
      }

      // player bullets
      for (let i = this.playerBullets.length - 1; i >= 0; i--) {
        const b = this.playerBullets[i];
        if (!b.update(w, this.player)) this.playerBullets.splice(i, 1);
      }
      // enemy bullets
      for (let i = this.enemyBullets.length - 1; i >= 0; i--) {
        const b = this.enemyBullets[i];
        if (!b.update(w, this.player)) this.enemyBullets.splice(i, 1);
      }
      this.pointDefense();
      this.trimEnemyBullets();

      // enemies
      for (let i = this.enemies.length - 1; i >= 0; i--) {
        const e = this.enemies[i];
        if (e.dead) { this.enemies.splice(i, 1); continue; }
        if (!e.update(w, this.player)) this.enemies.splice(i, 1);
      }

      // boss
      if (this.boss) this.boss.update(w);

      // lasers
      for (let i = this.lasers.length - 1; i >= 0; i--) {
        if (!this.lasers[i].update(w)) this.lasers.splice(i, 1);
      }

      // pickups
      for (let i = this.pickups.length - 1; i >= 0; i--) {
        const p = this.pickups[i];
        if (p.dead || !p.update(w, this.player)) this.pickups.splice(i, 1);
      }

      // particles & text
      for (let i = this.particles.length - 1; i >= 0; i--) {
        if (!this.particles[i].update(w)) this.particles.splice(i, 1);
      }
      for (let i = this.texts.length - 1; i >= 0; i--) {
        if (!this.texts[i].update(w)) this.texts.splice(i, 1);
      }

      this.collide();

      if (this.pickupMsgTimer > 0) {
        this.pickupMsgTimer -= dt;
        if (this.pickupMsgTimer <= 0) this.pickupMsgCount = 0;
      }

      if (this.combo > 0) {
        this.comboTimer -= w;
        if (this.comboTimer <= 0) {
          this.combo = 0;
          this.comboTimer = 0;
        }
      }
    }

    // 保险上限：超出时优先移除离玩家最远的弹幕，避免眼前的子弹凭空消失
    trimEnemyBullets() {
      const cap = 420;
      if (this.enemyBullets.length <= cap) return;
      const px = this.player ? this.player.x : SF.W / 2;
      const py = this.player ? this.player.y : SF.H;
      while (this.enemyBullets.length > cap) {
        let worst = 0;
        let worstD = -1;
        for (let i = 0; i < this.enemyBullets.length; i++) {
          const b = this.enemyBullets[i];
          const dx = b.x - px, dy = b.y - py;
          const d = dx * dx + dy * dy;
          if (d > worstD) { worstD = d; worst = i; }
        }
        this.enemyBullets.splice(worst, 1);
      }
    }

    pointDefense() {
      const p = this.player;
      if (!p || !p.alive || !this.enemyBullets.length) return;
      const zones = [];
      if (p.reflectT > 0) zones.push({ x: p.x, y: p.y, r: 50, field: true });
      for (const d of p.drones) {
        if (d.kind === 'aegis' && d.pdT <= 0) zones.push({ x: d.x, y: d.y, r: 38, drone: d });
      }
      if (!zones.length) return;

      const now = performance.now();
      let any = false;
      for (const z of zones) {
        if (z.drone) z.drone.pdT = 0.5;
        let cleared = 0;
        for (let i = this.enemyBullets.length - 1; i >= 0; i--) {
          const b = this.enemyBullets[i];
          const dx = b.x - z.x, dy = b.y - z.y;
          if (dx * dx + dy * dy < z.r * z.r) {
            this.enemyBullets.splice(i, 1);
            p.addNova(z.field ? 0.25 : 0.6);
            this.fx.spark(b.x, b.y, z.field ? P.gold : P.violet, 2, 60, 0.18);
            cleared++;
            any = true;
            if (z.drone && cleared >= 4) break;
          }
        }
        if (cleared) {
          this.fx.ring(z.x, z.y, z.field ? P.gold : P.violet, 3, 0.3, z.field ? 58 : 40, 0.45);
        }
      }
      if (any && now - (this.lastPdSound || 0) > 110) {
        this.lastPdSound = now;
        SF.Audio.sfx('graze');
      }
    }

    collide() {
      const player = this.player;
      const mult = this.multiplier();

      // player bullets vs enemies / boss
      for (let i = this.playerBullets.length - 1; i >= 0; i--) {
        const b = this.playerBullets[i];
        let remove = false;

        const canHit = (target) => {
          if (!b.hitSet) return true;
          if (b.hitSet.has(target)) return false;
          b.hitSet.add(target);
          return true;
        };
        const consumeHit = () => {
          if (b.pierce > 0) b.pierce--;
          else remove = true;
        };

        if (this.boss && !this.boss.entering && !this.boss.dying) {
          const rx = this.boss.size * 1.02 + b.r;
          const ry = this.boss.size * 0.82 + b.r;
          const c = segClosest(b.prevX, b.prevY, b.x, b.y, this.boss.x, this.boss.y);
          const nx = (c.x - this.boss.x) / rx;
          const ny = (c.y - this.boss.y) / ry;
          if (nx * nx + ny * ny <= 1 && canHit(this.boss)) {
            this.boss.hit(b.dmg * (b.bossBonus || 1));
            if (player) player.addNova(0.12);
            this.fx.impact(c.x, c.y, this.boss.def.color, 1.15);
            consumeHit();
          }
        }

        if (!remove) {
          for (let j = this.enemies.length - 1; j >= 0; j--) {
            const e = this.enemies[j];
            if (!e.targetable()) continue;
            const c = segClosest(b.prevX, b.prevY, b.x, b.y, e.x, e.y);
            const rr = e.radius + b.r;
            if (c.d2 < rr * rr && canHit(e)) {
              const dead = e.hit(b.dmg, c.x, c.y);
              if (player) player.addNova(0.06);
              if (dead) this.killEnemy(e);
              consumeHit();
              if (remove) break;
            }
          }
        }
        if (remove) this.playerBullets.splice(i, 1);
      }

      if (!player || !player.alive) return;

      // 相位冲刺：无敌帧内穿过弹幕给额外 NOVA / 分数，奖励主动穿缝
      if (player.dashT > 0) {
        for (let i = this.enemyBullets.length - 1; i >= 0; i--) {
          const b = this.enemyBullets[i];
          if (b.phased) continue;
          const dx = b.x - player.x, dy = b.y - (player.y + 2);
          if (dx * dx + dy * dy < 34 * 34) {
            b.phased = true;
            this.phases++;
            this.grazes++;
            this.sectorGrazes++;
            player.addNova(0.55);
            this.score += Math.round(6 * mult);
            this.fx.spark(b.x, b.y, P.teal, 3, 80, 0.2);
            if (this.phases % 5 === 0) {
              this.fx.text(player.x, player.y - 44, 'PHASE ×' + this.phases, P.gold, 12, { life: 0.8 });
            }
          }
        }
      }

      // enemy bullets vs player
      if (player.invuln <= 0 && player.dashT <= 0) {
        for (let i = this.enemyBullets.length - 1; i >= 0; i--) {
          const b = this.enemyBullets[i];
          const c = segClosest(b.prevX, b.prevY, b.x, b.y, player.x, player.y + 2);
          const d2 = c.d2;
          const hitR = player.hitR + b.r;
          const grazeR = player.grazeR + b.r;

          if (!b.grazed && d2 < grazeR * grazeR && d2 > hitR * hitR) {
            b.grazed = true;
            this.grazes++;
            this.sectorGrazes++;
            player.addNova(0.9);
            this.score += Math.round(4 * mult);
            if (this.grazes % 10 === 0) this.fx.text(player.x, player.y - 42, 'GRAZE ×' + this.grazes, P.teal, 11, { life: 0.8 });
            if (this.grazes % 3 === 0) {
              SF.Audio.sfx('graze');
              this.fx.spark(b.x, b.y, P.teal, 2, 60, 0.2);
            }
          } else if (d2 < hitR * hitR) {
            this.enemyBullets.splice(i, 1);
            player.hitFlash = 0.25;
            player.takeHit();
            if (!player.alive || this.state !== 'playing') return;
            break;
          }
        }
      }

      // contact
      if (player.invuln <= 0 && player.dashT <= 0) {
        for (let j = this.enemies.length - 1; j >= 0; j--) {
          const e = this.enemies[j];
          if (!e.targetable()) continue;
          const dx = e.x - player.x, dy = e.y - (player.y + 2);
          const rr = e.radius + 10;
          if (dx * dx + dy * dy < rr * rr) {
            const dead = e.hit(999, e.x, e.y);
            if (dead) this.killEnemy(e);
            player.takeHit();
            break;
          }
        }
        if (this.boss && !this.boss.entering && !this.boss.dying) {
          const dx = this.boss.x - player.x, dy = this.boss.y - (player.y + 2);
          const rx = this.boss.size * 0.85 + 8;
          const ry = this.boss.size * 0.7 + 8;
          if ((dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) < 1) player.takeHit();
        }
      }

      // lasers
      if (player.invuln <= 0 && player.dashT <= 0) {
        for (const l of this.lasers) {
          if (!l.isActive()) continue;
          const dist = this.pointRayDistance(player.x, player.y + 2, l);
          if (dist < l.drawWidth * 0.5 + player.hitR) {
            player.takeHit();
            break;
          }
        }
      }

      // pickups
      for (let i = this.pickups.length - 1; i >= 0; i--) {
        const p = this.pickups[i];
        const dx = p.x - player.x, dy = p.y - (player.y + 2);
        if (dx * dx + dy * dy < 30 * 30) {
          this.pickups.splice(i, 1);
          this.collectPickup(p);
        }
      }
    }

    pointRayDistance(px, py, laser) {
      const dx = Math.cos(laser.angle), dy = Math.sin(laser.angle);
      const t = (px - laser.x) * dx + (py - laser.y) * dy;
      if (t < 0) return Math.hypot(px - laser.x, py - laser.y);
      if (t > laser.length) return Math.hypot(px - (laser.x + dx * laser.length), py - (laser.y + dy * laser.length));
      const cx = laser.x + dx * t, cy = laser.y + dy * t;
      return Math.hypot(px - cx, py - cy);
    }

    multiplier() {
      return 1 + Math.min(8, Math.floor(this.combo / 5));
    }

    pickupMsg(text, color, size) {
      if (!this.player) return;
      this.pickupMsgTimer = 0.7;
      const idx = this.pickupMsgCount % 3;
      this.pickupMsgCount++;
      this.fx.text(this.player.x, this.player.y - 48 - idx * 19, text, color, size || 12, { life: 1.1 });
    }

    killEnemy(e) {
      const idx = this.enemies.indexOf(e);
      if (idx >= 0) this.enemies.splice(idx, 1);
      if (e.dead && e.counted) return;
      e.counted = true;
      this.kills++;
      this.combo++;
      this.comboTimer = this.comboMax;
      if (this.combo > this.maxCombo) this.maxCombo = this.combo;
      const mult = this.multiplier();
      const points = Math.round(e.score * mult * this.diff.scoreMul);
      this.score += points;
      if (this.player) {
        this.player.addNova(e.elite ? 7 : 2.5);
        // 超频期间继续击杀会延长爆发窗口，鼓励在星爆里主动进攻
        if (this.player.overdrive > 0) {
          this.player.overdrive = Math.min(9.5, this.player.overdrive + (e.elite ? 0.26 : 0.11));
        }
      }

      const now = performance.now();
      if (now - this.lastExplodeSound > 65) {
        this.lastExplodeSound = now;
        SF.Audio.sfx(e.elite ? 'bigExplode' : 'explode');
      }
      this.fx.explosion(e.x, e.y, e.elite ? 1.35 : e.size / 22, e.glowColor);
      if (e.elite || e.type === 'bulwark') this.hitstop(0.025);

      if (Math.random() < 0.45 || e.elite || this.combo % 10 === 0) {
        this.fx.text(e.x, e.y - 14, '+' + points, e.elite ? P.gold : 'rgba(255,210,122,.85)', e.elite ? 15 : 11, { life: 0.7 });
      }
      if (this.combo > 0 && this.combo % 10 === 0) {
        this.fx.text(e.x, e.y - 34, 'CHAIN ×' + this.multiplier(), P.teal, 12, { life: 1 });
      }

      this.dropLoot(e);

      // 爆裂词缀：死亡延迟半秒后向四周炸开弹幕，必须离开原地
      if (e.affix === 'volatile') {
        this.enemyBullets.push(new SF.Bullet(e.x, e.y, 0, 0, {
          kind: 'plasma', r: 11, fuse: 0.5, split: 6, life: 1.2, scale: 1.1,
        }));
        this.fx.ring(e.x, e.y, P.rose, 6, 0.55, 70, 0.8);
      }

      if (e.type === 'splitter') {
        for (let k = 0; k < 2; k++) {
          const child = this.spawnEnemy('shardling', e.x + (k ? 16 : -16), e.y + 4, { sector: this.sector });
          if (child) {
            child.hp = Math.max(1, Math.round(child.maxHp * 0.5));
            child.maxHp = child.hp;
            child.entryDelay = 0.1;
          }
        }
      }
    }

    dropLoot(e) {
      if (e.type === 'rock') {
        if (SF.chance(0.8)) {
          for (let i = 0; i < 2; i++) {
            this.pickups.push(new SF.Pickup(e.x + SF.rand(-10, 10), e.y + SF.rand(-8, 8), 'gem'));
          }
        }
        return;
      }
      if (e.elite) {
        this.pickups.push(new SF.Pickup(e.x, e.y, this.pickUpgradeType()));
        for (let i = 0; i < 3; i++) this.pickups.push(new SF.Pickup(e.x + SF.rand(-20, 20), e.y + SF.rand(-10, 10), 'gem'));
        return;
      }
      if (!SF.chance(e.drop || 0)) return;
      const table = this.player && this.player.level < 5
        ? [['power', 38], ['shield', 17], ['drone', 13], ['prism', 6], ['swarm', 6], ['reflect', 5], ['life', 4], ['gem', 11]]
        : [['shield', 22], ['drone', 20], ['prism', 9], ['swarm', 9], ['reflect', 7], ['life', 4], ['power', 20], ['gem', 9]];
      let roll = Math.random() * 100;
      let type = 'gem';
      for (const [t, w] of table) {
        roll -= w;
        if (roll <= 0) { type = t; break; }
      }
      if (type === 'gem') {
        this.pickups.push(new SF.Pickup(e.x, e.y, 'gem'));
        this.pickups.push(new SF.Pickup(e.x + 12, e.y + 6, 'gem'));
      } else {
        this.pickups.push(new SF.Pickup(e.x, e.y, type));
      }
    }

    pickUpgradeType() {
      if (this.player && this.player.level < 5 && SF.chance(0.4)) return 'power';
      if (this.player && this.player.shieldCharges < 3 && SF.chance(0.45)) return 'shield';
      if (this.player && this.player.drones.length < 3 && SF.chance(0.55)) return 'drone';
      if (this.player && !this.player.weapons.prism.unlocked && SF.chance(0.4)) return 'prism';
      if (this.player && !this.player.weapons.swarm.unlocked && SF.chance(0.4)) return 'swarm';
      if (this.player && SF.chance(0.45)) return SF.chance(0.5) ? 'prism' : 'reflect';
      return SF.pick(['power', 'prism', 'shield', 'life']);
    }

    collectPickup(p) {
      const player = this.player;
      if (!player) return;
      const mult = this.multiplier();
      switch (p.type) {
        case 'gem':
          this.score += 25 * mult;
          player.addNova(0.5);
          if (Math.random() < 0.35) {
            this.fx.spark(p.x, p.y, P.gold, 3, 70, 0.25);
            SF.Audio.sfx('pickup');
          }
          break;
        case 'power':
          if (player.addPower()) {
            SF.Audio.sfx('power');
            this.pickupMsg('POWER LV.' + player.level, P.teal, 13);
            this.fx.ring(player.x, player.y, P.teal, 14, 0.5, 90);
            this.fx.sparkle(p.x, p.y, P.teal, 12);
          } else {
            this.score += 800;
            SF.Audio.sfx('pickup');
            this.pickupMsg('POWER MAX +800', P.gold, 12);
          }
          break;
        case 'shield':
          if (player.addShield(1)) {
            SF.Audio.sfx('power');
            this.pickupMsg('SHIELD ' + player.shieldCharges + '/3', P.violet, 13);
            this.fx.ring(player.x, player.y, P.violet, 14, 0.5, 92);
            this.fx.sparkle(p.x, p.y, P.violet, 12);
          } else {
            this.score += 500;
            SF.Audio.sfx('pickup');
            this.pickupMsg('SHIELD MAX +500', P.gold, 11);
          }
          break;
        case 'reflect':
          player.reflectT = Math.max(player.reflectT, 8);
          SF.Audio.sfx('power');
          this.pickupMsg('PRISM FIELD 8s', P.gold, 12);
          this.fx.ring(player.x, player.y, P.gold, 18, 0.6, 110);
          this.fx.ring(player.x, player.y, P.teal, 12, 0.5, 78);
          this.fx.sparkle(p.x, p.y, P.gold, 16);
          break;
        case 'prism':
        case 'swarm': {
          const result = player.unlockWeapon(p.type);
          if (result === 'max') {
            this.score += 1200;
            SF.Audio.sfx('pickup');
            this.pickupMsg(p.type === 'prism' ? '棱镜 MAX +1200' : '蜂群 MAX +1200', P.gold, 11);
          } else {
            SF.Audio.sfx('power');
            const label = p.type === 'prism' ? '棱镜贯穿' : '蜂群追踪';
            this.pickupMsg(result === 'unlocked' ? '解锁 ' + label : label + ' LV.' + player.weapons[p.type].level,
              p.type === 'prism' ? P.violet : P.gold, 12);
            this.fx.ring(player.x, player.y, p.type === 'prism' ? P.violet : P.gold, 16, 0.5, 100);
            this.fx.sparkle(p.x, p.y, p.type === 'prism' ? P.violet : P.gold, 16);
          }
          break;
        }
        case 'swap': {
          const r = player.swapToBest();
          const line = SF.WEAPON_LINES[r.name] || SF.WEAPON_LINES.pulse;
          SF.Audio.sfx('power');
          this.pickupMsg(r.swapped
            ? '换线 · ' + SF.WEAPON_LINES[r.name].label + ' 蓄能 ' + r.charge + '%'
            : '同线回充 ' + r.charge + '%', line.tint, 12);
          this.fx.ring(player.x, player.y, line.tint, 16, 0.5, 96);
          this.fx.sparkle(p.x, p.y, line.tint, 14);
          break;
        }
        case 'drone': {
          const kind = player.addDrone();
          if (kind) {
            SF.Audio.sfx('power');
            const col = kind === 'lance' ? P.rose : kind === 'aegis' ? P.violet : kind === 'swarm' ? P.gold : P.teal;
            this.pickupMsg('ESCORT // ' + DRONE_LABELS[kind], col, 12);
            this.fx.ring(player.x, player.y, col, 14, 0.45, 78);
            this.fx.sparkle(p.x, p.y, col, 14);
          } else {
            this.score += 600;
            SF.Audio.sfx('pickup');
            this.pickupMsg('ESCORT MAX +600', P.gold, 11);
          }
          break;
        }
        case 'life':
          if (this.lives < 5) {
            this.lives++;
            SF.Audio.sfx('power');
            this.pickupMsg('HULL +1', P.rose, 13);
            this.fx.sparkle(p.x, p.y, P.rose, 16);
          } else {
            this.score += 1000;
            SF.Audio.sfx('pickup');
          }
          break;
      }
      if (this.uiReady) this.ui.update(this);
    }

    playerHit() {
      const player = this.player;
      if (!player || !player.alive) return;
      if (this.godMode) { player.grantInvuln(1.2); return; }

      if (player.reflectT > 0) {
        player.reflectT = 0;
        player.grantInvuln(1.5);
        player.hitFlash = 0.4;
        player.addNovaRaw(4);
        SF.Audio.sfx('playerHit');
        this.clearEnemyBullets(true);
        this.fx.explosion(player.x, player.y, 1.0, P.gold);
        this.fx.impact(player.x, player.y, P.gold, 2.4);
        this.fx.ring(player.x, player.y, P.teal, 16, 0.55, 130);
        this.fx.text(player.x, player.y - 42, '棱镜力场抵消', P.gold, 13, { life: 1 });
        this.fx.shake(9);
        this.fx.flash('255,210,122', 0.2);
        this.hitstop(0.05);
        this.haptic(40);
        return;
      }

      if (player.shieldCharges > 0) {
        player.shieldCharges--;
        player.grantInvuln(1.3);
        player.hitFlash = 0.4;
        player.addNovaRaw(8);
        SF.Audio.sfx('playerHit');
        this.fx.explosion(player.x, player.y, 0.7, P.violet);
        this.fx.impact(player.x, player.y, P.violet, 2.1);
        this.fx.text(player.x, player.y - 40,
          player.shieldCharges > 0 ? '护盾 -1 · 剩余 ' + player.shieldCharges : '护盾碎裂',
          P.violet, 13, { life: 0.9 });
        this.fx.shake(7);
        this.fx.flash('155,140,255', 0.16);
        this.hitstop(0.05);
        this.haptic(35);
        return;
      }

      const oldX = player.x;
      const oldY = player.y;

      this.lives = Math.max(0, this.lives - 1);
      this.hitsTaken++;
      this.sectorHits++;
      this.combo = 0;
      this.comboTimer = 0;
      // Pain feeds the star core: a hull hit is worth roughly 200 boss hits of
      // charging, so a struggling pilot can bank a screen-clearing nova.
      player.addNovaRaw(25);
      const lostDrone = player.applyHitPenalty();
      player.grantInvuln(2.8);
      player.x = SF.W / 2;
      player.y = SF.H - 120;
      player.dragging = false;
      player.hitFlash = 0.55;
      this.deathTimer = 0;

      this.clearEnemyBullets(true);
      this.fx.explosion(oldX, oldY, 1.35, P.rose);
      this.fx.explosion(oldX, oldY, 0.8, P.teal);
      this.fx.text(oldX, oldY - 46, '星爆 +25', P.gold, 13, { life: 1 });
      this.fx.flash('255,111,145', 0.4);
      this.fx.shake(15);
      this.slowmo(0.3, 0.5);
      this.hitstop(0.05);
      SF.Audio.sfx('playerHit');
      this.haptic([80, 40, 120]);

      if (this.lives <= 0) {
        player.alive = false;
        this.deathTimer = 1.8;
        this.slowmo(1.2, 0.28);
        this.fx.explosion(oldX, oldY, 2.4, P.rose);
        if (this.uiReady) this.ui.announce('SIGNAL LOST', '战机信号中断', { alert: true, duration: 1800 });
      } else {
        this.fx.ring(player.x, player.y, P.teal, 8, 0.42, 64, 0.55);
        if (this.uiReady) {
          const sub = lostDrone ? '武器降级 · 僚机失联' : '武器降级 · 蓄能受损';
          this.ui.announce('HULL BREACH', sub, { alert: true, duration: 1500 });
        }
      }
    }

    clearEnemyBullets(scoreThem) {
      const n = this.enemyBullets.length;
      for (let i = 0; i < Math.min(n, 90); i++) {
        const b = this.enemyBullets[i];
        this.fx.spark(b.x, b.y, P.teal, 1, 50, 0.18);
      }
      if (scoreThem && n) this.score += n * 4;
      this.enemyBullets.length = 0;
    }

    spawnEnemy(type, x, y, opts) {
      // 保险上限：满员时拒绝新生成，而不是把场上已有的敌机悄悄挤掉
      if (this.enemies.length >= 42) return null;
      opts = opts || {};
      const e = new SF.Enemy(type, x, y, Object.assign({}, opts, { sector: this.sector }));
      this.enemies.push(e);
      return e;
    }

    /* ---------------- boss lifecycle ---------------- */
    startBossWarning(delay) {
      if (this.boss || this.bossWarnTimer > 0) return;
      this.bossWarnTimer = delay || 3.0;
      SF.Audio.setIntensity(2);
      SF.Audio.setMood('boss');
      SF.Audio.sfx('warn');
      if (this.uiReady) {
        this.ui.announce('WARNING', '巨构信号接近 // MASSIVE SIGNAL', { alert: true, duration: Math.max(1600, (delay || 3) * 900) });
        this.ui.warnPulse(Math.max(1600, (delay || 3) * 900));
        this.ui.hideHint();
      }
      this.enemies.forEach((e) => {
        this.fx.ring(e.x, e.y, P.violet, 4, 0.4, 40);
        this.fx.spark(e.x, e.y, P.violet, 3, 100, 0.3);
      });
      this.enemies.length = 0;
      this.clearEnemyBullets(false);
    }

    spawnBoss() {
      this.bossWarnTimer = 0;
      this.boss = new SF.Boss(this.sector);
      this.boss.y = -this.boss.size * 2.4;
      this.boss.targetY = Math.max(SF.H * 0.27, 252);
      this.lasers.length = 0;
      SF.Audio.sfx('warn');
      this.fx.flash('255,111,145', 0.12);
      if (this.uiReady) this.ui.hideHint();
    }

    onBossReady(boss) {
      SF.Audio.setIntensity(2);
      this.fx.shake(6);
      this.fx.ring(boss.x, boss.y, boss.def.color, 40, 0.7, 260);
      if (this.uiReady) {
        this.ui.announce(boss.name, boss.title + ' // ' + boss.en, { alert: true, duration: 2400 });
      }
    }

    onBossBreak(boss) {
      this.bossBreaks++;
      SF.Audio.sfx('bigExplode');
      this.fx.shake(11);
      this.fx.flash('255,210,122', 0.22);
      this.fx.ring(boss.x, boss.y, P.gold, boss.size * 0.5, 0.45, boss.size * 3.2, 0.9);
      this.fx.rays(boss.x, boss.y, P.gold, 10, boss.size * 2.2, 0.4);
      this.hitstop(0.05);
      this.haptic([20, 30, 20]);
      if (this.uiReady) this.ui.announce('CORE BREAK', '核心破防 · 伤害窗口', { duration: 1200 });
    }

    onBossPhase(boss) {
      SF.Audio.setIntensity(boss.phase >= 3 ? 3 : 2);
      // Mercy window: the board is wiped so a phase change reads as a breath,
      // not as an unfair wall of leftover bullets.
      this.clearEnemyBullets(true);
      this.lasers.length = 0;
      if (this.player && this.player.alive) this.player.grantInvuln(0.75);
      this.fx.shake(10);
      this.fx.flash('255,111,145', 0.2);
      this.hitstop(0.06);
      this.haptic([20, 40, 20]);
      if (this.uiReady) {
        this.ui.announce('PHASE ' + boss.phase, '核心过载 · 弹幕强化', { alert: true, duration: 1400 });
      }
    }

    onBossDesperation(boss) {
      SF.Audio.setIntensity(3);
      this.fx.shake(16);
      this.fx.flash('255,210,120', 0.3);
      this.hitstop(0.08);
      this.haptic([30, 30, 30, 60]);
      SF.Audio.sfx('warn');
      this.fx.rays(boss.x, boss.y, boss.def.color, 16, boss.size * 4, 0.7);
      if (this.uiReady) {
        this.ui.announce('OVERLOAD', '核心临界 · 招式不再停顿', { alert: true, duration: 1600 });
      }
    }

    onBossDying(boss) {
      SF.Audio.sfx('bigExplode');
      SF.Audio.setIntensity(1);
      this.fx.flash('255,255,255', 0.45);
      this.fx.shake(22);
      this.slowmo(1.4, 0.35);
      this.hitstop(0.12);
      this.clearEnemyBullets(false);
      this.lasers.length = 0;
      if (this.player) this.player.grantInvuln(2.5);
      this.fx.explosion(boss.x, boss.y, 2.6, boss.def.color);
      this.haptic([60, 30, 100]);
      if (this.uiReady) this.ui.announce('CORE BREACH', '巨构核心崩解', { duration: 1800 });
    }

    onBossDefeated(boss) {
      // Clear rewards: speed, a clean run and grazing pay out separately, so
      // the clear card can show a breakdown instead of one opaque number.
      const clearT = Math.max(0, this.runTime - this.sectorStartTime);
      const timeBonus = Math.max(0, Math.round((90 - clearT) * 40));
      const cleanBonus = this.sectorHits === 0 ? 1200 + this.sector * 200 : 0;
      const grazeBonus = this.sectorGrazes * 20;
      const clearBonus = timeBonus + cleanBonus + grazeBonus;
      const bonus = Math.round((4000 + this.sector * 900 + clearBonus) * this.diff.scoreMul);
      this.score += bonus;
      this.kills++;
      const bx = boss.x, by = boss.y;
      this.fx.text(bx, by, '+' + bonus, P.gold, 18, { life: 1.6, vy: -30 });
      this.fx.shock(bx, by, boss.def.color);
      this.fx.explosion(bx, by, 2.8, P.gold);
      this.fx.flash('255,210,122', 0.5);
      this.fx.shake(18);
      SF.Audio.sfx('clear');
      this.haptic([80, 50, 140]);
      this.boss = null;
      this.lasers.length = 0;
      this.enemyBullets.length = 0;
      this.enemies.length = 0;
      this.director.onBossDefeated();
      this.sectorClearTimer = 4.4;
      this.slowTimer = 0;
      this.timeScale = 1;

      // guaranteed salvage — deployed within reach
      const px = this.player ? this.player.x : SF.W / 2;
      const py = clamp(this.player ? this.player.y - 240 : SF.H * 0.5, SF.H * 0.28, SF.H * 0.72);
      const loot = ['power', this.player && !this.player.weapons.prism.unlocked ? 'prism'
        : this.player && !this.player.weapons.swarm.unlocked ? 'swarm' : 'power'];
      loot.push(this.player && this.player.shieldCharges >= 2 ? 'reflect' : 'shield');
      loot.push(this.player && this.player.drones.length < 3 ? 'drone' : 'life');
      // Once there is somewhere to rotate to, every sector clear hands out a crate
      // that does the rotating for you — mono-weapon runs should not be the default.
      if (this.player && this.player.unlockedWeapons().length >= 2) loot.push('swap');
      if (this.lives < 3) loot.push('life');
      const gap = Math.min(56, (SF.W - 96) / Math.max(1, loot.length - 1));
      loot.forEach((type, i) => {
        const lx = clamp(px + (i - (loot.length - 1) / 2) * gap, 46, SF.W - 46);
        this.pickups.push(new SF.Pickup(lx, py - Math.abs(i - (loot.length - 1) / 2) * 14, type));
        this.fx.ring(lx, py, P.gold, 5, 0.5, 46);
      });
      for (let i = 0; i < 5; i++) {
        const gx = clamp(px + SF.rand(-90, 90), 30, SF.W - 30);
        this.pickups.push(new SF.Pickup(gx, py + SF.rand(-20, 40), 'gem'));
      }
      this.saveProgress();
      if (this.uiReady) {
        const parts = ['时间 +' + timeBonus];
        if (cleanBonus) parts.push('无伤 +' + cleanBonus);
        parts.push('擦弹 +' + grazeBonus);
        this.ui.bossWarn();
        this.ui.announce('SECTOR CLEAR', parts.join(' · '), { duration: 3200, bottom: true });
        this.ui.hint('整备阶段 · 拾取强化，准备跃迁', 3600);
      }
    }

    advanceSector() {
      this.sector++;
      // nothing from the previous sector comes along, not even a stray escort
      this.enemies.length = 0;
      this.enemyBullets.length = 0;
      this.lasers.length = 0;
      this.sectorStartTime = this.runTime;
      this.sectorHits = 0;
      this.sectorGrazes = 0;
      SF.Audio.setMood('waves');
      this.player.grantInvuln(1.5);
      this.player.x = SF.W / 2;
      this.player.y = SF.H - 130;
      this.director.startSector(this.sector);
      SF.Audio.setIntensity(1);
      this.saveProgress();
      if (this.uiReady) {
        const info = SF.sectorInfo(this.sector);
        const boss = SF.BOSS_TYPES[(this.sector - 1) % SF.BOSS_TYPES.length];
        this.ui.announce('SECTOR ' + SF.pad2(this.sector), info.name + ' // 巨构 ' + boss.name + ' 接近', { duration: 2200, bottom: true });
        this.ui.update(this);
      }
    }

    novaBlast() {
      const player = this.player;
      this.novasFired++;
      if (!player) return;
      SF.Audio.sfx('nova');
      this.fx.nova(player.x, player.y);
      this.fx.flash('255,255,255', 0.55);
      this.fx.shake(18);
      this.hitstop(0.1);
      this.slowmo(0.4, 0.45);

      let cleared = 0;
      for (const b of this.enemyBullets) {
        this.fx.spark(b.x, b.y, P.gold, 1, 80, 0.22);
        cleared++;
      }
      this.enemyBullets.length = 0;
      this.score += cleared * 10;

      for (let i = this.enemies.length - 1; i >= 0; i--) {
        const e = this.enemies[i];
        const dead = e.hit(32, e.x, e.y);
        if (dead) this.killEnemy(e);
      }
      if (this.boss && !this.boss.entering && !this.boss.dying) {
        this.boss.hit(Math.round(this.boss.maxHp * 0.06) + 30);
      }
      this.lasers.length = 0;
      this.fx.shock(player.x, player.y, P.gold);
      this.fx.ring(player.x, player.y, P.teal, 28, 0.5, 360);
      this.fx.text(player.x, player.y - 58, 'NOVA', P.gold, 20, { life: 1.1, vy: -34 });
      this.haptic([30, 25, 70]);
      if (this.uiReady) this.ui.toast('NOVA 释放 · 超频 7 秒');
    }

    /* ---------------- render ---------------- */
    render() {
      const ctx = this.ctx;
      if (!ctx) return;
      const W = SF.W, H = SF.H;
      ctx.setTransform(this.viewScale, 0, 0, this.viewScale, 0, 0);
      ctx.globalAlpha = 1;
      ctx.globalCompositeOperation = 'source-over';
      ctx.fillStyle = '#04060f';
      ctx.fillRect(0, 0, W, H);

      const shake = this.shakeMag > 0 ? this.shakeMag : 0;
      ctx.save();
      if (shake > 0) {
        ctx.translate(SF.rand(-shake, shake), SF.rand(-shake, shake));
      }

      const danger = this.boss && !this.boss.dying ? 0.75 : 0;
      this.bg.draw(ctx, {
        mode: this.state === 'menu' ? 'menu' : 'game',
        danger,
        sector: this.sector,
      });

      if (this.state !== 'menu' && this.player) {
        this.drawWorld(ctx);
      }

      ctx.restore();

      if (this.flashA > 0.004) {
        ctx.fillStyle = 'rgba(' + this.flashColor + ',' + Math.min(0.75, this.flashA) + ')';
        ctx.fillRect(0, 0, W, H);
      }
    }

    drawWorld(ctx) {
      for (const p of this.pickups) p.draw(ctx);
      for (const e of this.enemies) e.draw(ctx, e.t);
      if (this.boss) {
        if (this.boss.dying) {
          ctx.save();
          ctx.globalAlpha = Math.max(0.22, 1 - this.boss.deathT * 0.3);
          this.boss.draw(ctx);
          ctx.restore();
        } else {
          this.boss.draw(ctx);
        }
      }

      for (const b of this.playerBullets) b.draw(ctx);
      for (const b of this.enemyBullets) b.draw(ctx);

      if (this.player) this.player.draw(ctx);

      for (const l of this.lasers) l.draw(ctx);

      ctx.save();
      ctx.globalCompositeOperation = 'lighter';
      for (const p of this.particles) p.draw(ctx);
      ctx.restore();

      for (const t of this.texts) t.draw(ctx);
    }

    /* ---------------- debug & verification ---------------- */
    exposeDebug() {
      const g = this;
      window.__SF = {
        game: g,
        start: () => g.start(),
        pause: () => g.pause(),
        resume: () => g.resume(),
        toMenu: () => g.toMenu(),
        snapshot: () => g.snapshot(),
        startBoss: () => g.debugStartBoss(),
        damageBoss: (n) => { if (g.boss) g.boss.hit(n || 100); },
        setGod: (v) => { g.godMode = v !== false; },
        addScore: (n) => { g.score += n || 1000; },
        simulate: (seconds, opts) => g.stress(seconds, opts),
        spawn: (type, x, y, opts) => g.spawnEnemy(type, x === undefined ? SF.W * 0.5 : x, y === undefined ? -60 : y, opts),
        give: (type) => {
          if (!g.player) return false;
          g.collectPickup(new SF.Pickup(g.player.x, g.player.y, type));
          return true;
        },
        fireBossAttack: (name) => {
          if (!g.boss) return false;
          g.boss.entering = false;
          g.boss.y = g.boss.targetY;
          g.boss.attack = null;
          g.boss.startAttack(name);
          return true;
        },
      };
    }

    snapshot() {
      const p = this.player;
      const types = {};
      for (const e of this.enemies) types[e.type] = (types[e.type] || 0) + 1;
      const bulletKinds = {};
      for (const b of this.enemyBullets) bulletKinds[b.kind] = (bulletKinds[b.kind] || 0) + 1;
      return {
        state: this.state,
        score: this.score,
        best: SF.Storage.get('best', 0),
        lives: this.lives,
        sector: this.sector,
        kills: this.kills,
        combo: this.combo,
        maxCombo: this.maxCombo,
        phases: this.phases,
        bossBreaks: this.bossBreaks,
        nova: p ? Math.round(p.nova) : 0,
        level: p ? p.level : 0,
        shield: p ? p.shieldCharges : 0,
        reflect: p ? Math.round(p.reflectT * 10) / 10 : 0,
        weaponMode: p ? p.activeWeapon : null,
        weaponLevels: p ? Object.keys(p.weapons).reduce((acc, k) => { acc[k] = p.weapons[k].level; return acc; }, {}) : {},
        unlockedWeapons: p ? p.unlockedWeapons() : [],
        drones: p ? p.drones.map((d) => d.kind) : [],
        player: p ? { x: p.x, y: p.y, alive: p.alive, invuln: p.invuln } : null,
        boss: this.boss ? {
          type: this.boss.type, hp: this.boss.hp, maxHp: this.boss.maxHp, phase: this.boss.phase,
          entering: this.boss.entering, dying: this.boss.dying,
          attack: this.boss.attack ? this.boss.attack.name : null,
          label: this.boss.attackLabel || '',
          broken: this.boss.broken, stagger: Math.round(this.boss.stagger),
        } : null,
        enemyTypes: types,
        bulletKinds: bulletKinds,
        counts: {
          enemies: this.enemies.length,
          playerBullets: this.playerBullets.length,
          enemyBullets: this.enemyBullets.length,
          pickups: this.pickups.length,
          particles: this.particles.length,
          lasers: this.lasers.length,
        },
        director: this.director ? this.director.state : null,
        runTime: this.runTime,
      };
    }

    debugStartBoss() {
      if (this.state !== 'playing') this.start();
      this.enemies.length = 0;
      this.enemyBullets.length = 0;
      this.hints = [];
      this.director.state = 'boss';
      this.startBossWarning(0.35);
    }

    stress(seconds, opts) {
      opts = opts || {};
      if (this.state !== 'playing') this.start();
      const prevGod = this.godMode;
      this.godMode = true;
      const wasMuted = SF.Audio.muted;
      SF.Audio.muted = true;
      const errors = [];
      const steps = Math.round((seconds || 120) * 60);
      const dt = 1 / 60;
      let nan = false;
      let maxBullets = 0;
      let maxEnemies = 0;
      let t = 0;
      let bossSeen = 0;
      let sectorsSeen = [this.sector];
      try {
        for (let i = 0; i < steps; i++) {
          if (this.state !== 'playing') break;
          t += dt;
          const p = this.player;
          if (p) {
            p.x = SF.W / 2 + Math.sin(t * 1.7) * SF.W * 0.3;
            p.y = SF.H - 160 + Math.sin(t * 2.3) * 45;
            if (i % 300 === 0) p.tryDash();
            if (p.nova >= 100) p.fireNova();
          }
          if (opts.boss && t > 12 && !this.boss && this.director.state !== 'boss') {
            this.debugStartBoss();
          }
          this.updatePlaying(dt);
          maxBullets = Math.max(maxBullets, this.enemyBullets.length);
          maxEnemies = Math.max(maxEnemies, this.enemies.length);
          if (this.boss) bossSeen++;
          if (!sectorsSeen.includes(this.sector)) sectorsSeen.push(this.sector);
          if (i % 30 === 0) {
            const arrs = [this.playerBullets, this.enemyBullets, this.enemies];
            for (const arr of arrs) {
              for (const o of arr) {
                if (!isFinite(o.x) || !isFinite(o.y)) { nan = true; }
              }
            }
            if (p && (!isFinite(p.x) || !isFinite(p.y))) nan = true;
            if (nan) throw new Error('NaN position detected at t=' + t.toFixed(2));
          }
        }
      } catch (e) {
        errors.push(String(e && e.stack || e));
      }
      const snap = this.snapshot();
      this.godMode = prevGod;
      SF.Audio.muted = wasMuted;
      return {
        ok: errors.length === 0 && !nan,
        errors,
        nan,
        simulated: Math.round(t),
        bulletsPeak: maxBullets,
        enemiesPeak: maxEnemies,
        bossFrames: bossSeen,
        sectorsSeen,
        snapshot: snap,
      };
    }
  }

  SF.GameClass = Game;
})();
