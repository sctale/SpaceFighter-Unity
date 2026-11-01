/* ============================================================
   director.js — wave choreography, sector escalation
   ============================================================ */
(function () {
  'use strict';
  const SF = window.SF;

  const SECTORS = [
    { name: '新曙光', en: 'DAWN DRIFT' },
    { name: '玻璃海', en: 'GLASS SEA' },
    { name: '紫电环', en: 'VIOLET RING' },
    { name: '静默墓园', en: 'SILENT TOMB' },
    { name: '熔金带', en: 'EMBER BELT' },
    { name: '零度之眼', en: 'EYE OF ZERO' },
    { name: '回声廊', en: 'ECHO HALL' },
    { name: '星屑风暴', en: 'STARDUST GALE' },
    { name: '虹吸井', en: 'SIPHON WELL' },
    { name: '无极之岸', en: 'SHORELESS SHORE' },
  ];

  class Director {
    constructor(game) {
      this.game = game;
      this.sector = 1;
      this.state = 'idle';
      this.script = [];
      this.waveIndex = 0;
      this.waveTimer = 0;
      this.clearTimer = 0;
      this.supplyTimer = 0;
      this.pending = [];
      this.spawnedCount = 0;
      this.pityWeapon = 0;
      this.pityDrone = 0;
    }

    static sectorInfo(n) {
      if (n <= SECTORS.length) return SECTORS[n - 1];
      return { name: '深空 ' + SF.pad2(n - SECTORS.length) + ' 区', en: 'DEEP VOID // ' + SF.pad2(n) };
    }

    startSector(sector) {
      this.sector = sector;
      this.state = 'waves';
      this.script = this.buildScript(sector);
      this.waveIndex = 0;
      this.waveTimer = 1.2;
      this.clearTimer = 0;
      this.supplyTimer = 20;
      this.pending = [];
      this.spawnedCount = 0;
      this.midSupply = false;
      if (sector === 1) {
        this.pityWeapon = 0;
        this.pityDrone = 0;
      }
    }

    stop() {
      this.state = 'idle';
      this.pending = [];
    }

    buildScript(s) {
      const list = [];
      const add = (after, fn) => list.push({ after, run: fn });
      const hard = Math.max(0, s - 1);

      add(1.2, () => this.v('drifter', 6 + Math.min(3, hard), 0.13, 54));
      add(2.8, () => {
        this.line('drifter', 4 + Math.min(3, hard), 0.13, 68);
        this.queueSupply('power');
      });
      add(2.6, () => this.waspRush(3 + Math.min(2, hard)));
      add(2.8, () => {
        this.line('drifter', 4, 0.12, 64);
        this.queue('splitter', SF.W / 2, -90, 0.6, {});
      });
      add(2.6, () => {
        if (s >= 2) this.pincer('sidewinder', 2);
        else this.line('drifter', 3, 0.16, 120);
      });
      add(3.0, () => {
        this.line('lancer', 1 + (s >= 3 ? 1 : 0), 0.24, 130);
        this.queue('drifter', SF.W * 0.2, -70, 0.35, {});
        this.queue('drifter', SF.W * 0.8, -70, 0.5, {});
      });
      add(2.6, () => {
        this.queue('orbiter', SF.W * 0.32, -80, 0, {});
        if (s >= 4) this.queue('orbiter', SF.W * 0.68, -90, 0.4, {});
        if (s >= 3) this.queue('minekeeper', SF.W * 0.5, -110, 0.8, {});
      });
      add(2.8, () => this.arc('splitter', 4, 0.1));
      add(2.6, () => {
        this.queue('bulwark', SF.W * 0.5, -100, 0, { elite: s >= 5 });
        for (let i = 0; i < 2; i++) this.queue('rock', SF.W * (0.22 + i * 0.56), -60 - i * 40, 0.3 + i * 0.4, {});
      });
      add(2.8, () => {
        this.waspRush(3);
        this.line('drifter', 3, 0.18, 90);
      });

      if (s >= 2) {
        add(2.8, () => this.sniperNest(2));
        add(3.0, () => {
          const type = ['lancer', 'orbiter', 'splitter', 'sidewinder'][(s - 2) % 4];
          this.queue(type, SF.W * 0.5, -100, 0, { elite: true });
          this.queueSupply(s >= 4 ? (s % 2 ? 'prism' : 'swarm') : 'shield');
        });
      }
      if (s >= 3) {
        add(3.0, () => {
          this.line('lancer', 2, 0.22, 170, { elite: s >= 7 });
          this.v('wasp', 4, 0.1, 52);
        });
        add(2.8, () => this.mineLine(s >= 6 ? 2 : 1));
      }
      if (s >= 4) {
        add(3.2, () => this.carrierGroup());
      }
      if (s >= 5) {
        add(3.2, () => {
          this.line('bulwark', 2, 0.19, 200);
          this.waspRush(4);
          if (s >= 7) this.pincer('sidewinder', 2);
        });
      }

      add(3.0, () => {
        this.line('drifter', 5, 0.1, 58);
        this.queue('orbiter', SF.W * 0.5, -90, 0.9, { elite: s >= 4 });
        for (let i = 0; i < 2; i++) this.queue('splitter', SF.W * (0.3 + i * 0.4), -70, 1.3 + i * 0.3, {});
        if (s >= 3) this.queue('sniper', SF.W * (s % 2 ? 0.18 : 0.82), -70, 1.7, {});
      });
      add(2.6, () => this.queueSupply('drone'));
      return list;
    }

    /* ---- spawn helpers ---- */
    queue(type, x, y, delay, opts) {
      this.pending.push({ kind: 'enemy', type, x, y, t: delay, opts: opts || {} });
    }

    // 第 3 星区起给部分敌机挂词缀；精英机与陨石/分裂幼体不挂，保持可读性
    rollAffix(type, opts) {
      opts = opts || {};
      if (opts.affix || opts.elite || this.sector < 3 || type === 'rock' || type === 'shardling') return opts;
      const p = 0.16 + Math.min(0.14, (this.sector - 3) * 0.02);
      if (!SF.chance(p)) return opts;
      return Object.assign({}, opts, { affix: SF.pick(['armored', 'rapid', 'volatile']) });
    }

    queueSupply(type) {
      this.pending.push({ kind: 'pickup', type, t: 0.2 });
    }

    spawnSupplyNow(type) {
      const game = this.game;
      const pl = game.player;
      const px = SF.clamp((pl ? pl.x : SF.W / 2) + SF.rand(-140, 140), 54, SF.W - 54);
      const py = SF.clamp(pl ? pl.y - 330 : SF.H * 0.4, SF.H * 0.16, SF.H * 0.52);
      game.pickups.push(new SF.Pickup(px, py, type));
      const pal = SF.PALETTE;
      const col = type === 'life' ? pal.rose
        : (type === 'prism' || type === 'swarm' || type === 'reflect') ? pal.gold
        : type === 'shield' ? pal.violet : type === 'swap' ? pal.teal : pal.teal;
      game.fx.ring(px, py, col, 6, 0.55, 58);
      game.fx.spark(px, py, col, 5, 100, 0.35);
    }

    v(type, n, yFrac, spacing, opts) {
      for (let i = 0; i < n; i++) {
        const mid = (n - 1) / 2;
        const x = SF.W / 2 + (i - mid) * spacing;
        const y = -50 - Math.abs(i - mid) * 34;
        this.queue(type, x, y, i * 0.055, opts || {});
      }
    }

    line(type, n, yFrac, spacing, opts) {
      for (let i = 0; i < n; i++) {
        const mid = (n - 1) / 2;
        const x = SF.W / 2 + (i - mid) * spacing;
        this.queue(type, x, -60 - i * 16, i * 0.09, opts || {});
      }
    }

    arc(type, n, yFrac) {
      for (let i = 0; i < n; i++) {
        const t = n === 1 ? 0.5 : i / (n - 1);
        const x = SF.W * (0.18 + t * 0.64);
        this.queue(type, x, -60 - Math.sin(t * Math.PI) * 50, i * 0.12, {});
      }
    }

    waspRush(n) {
      for (let i = 0; i < n; i++) {
        const side = i % 2 === 0 ? 1 : -1;
        this.queue('wasp', SF.W * 0.5 + side * SF.rand(70, 150), -70 - i * 50, i * 0.35, { phase: Math.random() * 6 });
      }
    }

    pincer(type, n) {
      for (let i = 0; i < n; i++) {
        const dir = i % 2 === 0 ? 1 : -1;
        const x = dir > 0 ? -46 : SF.W + 46;
        this.queue(type, x, SF.H * (0.16 + i * 0.07), i * 0.4, { dir, side: dir });
      }
    }

    sniperNest(n) {
      for (let i = 0; i < n; i++) {
        this.queue('sniper', SF.W * (0.26 + i * 0.48), -70 - i * 30, i * 0.45, {});
      }
    }

    mineLine(n) {
      for (let i = 0; i < n; i++) {
        this.queue('minekeeper', SF.W * (n === 1 ? 0.5 : 0.3 + i * 0.4), -90 - i * 30, i * 0.5, {});
      }
    }

    carrierGroup() {
      this.queue('carrier', SF.W * 0.5, -130, 0, {});
      this.queue('drifter', SF.W * 0.2, -60, 0.6, {});
      this.queue('drifter', SF.W * 0.8, -60, 0.8, {});
      if (this.sector >= 6) this.queue('sniper', SF.W * 0.5, -170, 1.4, {});
    }

    collectSupply() {
      const g = this.game;
      const p = g.player;
      const missingWeapon = !!(p && (!p.weapons.prism.unlocked || !p.weapons.swarm.unlocked));
      let type;
      if (p && p.level < 3) type = 'power';
      else if (missingWeapon && (this.pityWeapon >= 3 || SF.chance(0.35))) {
        type = !p.weapons.prism.unlocked ? 'prism'
          : !p.weapons.swarm.unlocked ? 'swarm'
          : SF.pick(['prism', 'swarm']);
      }
      // Sitting on one line for a while? Make the crate that breaks the habit common.
      else if (p && p.unlockedWeapons().length >= 2 && p.swapIdle > 10 && SF.chance(0.62)) type = 'swap';
      else if (p && p.drones.length < 3 && this.pityDrone >= 3) type = 'drone';
      else if (p && p.shieldCharges < 3 && SF.chance(0.4)) type = 'shield';
      else if (p && p.drones.length < 3 && SF.chance(0.35)) type = 'drone';
      else if (p && p.reflectT <= 0 && SF.chance(0.25)) type = 'reflect';
      else if (g.lives <= 2 && SF.chance(0.3)) type = 'life';
      else type = SF.pick(['power', 'power', 'shield', 'swap', 'prism', 'swarm', 'gems']);
      if (type === 'prism' || type === 'swarm') this.pityWeapon = 0;
      else if (missingWeapon) this.pityWeapon++;
      if (type === 'drone') this.pityDrone = 0;
      else if (p) this.pityDrone++;
      if (p && !missingWeapon) this.pityWeapon = 0;
      if (type === 'gems') {
        for (let i = 0; i < 4; i++) {
          g.pickups.push(new SF.Pickup(SF.W * SF.rand(0.42, 0.58), -20 - i * 18, 'gem'));
        }
      } else {
        g.pickups.push(new SF.Pickup(SF.W * SF.rand(0.38, 0.62), -26, type));
      }
    }

    update(dt) {
      const game = this.game;
      if (this.state === 'waves') {
        for (let i = this.pending.length - 1; i >= 0; i--) {
          const p = this.pending[i];
          p.t -= dt;
          if (p.t <= 0) {
            this.pending.splice(i, 1);
            if (p.kind === 'enemy') {
              game.spawnEnemy(p.type, p.x, p.y, this.rollAffix(p.type, p.opts));
              this.spawnedCount++;
            } else if (p.kind === 'pickup') {
              this.spawnSupplyNow(p.type);
            }
          }
        }

        this.waveTimer -= dt;
        if (this.waveTimer <= 0) {
          if (this.waveIndex < this.script.length) {
            const entry = this.script[this.waveIndex];
            this.waveIndex++;
            entry.run();
            this.waveTimer = this.script[this.waveIndex] ? this.script[this.waveIndex].after : 2.4;
            if (this.waveIndex === Math.ceil(this.script.length * 0.55) && !this.midSupply) {
              this.midSupply = true;
              // 中期补给走完整掉落池：换线箱、护盾、僚机等都会按当前状态出现
              this.collectSupply();
            }
          } else {
            this.state = 'clearing';
            this.clearTimer = 0;
          }
        }

        this.supplyTimer -= dt;
        if (this.supplyTimer <= 0) {
          this.supplyTimer = 19;
          // 周期补给也走完整掉落池，"骑同一条线超 10 秒"的换线箱才真正生效
          this.collectSupply();
        }
      } else if (this.state === 'clearing') {
        this.clearTimer += dt;
        for (let i = this.pending.length - 1; i >= 0; i--) {
          const p = this.pending[i];
          p.t -= dt;
          if (p.t <= 0) {
            this.pending.splice(i, 1);
            if (p.kind === 'enemy') game.spawnEnemy(p.type, p.x, p.y, this.rollAffix(p.type, p.opts));
            else this.spawnSupplyNow(p.type);
          }
        }
        const cleared = game.enemies.length === 0;
        if ((cleared && this.clearTimer > 1.0) || this.clearTimer > 9.5) {
          this.state = 'boss';
          game.startBossWarning();
        }
      }
    }

    onBossDefeated() {
      this.state = 'idle';
      this.pending = [];
    }
  }
  SF.Director = Director;
  SF.sectorInfo = Director.sectorInfo;
})();
