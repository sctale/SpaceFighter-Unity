/* ============================================================
   core.js — utilities, input, procedural audio
   ============================================================ */
(function () {
  'use strict';

  const SF = window.SF = window.SF || {};

  const TAU = Math.PI * 2;
  Object.assign(SF, {
    W: 540,
    H: 960,
    TAU,
    clamp(v, a, b) { return v < a ? a : v > b ? b : v; },
    lerp(a, b, t) { return a + (b - a) * t; },
    damp(a, b, lambda, dt) { return b + (a - b) * Math.exp(-lambda * dt); },
    rand(a = 0, b = 1) { return a + Math.random() * (b - a); },
    randInt(a, b) { return Math.floor(a + Math.random() * (b - a + 1)); },
    pick(arr) { return arr[Math.floor(Math.random() * arr.length)]; },
    chance(p) { return Math.random() < p; },
    dist(x1, y1, x2, y2) { return Math.hypot(x2 - x1, y2 - y1); },
    angleTo(x1, y1, x2, y2) { return Math.atan2(y2 - y1, x2 - x1); },
    pad2(n) { return String(n).padStart(2, '0'); },
    fmtTime(ms) {
      const s = Math.max(0, Math.floor(ms / 1000));
      return SF.pad2(Math.floor(s / 60)) + ':' + SF.pad2(s % 60);
    },
    easeOutCubic(t) { return 1 - Math.pow(1 - t, 3); },
    easeInQuad(t) { return t * t; },
    // '#rrggbb' -> 'rgba(r,g,b,a)' so a single palette entry can tint strokes,
    // gradients and glows without hand-writing colour strings.
    hexA(hex, a) {
      const h = String(hex).replace('#', '');
      const n = h.length === 3
        ? [h[0] + h[0], h[1] + h[1], h[2] + h[2]]
        : [h.slice(0, 2), h.slice(2, 4), h.slice(4, 6)];
      const r = parseInt(n[0], 16), g = parseInt(n[1], 16), b = parseInt(n[2], 16);
      return `rgba(${r},${g},${b},${a === undefined ? 1 : a})`;
    },
  });

  SF.vibrate = function (pattern) {
    try {
      if (navigator.vibrate) navigator.vibrate(pattern);
    } catch (e) {}
  };

  SF.Storage = {    get(key, fallback) {
      try {
        const raw = localStorage.getItem('sf.' + key);
        if (raw === null) return fallback;
        return JSON.parse(raw);
      } catch (e) { return fallback; }
    },
    set(key, value) {
      try { localStorage.setItem('sf.' + key, JSON.stringify(value)); } catch (e) {}
    },
    // stored blobs are user-editable and survive version changes: clamp on read
    num(key, fallback, min, max) {
      const v = Number(this.get(key, fallback));
      if (!Number.isFinite(v)) return fallback;
      return Math.min(max, Math.max(min, Math.round(v)));
    },
  };

  SF.PALETTE = {
    white: '#f3f8ff',
    teal: '#69f5dc',
    tealDeep: '#2bb3a2',
    violet: '#9b8cff',
    violetDeep: '#5f52c7',
    rose: '#ff6f91',
    roseDeep: '#b8395c',
    gold: '#ffd27a',
    goldDeep: '#d28f36',
    steel: '#8fa4c9',
    ink: '#04060f',
  };

  /* ---------------- Input ---------------- */
  class Input {
    constructor(canvas) {
      this.canvas = canvas;
      this.keys = new Set();
      this.pointer = { x: SF.W / 2, y: SF.H * 0.8, down: false, type: 'mouse', inside: false, id: null };
      this.pressed = { dash: false, nova: false, pause: false, mute: false, start: false, restart: false, weapon: false, weapon1: false, weapon2: false, weapon3: false };
      this.hasTouch = false;
      this.touchFocus = false;
      this.dragStart = null;
      this.attach();
    }

    attach() {
      const c = this.canvas;

      window.addEventListener('keydown', (e) => {
        const k = e.key;
        const low = k.toLowerCase();
        // a focused button owns Space/Enter — otherwise Space cannot activate it
        const ae = document.activeElement;
        const onControl = ae && /^(BUTTON|INPUT|TEXTAREA|SELECT)$/.test(ae.tagName) && ae !== document.body;
        if (onControl && (low === ' ' || low === 'enter')) return;
        if (['arrowup', 'arrowdown', 'arrowleft', 'arrowright', ' ', 'tab'].includes(low)) {
          if (low !== 'tab') e.preventDefault();
        }
        if (this.keys.has(low)) return;
        this.keys.add(low);
        if (low === ' ') this.pressed.dash = true;
        if (low === 'x' || low === 'j') this.pressed.nova = true;
        if (low === 'escape' || low === 'p') this.pressed.pause = true;
        if (low === 'm') this.pressed.mute = true;
        if (low === 'enter') this.pressed.start = true;
        if (low === 'r') this.pressed.restart = true;
        if (low === 'q' || low === 'e' || low === 'f') this.pressed.weapon = true;
        if (low === '1') this.pressed.weapon1 = true;
        if (low === '2') this.pressed.weapon2 = true;
        if (low === '3') this.pressed.weapon3 = true;
      }, { passive: false });

      window.addEventListener('keyup', (e) => {
        this.keys.delete(e.key.toLowerCase());
      });

      window.addEventListener('blur', () => {
        this.keys.clear();
        this.pointer.down = false;
        if (SF.Game) SF.Game.onBlur();
      });

      const map = (clientX, clientY) => {
        const r = c.getBoundingClientRect();
        return {
          x: SF.clamp((clientX - r.left) * (SF.W / r.width), 0, SF.W),
          y: SF.clamp((clientY - r.top) * (SF.H / r.height), 0, SF.H),
        };
      };

      c.addEventListener('pointerdown', (e) => {
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        // 已有一根手指在驾驶时，忽略后续手指，避免多指互相抢方向盘
        if (this.pointer.down && this.pointer.id !== null) return;
        if (e.pointerType === 'touch' || e.pointerType === 'pen') {
          this.hasTouch = true;
          document.body.classList.add('is-touch');
        }
        e.preventDefault();
        try { c.setPointerCapture(e.pointerId); } catch (err) {}
        const p = map(e.clientX, e.clientY);
        this.pointer.x = p.x;
        this.pointer.y = p.y;
        this.pointer.down = true;
        this.pointer.type = e.pointerType === 'mouse' ? 'mouse' : e.pointerType;
        this.pointer.inside = true;
        this.pointer.id = e.pointerId;
        this.dragStart = { x: p.x, y: p.y, t: performance.now() };
        if (SF.Game) SF.Game.onPointerDown(p, e.pointerType);
      }, { passive: false });

      c.addEventListener('pointermove', (e) => {
        // 只接受当前主指针的移动；第二根手指不能驱动战机
        if (this.pointer.down && this.pointer.id !== null && e.pointerId !== this.pointer.id) return;
        const isTouchLike = e.pointerType === 'touch' || e.pointerType === 'pen';
        if (!isTouchLike || this.pointer.down) {
          const p = map(e.clientX, e.clientY);
          this.pointer.x = p.x;
          this.pointer.y = p.y;
          this.pointer.inside = true;
          this.pointer.type = isTouchLike ? e.pointerType : 'mouse';
        }
      }, { passive: true });

      const release = (e) => {
        if (this.pointer.id !== null && e.pointerId !== this.pointer.id) return;
        this.pointer.down = false;
        this.pointer.id = null;
        this.dragStart = null;
        if (SF.Game) SF.Game.onPointerUp();
      };
      c.addEventListener('pointerup', release, { passive: true });
      c.addEventListener('pointercancel', release, { passive: true });

      c.addEventListener('contextmenu', (e) => e.preventDefault());

      c.addEventListener('pointerleave', (e) => {
        if (e.pointerType === 'mouse') this.pointer.inside = false;
      });
    }

    moveVector() {
      let x = 0, y = 0;
      if (this.keys.has('a') || this.keys.has('arrowleft')) x -= 1;
      if (this.keys.has('d') || this.keys.has('arrowright')) x += 1;
      if (this.keys.has('w') || this.keys.has('arrowup')) y -= 1;
      if (this.keys.has('s') || this.keys.has('arrowdown')) y += 1;
      if (x && y) { const inv = Math.SQRT1_2; x *= inv; y *= inv; }
      return { x, y, active: x !== 0 || y !== 0 };
    }

    // Precise mode: hold SHIFT/CTRL on desktop, hold the on-screen 精准 ring on
    // touch. Slow movement plus a tightened shot is the standard bullet-hell
    // answer to "I need to thread this gap".
    focusHeld() {
      return this.touchFocus === true || this.keys.has('shift') || this.keys.has('control');
    }

    consume(action) {
      if (!this.pressed[action]) return false;
      this.pressed[action] = false;
      return true;
    }

    clearPressed() {
      for (const k in this.pressed) this.pressed[k] = false;
    }
  }
  SF.Input = Input;

  /* ---------------- Difficulty ---------------- */
  // The same game, three readings: cruise softens density and pays nova faster,
  // abyss tightens intervals, speeds bullets, thickens boss hulls and pays more.
  SF.DIFFICULTY = {
    cruise: { id: 'cruise', name: '巡航', note: '弹幕放缓 · 星爆更勤 · 得分 ×0.8', enemyRate: 1.22, bulletSpeed: 0.9, bossBullet: 0.86, bossHp: 0.9, nova: 1.2, scoreMul: 0.8 },
    standard: { id: 'standard', name: '标准', note: '设计基准 · 原汁原味', enemyRate: 1, bulletSpeed: 1, bossBullet: 1, bossHp: 1, nova: 1, scoreMul: 1 },
    abyss: { id: 'abyss', name: '深渊', note: '弹幕更快 · Boss 更硬 · 得分 ×1.35', enemyRate: 0.8, bulletSpeed: 1.1, bossBullet: 1.12, bossHp: 1.18, nova: 0.85, scoreMul: 1.35 },
  };

  /* ---------------- Audio engine ---------------- */
  class AudioEngine {
    constructor() {
      this.ctx = null;
      this.master = null;
      this.musicBus = null;
      this.sfxBus = null;
      this.delay = null;
      this.noiseBuffer = null;
      this.muted = false;
      this.musicOn = false;
      this.intensity = 0;
      this.mood = 'waves';
      this.timer = null;
      this.step = 0;
      this.nextStepTime = 0;
      this.stepDur = 60 / 116 / 4;
      this.lastShoot = 0;
      this.lastGraze = 0;
      this.lastHit = 0;
      this.paused = false;
    }

    init() {
      if (this.ctx) {
        if (this.ctx.state === 'suspended') this.ctx.resume();
        return;
      }
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return;
      try {
        const ctx = this.ctx = new AC();
        const comp = ctx.createDynamicsCompressor();
        comp.threshold.value = -18;
        comp.knee.value = 24;
        comp.ratio.value = 5;
        comp.attack.value = 0.004;
        comp.release.value = 0.22;

        this.master = ctx.createGain();
        this.master.gain.value = this.muted ? 0 : 0.72;
        this.master.connect(comp);
        comp.connect(ctx.destination);

        this.musicBus = ctx.createGain();
        this.musicBus.gain.value = 0.5;
        this.musicBus.connect(this.master);

        this.sfxBus = ctx.createGain();
        this.sfxBus.gain.value = 0.9;
        this.sfxBus.connect(this.master);

        const delay = ctx.createDelay(1.0);
        delay.delayTime.value = 0.26;
        const fb = ctx.createGain();
        fb.gain.value = 0.28;
        const wet = ctx.createGain();
        wet.gain.value = 0.2;
        const delayFilter = ctx.createBiquadFilter();
        delayFilter.type = 'lowpass';
        delayFilter.frequency.value = 2200;
        delay.connect(delayFilter);
        delayFilter.connect(fb);
        fb.connect(delay);
        delayFilter.connect(wet);
        wet.connect(this.musicBus);
        this.delay = delay;

        this.noiseBuffer = this._makeNoise(ctx);
        document.addEventListener('visibilitychange', () => {
          if (!this.ctx) return;
          if (document.hidden) {
            this.ctx.suspend();
          } else if (!this.paused) {
            this.ctx.resume();
          }
        });
      } catch (e) {
        this.ctx = null;
      }
    }

    _makeNoise(ctx) {
      const len = Math.floor(ctx.sampleRate * 1.2);
      const buf = ctx.createBuffer(1, len, ctx.sampleRate);
      const data = buf.getChannelData(0);
      for (let i = 0; i < len; i++) data[i] = Math.random() * 2 - 1;
      return buf;
    }

    setMuted(m) {
      this.muted = m;
      if (!this.ctx) return;
      const t = this.ctx.currentTime;
      this.master.gain.cancelScheduledValues(t);
      this.master.gain.setTargetAtTime(m ? 0 : 0.72, t, 0.03);
    }

    setPaused(p) {
      this.paused = p;
      if (!this.ctx) return;
      const t = this.ctx.currentTime;
      this.musicBus.gain.cancelScheduledValues(t);
      this.musicBus.gain.setTargetAtTime(p ? 0.14 : 0.5, t, 0.12);
      if (!p && this.ctx.state === 'suspended') this.ctx.resume();
    }

    startMusic() {
      if (!this.ctx || this.musicOn) return;
      this.musicOn = true;
      this.step = 0;
      this.nextStepTime = this.ctx.currentTime + 0.08;
      if (this.timer) clearInterval(this.timer);
      this.timer = setInterval(() => this._schedule(), 26);
    }

    stopMusic() {
      this.musicOn = false;
      if (this.timer) { clearInterval(this.timer); this.timer = null; }
    }

    setIntensity(n) {
      this.intensity = Math.max(0, Math.min(3, n));
    }

    // waves | boss — boss swaps to a darker progression and a driving bass.
    setMood(m) {
      this.mood = m === 'boss' ? 'boss' : 'waves';
    }

    _schedule() {
      if (!this.ctx || !this.musicOn) return;
      for (let guard = 0; guard < 20; guard++) {
        if (this.nextStepTime > this.ctx.currentTime + 0.16) break;
        this._step(this.step, this.nextStepTime);
        this.nextStepTime += this.stepDur;
        this.step = (this.step + 1) % 64;
      }
    }

    _step(step, time) {
      if (!this.ctx) return;
      const bar = Math.floor(step / 16);
      const s = step % 16;
      const inten = this.intensity;

      // Four-on-the-floor kick
      if (s === 0 || s === 4 || s === 8 || s === 12) this._kick(time, s === 0 ? 0.42 : 0.32);
      if (inten >= 2 && s === 14) this._kick(time, 0.16);
      // Snare / clap
      if (s === 4 || s === 12) this._snare(time, inten >= 1 ? 0.1 : 0.055);
      // Hats
      if (s % 2 === 0) this._hat(time, s % 4 === 2 ? 0.035 : 0.018, false);
      if (inten >= 1 && s % 4 === 3) this._hat(time, 0.012, false);
      if (inten >= 3) this._hat(time, s % 2 ? 0.012 : 0.02, s % 4 === 0);
      if (inten >= 2 && s % 4 === 2) this._hat(time, 0.02, true);

      const boss = this.mood === 'boss';
      const roots = boss ? [40, 36, 43, 38] : [45, 41, 48, 43]; // boss: E2, C2, G2, D2
      const root = roots[bar];
      const chords = boss
        ? [
            [0, 3, 7, 10],
            [0, 3, 7, 10],
            [0, 3, 6, 10],
            [0, 4, 7, 11],
          ]
        : [
            [0, 3, 7, 10],
            [0, 4, 7, 11],
            [0, 4, 7, 11],
            [0, 4, 7, 10],
          ];
      const chord = chords[bar];

      // Bass
      if (s === 0 || s === 6 || s === 8) this._bass(this._midi(root - 12), time, this.stepDur * (s === 6 ? 1.6 : 2.7), 0.09);
      if (inten >= 1 && s === 11) this._bass(this._midi(root), time, this.stepDur * 1.4, 0.05);
      if (inten >= 2 && s === 14) this._bass(this._midi(root - 12), time, this.stepDur * 1.2, 0.06);
      if (inten >= 3 && (s === 3 || s === 7 || s === 13)) this._bass(this._midi(root - 12 + (s === 7 ? 3 : 0)), time, this.stepDur * 1.1, 0.05);

      // Pad at bar start (two detuned voices)
      if (s === 0) {
        const dur = this.stepDur * 16;
        chord.slice(0, 3).forEach((n, i) => {
          this._pad(this._midi(root + n + 12), time, dur, 0.022 + i * 0.002);
        });
      }

      // Arpeggio
      const arpPattern = [0, 1, 2, 3, 2, 1, 3, 2, 1, 2, 3, 1, 0, 2, 1, 3];
      if (inten >= 3 || inten >= 1 || s % 2 === 0) {
        const idx = arpPattern[s];
        const oct = (s >= 8 ? 24 : 12);
        const n = this._midi(root + chord[idx] + oct);
        this._pluck(n, time, (inten >= 2 ? 0.024 : 0.017), this.intensity >= 1);
      }

      // Lead shimmer (sparse, bar 3+)
      if (inten >= 2 && (s === 3 || s === 10 || (inten >= 3 && s === 6))) {
        const n = this._midi(root + chord[2] + 24);
        this._pluck(n, time, 0.014, true);
      }
    }

    _midi(n) { return 440 * Math.pow(2, (n - 69) / 12); }

    _kick(time, vol) {
      const ctx = this.ctx;
      const o = ctx.createOscillator();
      const g = ctx.createGain();
      o.type = 'sine';
      o.frequency.setValueAtTime(140, time);
      o.frequency.exponentialRampToValueAtTime(42, time + 0.12);
      g.gain.setValueAtTime(vol, time);
      g.gain.exponentialRampToValueAtTime(0.001, time + 0.2);
      o.connect(g); g.connect(this.musicBus);
      o.start(time); o.stop(time + 0.22);
    }

    _snare(time, vol) {
      const ctx = this.ctx;
      const src = ctx.createBufferSource();
      src.buffer = this.noiseBuffer;
      const f = ctx.createBiquadFilter();
      f.type = 'highpass';
      f.frequency.value = 1400;
      const g = ctx.createGain();
      g.gain.setValueAtTime(vol, time);
      g.gain.exponentialRampToValueAtTime(0.001, time + 0.13);
      src.connect(f); f.connect(g); g.connect(this.musicBus);
      src.start(time, Math.random() * 0.4, 0.15);
    }

    _hat(time, vol, open) {
      const ctx = this.ctx;
      const src = ctx.createBufferSource();
      src.buffer = this.noiseBuffer;
      const f = ctx.createBiquadFilter();
      f.type = 'highpass';
      f.frequency.value = 7500;
      const g = ctx.createGain();
      const d = open ? 0.13 : 0.045;
      g.gain.setValueAtTime(vol, time);
      g.gain.exponentialRampToValueAtTime(0.001, time + d);
      src.connect(f); f.connect(g); g.connect(this.musicBus);
      src.start(time, Math.random() * 0.5, d);
    }

    _bass(freq, time, dur, vol) {
      const ctx = this.ctx;
      const o = ctx.createOscillator();
      const f = ctx.createBiquadFilter();
      const g = ctx.createGain();
      o.type = 'sawtooth';
      o.frequency.value = freq;
      f.type = 'lowpass';
      f.frequency.setValueAtTime(520, time);
      f.frequency.exponentialRampToValueAtTime(190, time + dur);
      f.Q.value = 5;
      g.gain.setValueAtTime(0.0001, time);
      g.gain.exponentialRampToValueAtTime(vol, time + 0.012);
      g.gain.exponentialRampToValueAtTime(0.0001, time + dur);
      o.connect(f); f.connect(g); g.connect(this.musicBus);
      o.start(time); o.stop(time + dur + 0.02);
    }

    _pad(freq, time, dur, vol) {
      const ctx = this.ctx;
      const g = ctx.createGain();
      const f = ctx.createBiquadFilter();
      f.type = 'lowpass';
      f.frequency.value = 1100;
      g.gain.setValueAtTime(0.0001, time);
      g.gain.linearRampToValueAtTime(vol, time + 0.35);
      g.gain.linearRampToValueAtTime(vol * 0.75, time + dur * 0.8);
      g.gain.exponentialRampToValueAtTime(0.0001, time + dur);
      f.connect(g); g.connect(this.musicBus);
      [0, 4.5, -4.5].forEach((detune, i) => {
        const o = ctx.createOscillator();
        o.type = i === 0 ? 'triangle' : 'sine';
        o.frequency.value = freq;
        o.detune.value = detune;
        const og = ctx.createGain();
        og.gain.value = i === 0 ? 1 : 0.5;
        o.connect(og); og.connect(f);
        o.start(time); o.stop(time + dur + 0.05);
      });
    }

    _pluck(freq, time, vol, echo) {
      const ctx = this.ctx;
      const o = ctx.createOscillator();
      const g = ctx.createGain();
      o.type = 'triangle';
      o.frequency.value = freq;
      g.gain.setValueAtTime(0.0001, time);
      g.gain.exponentialRampToValueAtTime(vol, time + 0.008);
      g.gain.exponentialRampToValueAtTime(0.0001, time + 0.24);
      o.connect(g);
      g.connect(this.musicBus);
      if (echo) g.connect(this.delay);
      o.start(time); o.stop(time + 0.26);
    }

    /* ---- SFX ---- */
    _envGain(vol, time, attack, decay, bus) {
      const g = this.ctx.createGain();
      g.gain.setValueAtTime(0.0001, time);
      g.gain.exponentialRampToValueAtTime(Math.max(0.0002, vol), time + attack);
      g.gain.exponentialRampToValueAtTime(0.0001, time + attack + decay);
      g.connect(bus || this.sfxBus);
      return g;
    }

    _tone(type, f0, f1, time, dur, vol) {
      const ctx = this.ctx;
      const o = ctx.createOscillator();
      o.type = type;
      o.frequency.setValueAtTime(f0, time);
      if (f1 && f1 !== f0) o.frequency.exponentialRampToValueAtTime(Math.max(20, f1), time + dur);
      const g = this._envGain(vol, time, 0.006, dur);
      o.connect(g);
      o.start(time); o.stop(time + dur + 0.03);
    }

    _noise(time, dur, vol, filterType, f0, f1, q) {
      const ctx = this.ctx;
      const src = ctx.createBufferSource();
      src.buffer = this.noiseBuffer;
      src.loop = true;
      const f = ctx.createBiquadFilter();
      f.type = filterType || 'lowpass';
      f.frequency.setValueAtTime(f0, time);
      if (f1) f.frequency.exponentialRampToValueAtTime(Math.max(40, f1), time + dur);
      if (q) f.Q.value = q;
      const g = this._envGain(vol, time, 0.008, dur);
      src.connect(f); f.connect(g);
      src.start(time); src.stop(time + dur + 0.05);
    }

    sfx(name, opt) {
      if (!this.ctx || this.muted) return;
      if (this.ctx.state !== 'running') {
        if (this.ctx.state === 'suspended' && !this.paused) this.ctx.resume();
        return;
      }
      const t = this.ctx.currentTime + 0.001;
      const now = performance.now();
      switch (name) {
        case 'shoot':
          if (now - this.lastShoot < 52) return;
          this.lastShoot = now;
          this._tone('triangle', SF.rand(760, 940), 300, t, 0.055, 0.016);
          break;
        case 'shootHeavy':
          this._tone('sawtooth', 300, 90, t, 0.1, 0.02);
          break;
        case 'hit':
          if (now - this.lastHit < 45) return;
          this.lastHit = now;
          this._noise(t, 0.05, 0.035, 'bandpass', 2600, 1400, 1.2);
          break;
        case 'graze':
          if (now - this.lastGraze < 55) return;
          this.lastGraze = now;
          this._tone('sine', 2100, 2500, t, 0.035, 0.012);
          break;
        case 'explode':
          this._noise(t, 0.32, 0.1, 'lowpass', 1800, 160, 0.8);
          this._tone('sine', 150, 42, t, 0.3, 0.09);
          break;
        case 'bigExplode': {
          this._noise(t, 0.6, 0.2, 'lowpass', 2400, 80, 0.7);
          this._tone('sine', 110, 30, t, 0.55, 0.18);
          this._tone('sawtooth', 90, 28, t + 0.02, 0.5, 0.05);
          break;
        }
        case 'pickup':
          this._tone('sine', 880, 880, t, 0.09, 0.05);
          this._tone('sine', 1320, 1320, t + 0.07, 0.12, 0.04);
          break;
        case 'power':
          [523, 659, 784].forEach((f, i) => this._tone('triangle', f, f, t + i * 0.055, 0.18, 0.045));
          break;
        case 'nova':
          this._tone('sawtooth', 70, 1400, t, 0.55, 0.09);
          this._noise(t, 0.7, 0.16, 'bandpass', 300, 5000, 0.8);
          this._tone('sine', 120, 34, t + 0.05, 0.8, 0.2);
          break;
        case 'dash':
          this._noise(t, 0.22, 0.07, 'bandpass', 500, 3000, 0.9);
          break;
        case 'playerHit':
          this._noise(t, 0.4, 0.14, 'lowpass', 1200, 90, 0.6);
          this._tone('sawtooth', 220, 34, t, 0.5, 0.1);
          break;
        case 'warn':
          [0, 0.28, 0.56].forEach((d) => {
            this._tone('square', 520, 520, t + d, 0.12, 0.035);
            this._tone('square', 392, 392, t + d + 0.14, 0.12, 0.03);
          });
          break;
        case 'clear':
          [392, 523, 659, 784].forEach((f, i) => this._tone('triangle', f, f, t + i * 0.09, 0.4, 0.04));
          break;
        case 'gameover':
          [330, 262, 196, 147].forEach((f, i) => this._tone('sawtooth', f, f * 0.97, t + i * 0.16, 0.5, 0.035));
          break;
        case 'ui':
          this._tone('sine', 660, 880, t, 0.06, 0.025);
          break;
      }
    }
  }
  SF.Audio = new AudioEngine();
})();
