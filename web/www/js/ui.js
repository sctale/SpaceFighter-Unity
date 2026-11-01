/* ============================================================
   ui.js — screens, HUD, settings
   ============================================================ */
(function () {
  'use strict';
  const SF = window.SF;

  const WEAPON_NAMES = {
    pulse: ['脉冲炮', '双联脉冲', '三叉星流', '四象阵列', '星瀑'],
    prism: ['棱镜', '双棱', '棱镜矩阵', '裂光', '终焉棱镜'],
    swarm: ['蜂群', '双子蜂', '三蜂', '五蜂', '蜂后'],
  };
  const WEAPON_MODE_NAMES = { pulse: '脉冲', prism: '棱镜', swarm: '蜂群' };

  class UI {
    constructor() {
      this.game = null;
      this.el = {};
      this.ready = false;
      this.bestValue = 0;
      this.bestSectorValue = 1;
      this.cache = { score: -1, best: -1, sector: -1, lives: -1, weapon: -1, weaponMode: '', weaponMod: '', weaponCharge: -1, graze: -1, weaponDetuned: null, chargeLow: null, resonance: -1, shield: -1, progress: -1, nova: -1, dashText: '', ready: null, dash: -1, combo: -1, mult: -1, bossPhase: 0, bossDesperate: false, bossName: '', bossPattern: '', bossBreak: -1, bossBroken: null };
      this.announceTimer = null;
      this.hintTimer = null;
      this.toastTimer = null;
      this.comboPopTimer = null;
    }

    init(game) {
      this.game = game;
      const $ = (id) => document.getElementById(id);
      this.el = {
        stage: $('stage'),
        stageWarn: $('stageWarn'),
        score: $('hudScore'), best: $('hudBest'), graze: $('hudGraze'), sector: $('hudSector'), sectorName: $('hudSectorName'),
        lives: $('hudLives'), weaponChip: $('weaponChip'), weaponName: $('weaponName'), weaponPips: $('weaponPips'),
        weaponRing: $('weaponRing'), resonanceBadge: $('resonanceBadge'),
        shieldChip: $('shieldChip'), shieldPips: $('shieldPips'),
        comboBox: $('comboBox'), comboMult: $('comboMult'), comboCount: $('comboCount'), comboFill: $('comboFill'),
        novaPct: $('novaPct'), dashPct: $('dashPct'),
        progress: $('dashProgress'), progressFill: $('dashProgressFill'),
        bossPanel: $('bossPanel'), bossName: $('bossName'), bossTitle: $('bossTitle'), bossPhase: $('bossPhase'), bossFill: $('bossFill'), bossBreak: $('bossBreak'), bossPattern: $('bossPattern'),
        announce: $('announce'), announceTitle: $('announceTitle'), announceSub: $('announceSub'),
        hint: $('gameHint'), toast: $('toast'),
        menuBest: $('menuBest'), menuSector: $('menuSector'),
        screenMenu: $('screenMenu'), screenPause: $('screenPause'), screenOver: $('screenOver'),
        finalScore: $('finalScore'), finalSector: $('finalSector'), finalKills: $('finalKills'),
        finalGraze: $('finalGraze'), finalNovas: $('finalNovas'), finalPhases: $('finalPhases'), finalBreaks: $('finalBreaks'), finalHits: $('finalHits'), finalMult: $('finalMult'),
        rankRule: $('rankRule'), resultBuild: $('resultBuild'),
        finalChain: $('finalChain'), finalTime: $('finalTime'), newRecord: $('newRecord'), overSub: $('overSub'),
        resultRank: $('resultRank'),
        btnPause: $('btnPause'), btnStart: $('btnStart'), btnRetry: $('btnRetry'), btnMenu: $('btnMenu'),
        btnResume: $('btnResume'), btnRestart: $('btnRestart'), btnQuit: $('btnQuit'),
        btnDash: $('btnDash'), btnNova: $('btnNova'), btnFocus: $('btnFocus'),
        diffPicker: $('diffPicker'), diffNote: $('diffNote'),
        buildSummary: $('buildSummary'),
        toggleSound: $('toggleSound'), toggleShake: $('toggleShake'), toggleFx: $('toggleFx'), toggleHaptic: $('toggleHaptic'),
        hud: $('hud'),
      };

      this.buildWeaponPips();
      this.buildShieldPips();
      this.bind();
      this.applySettings();
      this.updateMenuStats();
      this.setState('menu');
      this.showScreen('menu');
      this.ready = true;
    }

    bind() {
      const g = this.game;
      const click = (el, fn) => {
        if (!el) return;
        el.addEventListener('click', (e) => {
          e.preventDefault();
          SF.Audio.sfx('ui');
          fn(e);
        });
      };
      click(this.el.btnStart, () => g.start());
      click(this.el.btnRetry, () => g.start());
      click(this.el.btnRestart, () => g.start());
      click(this.el.btnResume, () => g.resume());
      click(this.el.btnPause, () => g.pause());
      click(this.el.btnMenu, () => g.toMenu());
      click(this.el.btnQuit, () => g.toMenu());
      click(this.el.btnDash, () => g.tryDash());
      click(this.el.btnNova, () => g.tryNova());
      // hold-to-focus ring mirrors SHIFT on the keyboard
      if (this.el.btnFocus) {
        const fbtn = this.el.btnFocus;
        const setFocus = (on) => { g.input.touchFocus = on; fbtn.classList.toggle('is-held', on); };
        fbtn.addEventListener('pointerdown', (e) => { e.preventDefault(); setFocus(true); });
        for (const ev of ['pointerup', 'pointercancel', 'pointerleave']) fbtn.addEventListener(ev, () => setFocus(false));
      }
      click(this.el.weaponChip, () => {
        if (g.player && g.player.cycleWeapon()) this.toast('武器切换 · ' + (WEAPON_MODE_NAMES[g.player.activeWeapon] || ''));
      });

      click(this.el.toggleSound, () => {
        const next = !g.settings.sound;
        g.setSetting('sound', next);
        this.toast(next ? '音乐与音效已开启' : '音乐与音效已关闭');
      });
      click(this.el.toggleShake, () => {
        const next = !g.settings.shake;
        g.setSetting('shake', next);
        this.toast(next ? '屏幕震动已开启' : '屏幕震动已关闭');
      });
      click(this.el.toggleFx, () => {
        const next = !g.settings.lowFx;
        g.setSetting('lowFx', next);
        this.toast(next ? '已切换轻柔特效' : '已切换标准特效');
      });
      const picker = this.el.diffPicker;
      if (picker) {
        picker.addEventListener('click', (e) => {
          const btn = e.target.closest('button[data-diff]');
          if (!btn) return;
          SF.Audio.sfx('ui');
          g.setSetting('difficulty', btn.dataset.diff);
          this.applySettings();
        });
      }

      click(this.el.toggleHaptic, () => {
        const next = !g.settings.haptic;
        g.setSetting('haptic', next);
        if (next) SF.vibrate(20);
        this.toast(next ? '触感反馈已开启' : '触感反馈已关闭');
      });
      // drop focus after toggling: keeps the panel from focus-scrolling and
      // stops SPACE from re-flipping the switch instead of dashing
      for (const id of ['toggleSound', 'toggleShake', 'toggleFx', 'toggleHaptic']) {
        const el = this.el[id];
        if (el) el.addEventListener('click', () => el.blur());
      }

      // prevent touch-actions from dragging the ship
      ['btnDash', 'btnNova'].forEach((k) => {
        const el = this.el[k];
        if (!el) return;
        el.addEventListener('pointerdown', (e) => e.stopPropagation());
      });
    }

    buildWeaponPips() {
      const wrap = this.el.weaponPips;
      wrap.innerHTML = '';
      for (let i = 0; i < 5; i++) {
        const b = document.createElement('b');
        wrap.appendChild(b);
      }
    }

    buildShieldPips() {
      const wrap = this.el.shieldPips;
      if (!wrap) return;
      wrap.innerHTML = '';
      for (let i = 0; i < 3; i++) {
        const b = document.createElement('b');
        wrap.appendChild(b);
      }
    }

    setState(state) {
      this.el.stage.dataset.state = state;
      const visible = state === 'playing' || state === 'paused';
      this.el.hud.setAttribute('aria-hidden', visible ? 'false' : 'true');
    }

    showScreen(name) {
      const screens = { menu: this.el.screenMenu, pause: this.el.screenPause, over: this.el.screenOver };
      Object.keys(screens).forEach((key) => {
        const el = screens[key];
        const on = key === name;
        el.classList.toggle('is-active', on);
        el.setAttribute('aria-hidden', on ? 'false' : 'true');
      });
      if (name === 'pause') this.renderBuildSummary();
      this.hideToast();
    }

    renderBuildSummary() {
      const p = this.game && this.game.player;
      if (!p || !this.el.buildSummary) return;
      const droneLabels = { gunner: '脉冲', lance: '棱镜', aegis: '守护', swarm: '蜂群' };
      const chips = [];
      // 三条武器线一起展示，暂停时能直接比较蓄能与定位
      const roles = { pulse: '速射', prism: '贯穿', swarm: '追踪' };
      const short = { pulse: '脉冲', prism: '棱镜', swarm: '蜂群' };
      for (const k of ['pulse', 'prism', 'swarm']) {
        const w = p.weapons[k];
        const cls = ['is-weapon', k === p.activeWeapon ? 'is-active' : '', w.unlocked ? '' : 'is-locked'].filter(Boolean).join(' ');
        chips.push(`<span class="${cls}">${short[k]} ${roles[k]} ${w.unlocked ? 'LV.' + w.level : '未解锁'}</span>`);
      }
      if (p.shieldCharges > 0) chips.push(`<span class="is-shield">护盾 ${p.shieldCharges}/3</span>`);
      if (p.reflectT > 0) chips.push(`<span class="is-shield">力场 ${Math.ceil(p.reflectT)}s</span>`);
      p.drones.forEach((d) => chips.push(`<span>僚机 ${droneLabels[d.kind] || d.kind}</span>`));
      this.el.buildSummary.innerHTML = chips.join('');
    }

    updateMenuStats() {
      this.bestValue = SF.Storage.num('best', 0, 0, 99999999);
      this.bestSectorValue = SF.Storage.num('bestSector', 1, 1, 999);
      this.el.menuBest.textContent = this.fmt(this.bestValue);
      this.el.menuSector.textContent = SF.pad2(this.bestSectorValue);
      this.el.best.textContent = this.fmt(this.bestValue);
    }

    fmt(n) {
      return Math.round(n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    }

    resetRun() {
      this.cache = { score: -1, best: -1, sector: -1, lives: -1, weapon: -1, weaponMode: '', weaponMod: '', weaponCharge: -1, graze: -1, weaponDetuned: null, chargeLow: null, resonance: -1, shield: -1, progress: -1, nova: -1, dashText: '', ready: null, dash: -1, combo: -1, mult: -1, bossPhase: 0, bossDesperate: false, bossName: '', bossPattern: '', bossBreak: -1, bossBroken: null };
      this.el.bossPanel.hidden = true;
      this.el.comboBox.classList.remove('is-live');
      this.el.hint.classList.remove('is-show');
      this.el.announce.classList.remove('is-show');
      if (this.announceTimer) clearTimeout(this.announceTimer);
      if (this.hintTimer) clearTimeout(this.hintTimer);
      // the low-hull vignette is a body class maintained during play; returning to
      // the menu with one life left used to keep the red pulse running there
      this.cache.lowHull = false;
      document.body.classList.remove('is-low-hull');
      this.updateMenuStats();
    }

    update(game) {
      const el = this.el;
      const p = game.player;
      const score = game.score;
      if (score !== this.cache.score) {
        el.score.textContent = this.fmt(score);
        this.cache.score = score;
      }
      if (game.grazes !== this.cache.graze) {
        el.graze.textContent = game.grazes;
        this.cache.graze = game.grazes;
      }
      const best = Math.max(this.bestValue, score);
      if (best !== this.cache.best) {
        el.best.textContent = this.fmt(best);
        this.cache.best = best;
      }
      if (game.sector !== this.cache.sector) {
        el.sector.textContent = SF.pad2(game.sector);
        const info = SF.sectorInfo(game.sector);
        el.sectorName.textContent = info.name;
        this.cache.sector = game.sector;
      }
      if (game.lives !== this.cache.lives) {
        this.renderLives(game.lives);
        this.cache.lives = game.lives;
      }
      const lowHull = game.lives === 1;
      if (lowHull !== this.cache.lowHull) {
        document.body.classList.toggle('is-low-hull', lowHull);
        this.cache.lowHull = lowHull;
      }
      if (p && (p.level !== this.cache.weapon || p.activeWeapon !== this.cache.weaponMode)) {
        const names = WEAPON_NAMES[p.activeWeapon] || WEAPON_NAMES.pulse;
        el.weaponName.textContent = names[Math.min(p.level - 1, 4)];
        Array.from(el.weaponPips.children).forEach((b, i) => b.classList.toggle('is-on', i < p.level));
        el.weaponChip.disabled = p.unlockedWeapons().length < 2;
        el.weaponChip.classList.remove('is-prism', 'is-swarm');
        if (p.activeWeapon !== 'pulse') el.weaponChip.classList.add('is-' + p.activeWeapon);
        this.cache.weapon = p.level;
        this.cache.weaponMode = p.activeWeapon;
      }
      if (p) {
        // charge ring + detune state: the weapon tells you when to swap
        const w = p.weapons[p.activeWeapon];
        const charge = Math.round(w.charge);
        if (charge !== this.cache.weaponCharge && el.weaponRing) {
          el.weaponRing.style.setProperty('--pct', charge);
          // While a dry line vents, the label counts the climb so the recharge is
          // something you watch happen rather than guess at.
          if (this.cache.weaponDetuned) el.weaponName.textContent = '失谐排气';
          this.cache.weaponCharge = charge;
        }
        if (w.detuned !== this.cache.weaponDetuned) {
          el.weaponChip.classList.toggle('is-detuned', w.detuned);
          if (w.detuned) {
            el.weaponName.textContent = '失谐排气';
          } else if (this.cache.weaponDetuned !== null) {
            const names = WEAPON_NAMES[p.activeWeapon] || WEAPON_NAMES.pulse;
            el.weaponName.textContent = names[Math.min(p.level - 1, 4)];
            // A full vent is the only re-arm worth flashing for; swapping onto a
            // half-charged line just changes the name.
            if (w.charge >= 99.5) {
              el.weaponChip.classList.remove('is-armed');
              void el.weaponChip.offsetWidth;
              el.weaponChip.classList.add('is-armed');
            }
          }
          // the accessible name should describe the line the pill is showing
          el.weaponChip.setAttribute('aria-label', '切换武器 · ' + el.weaponName.textContent);
          this.cache.weaponDetuned = w.detuned;
        }
        const chargeLow = !w.detuned && w.charge < 40;
        if (chargeLow !== this.cache.chargeLow) {
          el.weaponChip.classList.toggle('is-low', chargeLow);
          this.cache.chargeLow = chargeLow;
        }
        if (p.resonance !== this.cache.resonance && el.resonanceBadge) {
          el.resonanceBadge.classList.toggle('is-on', p.resonance > 0);
          el.resonanceBadge.textContent = '×' + p.resonance;
          el.resonanceBadge.classList.toggle('is-hot', p.resonance >= 3);
          this.cache.resonance = p.resonance;
        }
        if (p.shieldCharges !== this.cache.shield) {
          this.cache.shield = p.shieldCharges;
          Array.from(el.shieldPips.children).forEach((b, i) => b.classList.toggle('is-on', i < p.shieldCharges));
        }
        const novaRounded = Math.round(p.nova);
        const novaLabel = novaRounded >= 100 ? 'READY' : novaRounded + '%';
        if (novaLabel !== this.cache.nova) {
          el.novaPct.textContent = novaLabel;
          el.btnNova.style.setProperty('--pct', Math.round(p.nova));
          this.cache.nova = novaLabel;
        }
        const ready = p.nova >= 100 && p.overdrive <= 0;
        if (ready !== this.cache.ready) {
          el.btnNova.classList.toggle('is-ready', ready);
          this.cache.ready = ready;
        }
        const dashRatio = Math.round(Math.max(0, 1 - p.dashCd / 1.7) * 100);
        // the caption doubles as the cooldown countdown — the one number a dash
        // button needs, and it keeps the ring free to show the same thing
        const dashLabel = p.dashCd > 0.05 ? p.dashCd.toFixed(1) + 's' : '闪避';
        if (dashRatio !== this.cache.dash || dashLabel !== this.cache.dashText) {
          el.btnDash.style.setProperty('--pct', dashRatio);
          el.btnDash.classList.toggle('is-cooling', p.dashCd > 0.05);
          el.dashPct.textContent = dashLabel;
          this.cache.dash = dashRatio;
          this.cache.dashText = dashLabel;
        }
      }

      // stage rail — waves advance it, the boss lights the marker at the end
      if (el.progress) {
        const d = game.director;
        const total = d && d.script && d.script.length ? d.script.length : 1;
        const state = d ? d.state : 'waves';
        // 'idle' (sector intro, the seconds after a clear) holds the rail where it is
        // rather than snapping an earned bar back to empty
        const prog = state === 'waves' ? Math.min(0.9, (d.waveIndex / total) * 0.86)
          : state === 'clearing' ? 0.96
            : state === 'boss' ? 1 : null;
        if (prog !== null) {
          const rounded = Math.round(prog * 100);
          if (rounded !== this.cache.progress) {
            el.progress.style.setProperty('--p', rounded / 100);
            this.cache.progress = rounded;
          }
          el.progress.classList.toggle('is-clearing', state === 'clearing');
          el.progress.classList.toggle('is-boss', state === 'boss');
        }
      }

      if (game.combo > 0) {
        el.comboBox.classList.add('is-live');
        if (game.combo !== this.cache.combo) {
          el.comboCount.textContent = game.combo;
          this.cache.combo = game.combo;
          el.comboBox.classList.add('is-pop');
          if (this.comboPopTimer) clearTimeout(this.comboPopTimer);
          this.comboPopTimer = setTimeout(() => el.comboBox.classList.remove('is-pop'), 130);
        }
        const mult = game.multiplier();
        if (mult !== this.cache.mult) {
          el.comboMult.textContent = '×' + mult;
          this.cache.mult = mult;
        }
        el.comboFill.style.transform = 'scaleX(' + Math.max(0, Math.min(1, game.comboTimer / game.comboMax)) + ')';
      } else {
        if (this.cache.combo !== 0) {
          el.comboBox.classList.remove('is-live');
          this.cache.combo = 0;
          el.comboCount.textContent = '0';
          this.cache.mult = -1;
        }
      }

      // boss bar
      el.comboBox.classList.toggle('is-boss', !!game.boss);
      if (game.boss) {
        const b = game.boss;
        if (el.bossPanel.hidden) el.bossPanel.hidden = false;
        const title = b.title + ' // ' + b.en;
        if (this.cache.bossName !== b.name + b.sector) {
          el.bossName.textContent = b.name;
          el.bossTitle.textContent = title;
          this.cache.bossName = b.name + b.sector;
        }
        const fill = Math.max(0, b.hp / b.maxHp);
        el.bossFill.style.transform = 'scaleX(' + fill + ')';
        const broken = b.broken > 0;
        const breakPct = Math.round((broken ? 1 : Math.min(1, b.stagger / b.staggerMax)) * 100);
        if (el.bossBreak) {
          if (breakPct !== this.cache.bossBreak) {
            el.bossBreak.style.setProperty('--b', breakPct / 100);
            this.cache.bossBreak = breakPct;
          }
          if (broken !== this.cache.bossBroken) {
            el.bossBreak.classList.toggle('is-broken', broken);
            this.cache.bossBroken = broken;
          }
        }
        const phaseTag = 'P' + b.phase + (b.desperate ? '·临界' : '');
        if (b.phase !== this.cache.bossPhase || this.cache.bossDesperate !== !!b.desperate) {
          el.bossPhase.textContent = phaseTag;
          el.bossPhase.classList.toggle('is-critical', !!b.desperate);
          this.cache.bossPhase = b.phase;
          this.cache.bossDesperate = !!b.desperate;
        }
        const pattern = broken ? '核心破防 · 伤害窗口'
          : b.attack ? (b.attackLabel || '') : (b.mercy > 0 ? '核心重载' : '蓄招');
        if (pattern !== this.cache.bossPattern) {
          el.bossPattern.textContent = pattern;
          el.bossPattern.classList.toggle('is-live', !!pattern);
          this.cache.bossPattern = pattern;
        }
      } else if (!el.bossPanel.hidden) {
        el.bossPanel.hidden = true;
        el.bossPattern.textContent = '';
        this.cache.bossPattern = '';
      }
    }

    // The hull row is a fixed five-slot track (the run cap). Slots past the lives
    // you have ever shown are invisible placeholders, so gaining an extra life or
    // losing one only changes which marks are lit — never where they sit.
    renderLives(lives) {
      const MAX_LIVES = 5;
      const wrap = this.el.lives;
      if (wrap.children.length !== MAX_LIVES) {
        wrap.innerHTML = '';
        for (let i = 0; i < MAX_LIVES; i++) wrap.appendChild(Object.assign(document.createElement('span'), { className: 'life' }));
      }
      const shown = Math.max(3, lives);
      const grew = this.lifeShown === undefined ? false : shown > this.lifeShown;
      this.lifeShown = shown;
      Array.from(wrap.children).forEach((el, i) => {
        el.classList.toggle('is-ghost', i >= shown);
        el.classList.toggle('is-empty', i < shown && i >= lives);
        const flash = grew && i === shown - 1;
        if (flash) {
          el.classList.remove('is-new');
          void el.offsetWidth;
        } else {
          el.classList.remove('is-new');
        }
        if (flash) el.classList.add('is-new');
      });
    }

    // A held action ring is cleared from here when the run pauses or restarts,
    // otherwise the button keeps its pressed look with no pointer on it.
    releaseHolds() {
      const el = this.el;
      if (!el || !el.btnFocus) return;
      [el.btnFocus, el.btnDash, el.btnNova].forEach((b) => b.classList.remove('is-held'));
    }

    announce(title, sub, opts) {
      opts = opts || {};
      const el = this.el;
      el.announceTitle.textContent = title;
      el.announceSub.textContent = sub || '';
      el.announce.classList.toggle('is-alert', !!opts.alert);
      el.announce.classList.toggle('is-bottom', !!opts.bottom);
      el.announce.classList.add('is-show');
      el.hint.classList.remove('is-show');
      if (this.announceTimer) clearTimeout(this.announceTimer);
      this.announceTimer = setTimeout(() => {
        el.announce.classList.remove('is-show');
      }, opts.duration || 2400);
    }

    warnPulse(ms) {
      const el = this.el.stageWarn;
      if (!el) return;
      el.classList.add('is-on');
      if (this.warnTimer) clearTimeout(this.warnTimer);
      this.warnTimer = setTimeout(() => el.classList.remove('is-on'), ms || 2600);
    }

    hint(html, duration) {
      const el = this.el;
      el.hint.innerHTML = html;
      el.hint.classList.add('is-show');
      if (this.hintTimer) clearTimeout(this.hintTimer);
      this.hintTimer = setTimeout(() => el.hint.classList.remove('is-show'), duration || 3800);
    }

    hideHint() {
      this.el.hint.classList.remove('is-show');
      if (this.hintTimer) clearTimeout(this.hintTimer);
    }

    toast(msg) {
      const el = this.el.toast;
      el.textContent = msg;
      el.classList.add('is-show');
      if (this.toastTimer) clearTimeout(this.toastTimer);
      this.toastTimer = setTimeout(() => el.classList.remove('is-show'), 1800);
    }

    hideToast() {
      this.el.toast.classList.remove('is-show');
    }

    applySettings() {
      const s = this.game.settings;
      this.el.toggleSound.classList.toggle('is-on', !!s.sound);
      this.el.toggleSound.setAttribute('aria-checked', s.sound ? 'true' : 'false');
      this.el.toggleShake.classList.toggle('is-on', !!s.shake);
      this.el.toggleShake.setAttribute('aria-checked', s.shake ? 'true' : 'false');
      this.el.toggleFx.classList.toggle('is-on', !!s.lowFx);
      this.el.toggleFx.setAttribute('aria-checked', s.lowFx ? 'true' : 'false');
      if (this.el.toggleHaptic) {
        this.el.toggleHaptic.classList.toggle('is-on', s.haptic !== false);
        this.el.toggleHaptic.setAttribute('aria-checked', s.haptic !== false ? 'true' : 'false');
      }
      document.body.classList.toggle('reduce-fx', !!s.lowFx);
      const diff = SF.DIFFICULTY[s.difficulty] || SF.DIFFICULTY.standard;
      if (this.el.diffPicker) {
        this.el.diffPicker.querySelectorAll('button[data-diff]').forEach((b) => {
          const on = b.dataset.diff === diff.id;
          b.classList.toggle('is-on', on);
          b.setAttribute('aria-checked', on ? 'true' : 'false');
        });
        if (this.el.diffNote) this.el.diffNote.textContent = diff.name + ' · ' + diff.note;
      }
    }

    showResult(data) {
      const el = this.el;
      el.finalScore.textContent = this.fmt(data.score);
      el.finalSector.textContent = SF.pad2(data.sector);
      el.finalKills.textContent = this.fmt(data.kills);
      el.finalChain.textContent = data.maxCombo;
      el.finalTime.textContent = SF.fmtTime(data.time);
      if (el.finalGraze) el.finalGraze.textContent = this.fmt(data.grazes || 0);
      if (el.finalNovas) el.finalNovas.textContent = data.novas || 0;
      if (el.finalPhases) el.finalPhases.textContent = data.phases || 0;
      if (el.finalBreaks) el.finalBreaks.textContent = data.breaks || 0;
      if (el.finalHits) el.finalHits.textContent = data.hits || 0;
      if (el.finalMult) el.finalMult.textContent = '×' + (data.maxMult || 1);
      el.newRecord.hidden = !data.newRecord;
      const rank = data.score >= 120000 || data.sector >= 8 ? 'S'
        : data.score >= 60000 || data.sector >= 5 ? 'A'
        : data.score >= 22000 || data.sector >= 3 ? 'B' : 'C';
      const letter = el.resultRank.querySelector('b');
      if (letter) letter.textContent = rank;
      else el.resultRank.textContent = rank;
      el.resultRank.className = 'result-rank rank-' + rank.toLowerCase();
      if (el.rankRule) {
        const next = rank === 'S' ? null
          : rank === 'A' ? Math.max(0, 120000 - data.score)
          : rank === 'B' ? Math.max(0, 60000 - data.score)
          : Math.max(0, 22000 - data.score);
        el.rankRule.textContent = rank === 'S'
          ? 'S 级巡航 · 已达最高评价'
          : '距离上一档还差 ' + this.fmt(next) + ' 分';
      }
      if (el.resultBuild) {
        if (data.build && data.build.weapons) {
          const wn = { pulse: '脉冲', prism: '棱镜', swarm: '蜂群' };
          const dn = { gunner: '脉冲僚机', lance: '棱镜僚机', aegis: '守护僚机', swarm: '蜂群僚机' };
          const chips = data.build.weapons.map((w) => '<span>' + (wn[w.k] || w.k) + ' <b>LV.' + w.lv + '</b></span>');
          (data.build.drones || []).forEach((d) => chips.push('<span>' + (dn[d] || d) + '</span>'));
          el.resultBuild.innerHTML = chips.length ? '<em>本次装配</em>' + chips.join('') : '';
        } else {
          el.resultBuild.textContent = '';
        }
      }
      const flavors = [
        '信号消失在裂隙深处。',
        '深空收回了你的航迹。',
        '战机沉默，星光仍在。',
        '下一次，穿过那道裂隙。',
      ];
      this.el.overSub.textContent = data.newRecord ? '新的巡航纪录已写入星图。' : flavors[data.sector % flavors.length];
      this.updateMenuStats();
    }

    bossWarn() {
      this.el.bossPanel.hidden = true;
    }
  }

  SF.UI = new UI();
})();
