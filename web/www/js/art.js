/* ============================================================
   art.js — procedural vector art & cached glow sprites
   ============================================================ */
(function () {
  'use strict';
  const SF = window.SF;
  const P = SF.PALETTE;
  const Art = SF.Art = { sprites: {}, glows: {} };

  function makeSprite(w, h, draw) {
    const s = 2;
    const c = document.createElement('canvas');
    c.width = Math.ceil(w * s);
    c.height = Math.ceil(h * s);
    const g = c.getContext('2d');
    g.scale(s, s);
    g.translate(w / 2, h / 2);
    draw(g, w, h);
    c._w = w;
    c._h = h;
    return c;
  }
  Art.makeSprite = makeSprite;

  Art.glow = function (color) {
    if (this.glows[color]) return this.glows[color];
    const size = 128;
    const c = document.createElement('canvas');
    c.width = c.height = size;
    const g = c.getContext('2d');
    const grad = g.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    grad.addColorStop(0, color);
    grad.addColorStop(0.26, color);
    grad.addColorStop(1, 'rgba(0,0,0,0)');
    g.globalAlpha = 0.9;
    g.fillStyle = grad;
    g.fillRect(0, 0, size, size);
    this.glows[color] = c;
    return c;
  };

  Art.drawGlow = function (ctx, x, y, r, color, alpha) {
    const g = this.glow(color);
    // 保留调用方已有的透明度（无敌残影、Boss 死亡淡出都依赖外层 globalAlpha）
    const prev = ctx.globalAlpha;
    ctx.globalAlpha = prev * alpha;
    ctx.drawImage(g, x - r, y - r, r * 2, r * 2);
    ctx.globalAlpha = prev;
  };

  /* ---------------- init sprites ---------------- */
  Art.init = function () {
    const S = this.sprites;

    // Player bolt (gold core, teal halo)
    S.bolt = makeSprite(20, 38, (g) => {
      // soft directional tail
      const tail = g.createLinearGradient(0, 2, 0, 19);
      tail.addColorStop(0, 'rgba(105,245,220,.4)');
      tail.addColorStop(0.5, 'rgba(105,245,220,.14)');
      tail.addColorStop(1, 'rgba(105,245,220,0)');
      g.fillStyle = tail;
      g.beginPath();
      g.moveTo(-2.6, 1);
      g.lineTo(2.6, 1);
      g.lineTo(0.8, 18);
      g.lineTo(-0.8, 18);
      g.closePath();
      g.fill();

      // halo
      const halo = g.createRadialGradient(0, -1, 0, 0, -1, 9);
      halo.addColorStop(0, 'rgba(255,236,190,.36)');
      halo.addColorStop(0.5, 'rgba(105,245,220,.18)');
      halo.addColorStop(1, 'rgba(105,245,220,0)');
      g.fillStyle = halo;
      g.fillRect(-10, -12, 20, 26);

      // lance core
      const core = g.createLinearGradient(0, -12, 0, 11);
      core.addColorStop(0, '#ffffff');
      core.addColorStop(0.3, '#fff3c4');
      core.addColorStop(0.62, '#ffd27a');
      core.addColorStop(1, 'rgba(105,245,220,.35)');
      g.fillStyle = core;
      g.beginPath();
      g.moveTo(0, -13);
      g.lineTo(2.3, -4);
      g.lineTo(1.6, 7);
      g.lineTo(0, 11);
      g.lineTo(-1.6, 7);
      g.lineTo(-2.3, -4);
      g.closePath();
      g.fill();
      g.strokeStyle = 'rgba(105,245,220,.55)';
      g.lineWidth = 0.7;
      g.stroke();

      // warm tip
      const tip = g.createRadialGradient(0, -9, 0, 0, -9, 4.5);
      tip.addColorStop(0, 'rgba(255,250,225,1)');
      tip.addColorStop(0.5, 'rgba(255,210,122,.6)');
      tip.addColorStop(1, 'rgba(255,210,122,0)');
      g.fillStyle = tip;
      g.fillRect(-5, -14, 10, 10);
      g.fillStyle = '#ffffff';
      g.beginPath();
      g.moveTo(0, -12.5);
      g.lineTo(1.4, -4);
      g.lineTo(-1.4, -4);
      g.closePath();
      g.fill();
    });

    S.droneBolt = makeSprite(12, 24, (g) => {
      const tail = g.createLinearGradient(0, 2, 0, 12);
      tail.addColorStop(0, 'rgba(105,245,220,.4)');
      tail.addColorStop(1, 'rgba(105,245,220,0)');
      g.fillStyle = tail;
      g.fillRect(-1.2, 2, 2.4, 10);
      const grad = g.createRadialGradient(0, 0, 0, 0, 0, 6);
      grad.addColorStop(0, 'rgba(240,255,255,.9)');
      grad.addColorStop(0.45, 'rgba(105,245,220,.45)');
      grad.addColorStop(1, 'rgba(105,245,220,0)');
      g.fillStyle = grad;
      g.fillRect(-6, -6, 12, 12);
      g.fillStyle = '#ffffff';
      g.beginPath();
      g.moveTo(0, -7);
      g.lineTo(1.4, 0);
      g.lineTo(0, 5);
      g.lineTo(-1.4, 0);
      g.closePath();
      g.fill();
    });

    // Enemy orb
    S.orb = makeSprite(28, 28, (g) => {
      const grad = g.createRadialGradient(0, 0, 0, 0, 0, 14);
      grad.addColorStop(0, 'rgba(255,255,255,1)');
      grad.addColorStop(0.22, 'rgba(255,190,205,1)');
      grad.addColorStop(0.42, 'rgba(255,111,145,.95)');
      grad.addColorStop(0.7, 'rgba(255,111,145,.28)');
      grad.addColorStop(1, 'rgba(255,111,145,0)');
      g.fillStyle = grad;
      g.fillRect(-14, -14, 28, 28);
      // dark rim keeps the orb readable over bright nebulae
      g.strokeStyle = 'rgba(22,6,15,.55)';
      g.lineWidth = 1.6;
      g.beginPath();
      g.arc(0, 0, 8.6, 0, Math.PI * 2);
      g.stroke();
      g.fillStyle = 'rgba(255,255,255,.95)';
      g.beginPath();
      g.arc(0, 0, 2.6, 0, Math.PI * 2);
      g.fill();
    });

    // Enemy shard (elongated)
    S.shard = makeSprite(20, 38, (g) => {
      g.save();
      g.rotate(0);
      const grad = g.createLinearGradient(0, -18, 0, 18);
      grad.addColorStop(0, 'rgba(255,111,145,0)');
      grad.addColorStop(0.35, 'rgba(255,111,145,.5)');
      grad.addColorStop(0.5, 'rgba(255,235,240,1)');
      grad.addColorStop(0.65, 'rgba(255,111,145,.5)');
      grad.addColorStop(1, 'rgba(255,111,145,0)');
      g.fillStyle = grad;
      g.beginPath();
      g.moveTo(0, -19); g.lineTo(3.5, 0); g.lineTo(0, 19); g.lineTo(-3.5, 0);
      g.closePath();
      g.fill();
      const halo = g.createRadialGradient(0, 0, 0, 0, 0, 19);
      halo.addColorStop(0, 'rgba(255,111,145,.35)');
      halo.addColorStop(1, 'rgba(255,111,145,0)');
      g.fillStyle = halo;
      g.fillRect(-10, -19, 20, 38);
      g.restore();
    });

    // Heavy orb (violet)
    S.heavy = makeSprite(52, 52, (g) => {
      const grad = g.createRadialGradient(0, 0, 0, 0, 0, 26);
      grad.addColorStop(0, 'rgba(255,255,255,1)');
      grad.addColorStop(0.18, 'rgba(201,188,255,1)');
      grad.addColorStop(0.4, 'rgba(155,140,255,.85)');
      grad.addColorStop(0.66, 'rgba(155,140,255,.22)');
      grad.addColorStop(1, 'rgba(155,140,255,0)');
      g.fillStyle = grad;
      g.fillRect(-26, -26, 52, 52);
      g.strokeStyle = 'rgba(255,190,205,.9)';
      g.lineWidth = 1.6;
      g.beginPath();
      g.arc(0, 0, 9.5, 0, Math.PI * 2);
      g.stroke();
      g.fillStyle = 'rgba(255,255,255,.95)';
      g.beginPath();
      g.arc(0, 0, 4.2, 0, Math.PI * 2);
      g.fill();
    });

    // Homing seeker
    S.homing = makeSprite(34, 34, (g) => {
      const grad = g.createRadialGradient(0, 0, 0, 0, 0, 17);
      grad.addColorStop(0, 'rgba(255,255,255,1)');
      grad.addColorStop(0.22, 'rgba(255,210,122,1)');
      grad.addColorStop(0.48, 'rgba(255,111,145,.85)');
      grad.addColorStop(1, 'rgba(255,111,145,0)');
      g.fillStyle = grad;
      g.fillRect(-17, -17, 34, 34);
      g.strokeStyle = 'rgba(255,240,220,.95)';
      g.lineWidth = 1.2;
      g.beginPath();
      g.arc(0, 0, 7, 0, Math.PI * 2);
      g.stroke();
      g.fillStyle = '#fff';
      g.beginPath();
      g.arc(0, 0, 2.4, 0, Math.PI * 2);
      g.fill();
    });

    // Sniper needle (fast, thin, teal)
    S.needle = makeSprite(12, 36, (g) => {
      const grad = g.createLinearGradient(0, -17, 0, 17);
      grad.addColorStop(0, 'rgba(105,245,220,0)');
      grad.addColorStop(0.4, 'rgba(105,245,220,.5)');
      grad.addColorStop(0.55, 'rgba(240,255,255,1)');
      grad.addColorStop(0.7, 'rgba(105,245,220,.45)');
      grad.addColorStop(1, 'rgba(105,245,220,0)');
      g.fillStyle = grad;
      g.beginPath();
      g.moveTo(0, -18); g.lineTo(2, 0); g.lineTo(0, 18); g.lineTo(-2, 0);
      g.closePath();
      g.fill();
      g.fillStyle = '#ffffff';
      g.fillRect(-0.6, -8, 1.2, 16);
    });

    // Wave bullet (violet serpent)
    S.wave = makeSprite(28, 28, (g) => {
      const grad = g.createRadialGradient(0, 0, 0, 0, 0, 13);
      grad.addColorStop(0, 'rgba(255,255,255,1)');
      grad.addColorStop(0.25, 'rgba(201,188,255,.95)');
      grad.addColorStop(0.55, 'rgba(155,140,255,.5)');
      grad.addColorStop(1, 'rgba(155,140,255,0)');
      g.fillStyle = grad;
      g.fillRect(-14, -14, 28, 28);
      g.strokeStyle = 'rgba(255,255,255,.8)';
      g.lineWidth = 1.2;
      g.beginPath();
      g.arc(0, 0, 5.2, 0.4, Math.PI * 1.7);
      g.stroke();
      g.fillStyle = '#fff';
      g.beginPath();
      g.arc(-1.6, 1.2, 2, 0, Math.PI * 2);
      g.fill();
    });

    // Mine (gold spiked charge)
    S.mine = makeSprite(38, 38, (g) => {
      const glow = g.createRadialGradient(0, 0, 0, 0, 0, 19);
      glow.addColorStop(0, 'rgba(255,240,190,.9)');
      glow.addColorStop(0.4, 'rgba(255,210,122,.45)');
      glow.addColorStop(1, 'rgba(255,210,122,0)');
      g.fillStyle = glow;
      g.fillRect(-19, -19, 38, 38);
      g.strokeStyle = 'rgba(255,210,122,.95)';
      g.lineWidth = 1.4;
      for (let i = 0; i < 8; i++) {
        const a = (i / 8) * Math.PI * 2;
        g.beginPath();
        g.moveTo(Math.cos(a) * 5, Math.sin(a) * 5);
        g.lineTo(Math.cos(a) * 12.5, Math.sin(a) * 12.5);
        g.stroke();
      }
      g.fillStyle = '#2a1e12';
      g.beginPath();
      g.arc(0, 0, 8, 0, Math.PI * 2);
      g.fill();
      g.strokeStyle = '#ffd27a';
      g.stroke();
      g.fillStyle = '#ff6f91';
      g.beginPath();
      g.arc(0, 0, 3.2, 0, Math.PI * 2);
      g.fill();
    });

    // Plasma splitter (large violet orb)
    S.plasma = makeSprite(56, 56, (g) => {
      const grad = g.createRadialGradient(0, 0, 0, 0, 0, 28);
      grad.addColorStop(0, 'rgba(255,255,255,1)');
      grad.addColorStop(0.16, 'rgba(225,214,255,1)');
      grad.addColorStop(0.34, 'rgba(155,140,255,.9)');
      grad.addColorStop(0.62, 'rgba(155,140,255,.25)');
      grad.addColorStop(1, 'rgba(155,140,255,0)');
      g.fillStyle = grad;
      g.fillRect(-28, -28, 56, 56);
      g.strokeStyle = 'rgba(255,255,255,.55)';
      g.lineWidth = 1.3;
      g.beginPath();
      g.arc(0, 0, 12, 0.2, Math.PI * 1.3);
      g.stroke();
      g.beginPath();
      g.arc(0, 0, 9, Math.PI * 1.25, Math.PI * 2.2);
      g.stroke();
    });

    // Player prism lance
    S.lance = makeSprite(22, 64, (g) => {
      const halo = g.createLinearGradient(0, 22, 0, -30);
      halo.addColorStop(0, 'rgba(255,111,145,0)');
      halo.addColorStop(0.4, 'rgba(155,140,255,.36)');
      halo.addColorStop(0.8, 'rgba(105,245,220,.5)');
      halo.addColorStop(1, 'rgba(255,255,255,.9)');
      g.fillStyle = halo;
      g.fillRect(-6, -32, 12, 54);
      const core = g.createLinearGradient(0, 26, 0, -30);
      core.addColorStop(0, 'rgba(255,111,145,0)');
      core.addColorStop(0.35, 'rgba(201,188,255,.8)');
      core.addColorStop(0.82, 'rgba(105,245,220,.95)');
      core.addColorStop(1, 'rgba(255,255,255,1)');
      g.fillStyle = core;
      g.beginPath();
      g.moveTo(0, -31); g.lineTo(3.4, -10); g.lineTo(2.4, 20); g.lineTo(0, 26);
      g.lineTo(-2.4, 20); g.lineTo(-3.4, -10);
      g.closePath();
      g.fill();
      g.fillStyle = '#ffffff';
      g.beginPath();
      g.moveTo(0, -29); g.lineTo(1.4, -8); g.lineTo(-1.4, -8);
      g.closePath();
      g.fill();
    });

    // Player swarm micro missile
    S.micro = makeSprite(16, 30, (g) => {
      g.fillStyle = 'rgba(255,210,122,.35)';
      g.fillRect(-2.4, 5, 1.6, 10);
      g.fillRect(0.8, 5, 1.6, 10);
      const grad = g.createLinearGradient(0, -12, 0, 12);
      grad.addColorStop(0, '#ffffff');
      grad.addColorStop(0.4, '#ffd27a');
      grad.addColorStop(1, 'rgba(255,111,145,.35)');
      g.fillStyle = grad;
      g.beginPath();
      g.moveTo(0, -12); g.lineTo(3.4, -1); g.lineTo(2, 9); g.lineTo(0, 12);
      g.lineTo(-2, 9); g.lineTo(-3.4, -1);
      g.closePath();
      g.fill();
      g.fillStyle = '#fff';
      g.beginPath();
      g.arc(0, -3, 1.4, 0, Math.PI * 2);
      g.fill();
    });

    // Escort lance drone beam
    S.beam = makeSprite(14, 62, (g) => {
      const grad = g.createLinearGradient(0, 30, 0, -30);
      grad.addColorStop(0, 'rgba(255,111,145,0)');
      grad.addColorStop(0.45, 'rgba(255,111,145,.55)');
      grad.addColorStop(0.85, 'rgba(255,240,245,1)');
      grad.addColorStop(1, 'rgba(255,255,255,1)');
      g.fillStyle = grad;
      g.fillRect(-2.4, -30, 4.8, 60);
      g.fillStyle = 'rgba(255,255,255,.95)';
      g.fillRect(-0.8, -10, 1.6, 18);
    });

    // Spark particle
    S.spark = makeSprite(10, 10, (g) => {
      const grad = g.createRadialGradient(0, 0, 0, 0, 0, 5);
      grad.addColorStop(0, 'rgba(255,255,255,1)');
      grad.addColorStop(0.35, 'rgba(255,255,255,.75)');
      grad.addColorStop(1, 'rgba(255,255,255,0)');
      g.fillStyle = grad;
      g.fillRect(-5, -5, 10, 10);
    });

    // Gem
    S.gem = makeSprite(26, 26, (g) => {
      const grad = g.createRadialGradient(0, 0, 0, 0, 0, 12);
      grad.addColorStop(0, 'rgba(255,236,190,.5)');
      grad.addColorStop(0.5, 'rgba(255,210,122,.18)');
      grad.addColorStop(1, 'rgba(255,210,122,0)');
      g.fillStyle = grad;
      g.fillRect(-13, -13, 26, 26);
      g.fillStyle = '#ffd27a';
      g.strokeStyle = 'rgba(255,246,220,.9)';
      g.lineWidth = 1;
      g.beginPath();
      g.moveTo(0, -7.4); g.lineTo(5.6, 0); g.lineTo(0, 7.4); g.lineTo(-5.6, 0);
      g.closePath();
      g.fill();
      g.stroke();
      g.fillStyle = 'rgba(255,255,255,.95)';
      g.beginPath();
      g.moveTo(0, -4.2); g.lineTo(2.2, 0); g.lineTo(0, 3.4); g.lineTo(-2.2, 0);
      g.closePath();
      g.fill();
    });
  };

  Art.drawSprite = function (ctx, spr, x, y, angle, scale, alpha) {
    const w = spr._w * (scale || 1);
    const h = spr._h * (scale || 1);
    ctx.save();
    if (alpha !== undefined) ctx.globalAlpha = alpha;
    ctx.translate(x, y);
    if (angle) ctx.rotate(angle);
    ctx.drawImage(spr, -w / 2, -h / 2, w, h);
    ctx.restore();
  };

  /* ---------------- Player ship ---------------- */
  Art.drawShip = function (ctx, x, y, opts) {
    opts = opts || {};
    const bank = opts.bank || 0;
    const scale = opts.scale || 1;
    const t = opts.t || 0;
    const thrust = opts.thrust === undefined ? 1 : opts.thrust;
    const alpha = opts.alpha === undefined ? 1 : opts.alpha;
    // The hull is painted by the weapon line currently online, so swapping
    // weapons reads instantly even in the middle of a bullet storm.
    const tint = opts.tint || P.teal;
    const deep = opts.deep || P.tealDeep;

    ctx.save();
    ctx.translate(x, y);
    ctx.scale(scale, scale);
    ctx.globalAlpha = alpha;

    // aura
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    Art.drawGlow(ctx, 0, 4, 46, tint, 0.16 + 0.04 * Math.sin(t * 3));
    ctx.restore();

    ctx.rotate(bank * 0.1);
    ctx.scale(1 - Math.abs(bank) * 0.2, 1);

    // engine flame
    if (thrust > 0) {
      const flick = 0.72 + Math.sin(t * 34) * 0.15 + Math.random() * 0.1;
      ctx.save();
      ctx.globalCompositeOperation = 'lighter';
      for (const ex of [-8.5, 8.5]) {
        const fg = ctx.createLinearGradient(ex, 16, ex, 40 * flick);
        fg.addColorStop(0, 'rgba(255,255,255,.9)');
        fg.addColorStop(0.25, SF.hexA(tint, 0.8));
        fg.addColorStop(0.6, SF.hexA(deep, 0.35));
        fg.addColorStop(1, SF.hexA(deep, 0));
        ctx.fillStyle = fg;
        ctx.beginPath();
        ctx.moveTo(ex - 3.2, 15);
        ctx.quadraticCurveTo(ex - 2.2, 24 + flick * 8, ex, 34 + flick * 12);
        ctx.quadraticCurveTo(ex + 2.2, 24 + flick * 8, ex + 3.2, 15);
        ctx.closePath();
        ctx.fill();
      }
      ctx.restore();
    }

    // wings (dark plates)
    ctx.fillStyle = '#16223f';
    ctx.strokeStyle = SF.hexA(tint, 0.55);
    ctx.lineWidth = 1.1;
    ctx.beginPath();
    ctx.moveTo(0, -24);
    ctx.lineTo(5, -14);
    ctx.lineTo(22, 0);
    ctx.lineTo(30, 7);
    ctx.lineTo(22, 5);
    ctx.lineTo(12, 13);
    ctx.lineTo(9, 21);
    ctx.lineTo(4, 16);
    ctx.lineTo(0, 19);
    ctx.lineTo(-4, 16);
    ctx.lineTo(-9, 21);
    ctx.lineTo(-12, 13);
    ctx.lineTo(-22, 5);
    ctx.lineTo(-30, 7);
    ctx.lineTo(-22, 0);
    ctx.lineTo(-5, -14);
    ctx.closePath();
    ctx.fill();
    ctx.stroke();

    // gold wing accents
    ctx.strokeStyle = 'rgba(255,210,122,.8)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(24, 4); ctx.lineTo(29, 6.5);
    ctx.moveTo(-24, 4); ctx.lineTo(-29, 6.5);
    ctx.stroke();

    // central hull
    const hull = ctx.createLinearGradient(0, -26, 0, 20);
    hull.addColorStop(0, '#f3f8ff');
    hull.addColorStop(0.3, SF.hexA(tint, 0.78));
    hull.addColorStop(0.75, SF.hexA(deep, 0.92));
    hull.addColorStop(1, '#122638');
    ctx.fillStyle = hull;
    ctx.beginPath();
    ctx.moveTo(0, -27);
    ctx.lineTo(6.5, -6);
    ctx.lineTo(8, 10);
    ctx.lineTo(4.5, 18);
    ctx.lineTo(0, 15);
    ctx.lineTo(-4.5, 18);
    ctx.lineTo(-8, 10);
    ctx.lineTo(-6.5, -6);
    ctx.closePath();
    ctx.fill();

    // hull line
    ctx.strokeStyle = 'rgba(255,255,255,.55)';
    ctx.lineWidth = 0.8;
    ctx.beginPath();
    ctx.moveTo(0, -22);
    ctx.lineTo(0, 8);
    ctx.stroke();

    // cockpit
    const ck = ctx.createLinearGradient(0, -12, 0, 1);
    ck.addColorStop(0, '#ffffff');
    ck.addColorStop(0.4, SF.hexA(tint, 0.65));
    ck.addColorStop(1, SF.hexA(deep, 0.9));
    ctx.fillStyle = ck;
    ctx.beginPath();
    ctx.ellipse(0, -7, 3.4, 7.4, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = 'rgba(255,255,255,.9)';
    ctx.beginPath();
    ctx.ellipse(-0.8, -9, 1.2, 2.6, 0, 0, Math.PI * 2);
    ctx.fill();

    // engine nozzles
    ctx.fillStyle = '#0a1424';
    ctx.fillRect(-11, 14, 4, 5);
    ctx.fillRect(7, 14, 4, 5);

    ctx.restore();
  };

  Art.drawDrone = function (ctx, x, y, t, angle, kind, tint) {
    kind = kind || 'gunner';
    // The wingmen paint themselves in the hull's current line colour, so the
    // whole formation reads as one system when the weapon changes.
    const col = tint || (kind === 'lance' ? P.rose : kind === 'aegis' ? P.violet : kind === 'swarm' ? P.gold : P.teal);
    ctx.save();
    ctx.translate(x, y);
    ctx.rotate(angle || 0);
    ctx.globalCompositeOperation = 'lighter';
    Art.drawGlow(ctx, 0, 0, 18, col, 0.38);
    ctx.globalCompositeOperation = 'source-over';

    if (kind === 'lance') {
      ctx.fillStyle = '#2a1524';
      ctx.strokeStyle = col;
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      ctx.moveTo(0, -10);
      ctx.lineTo(3.4, -1);
      ctx.lineTo(8, 3);
      ctx.lineTo(3, 3);
      ctx.lineTo(0, 8);
      ctx.lineTo(-3, 3);
      ctx.lineTo(-8, 3);
      ctx.lineTo(-3.4, -1);
      ctx.closePath();
      ctx.fill();
      ctx.stroke();
      ctx.strokeStyle = '#fff';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(0, -13);
      ctx.lineTo(0, 2);
      ctx.stroke();
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.arc(0, -5, 1.5, 0, Math.PI * 2);
      ctx.fill();
    } else if (kind === 'aegis') {
      ctx.rotate(t * 0.9);
      ctx.fillStyle = '#1c1938';
      ctx.strokeStyle = col;
      ctx.lineWidth = 1.3;
      ctx.beginPath();
      for (let i = 0; i < 6; i++) {
        const a = (i / 6) * Math.PI * 2;
        const px = Math.cos(a) * 7.5;
        const py = Math.sin(a) * 7.5;
        if (i === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
      }
      ctx.closePath();
      ctx.fill();
      ctx.stroke();
      ctx.strokeStyle = 'rgba(255,255,255,.6)';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.arc(0, 0, 4, 0.4, Math.PI * 1.7);
      ctx.stroke();
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.arc(0, 0, 1.8, 0, Math.PI * 2);
      ctx.fill();
    } else if (kind === 'swarm') {
      ctx.fillStyle = '#2b2114';
      ctx.strokeStyle = col;
      ctx.lineWidth = 1;
      for (const dir of [-1, 1]) {
        ctx.beginPath();
        ctx.moveTo(dir * 2, -7);
        ctx.lineTo(dir * 8, 1);
        ctx.lineTo(dir * 5, 6);
        ctx.lineTo(dir * 1.6, 3);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = '#fff';
        ctx.fillRect(dir * 5 - 0.7, 2, 1.4, 1.4);
        ctx.fillStyle = '#2b2114';
      }
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.arc(0, 0, 1.8, 0, Math.PI * 2);
      ctx.fill();
    } else {
      ctx.fillStyle = '#16333a';
      ctx.strokeStyle = col;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(0, -7);
      ctx.lineTo(5, 4);
      ctx.lineTo(0, 2);
      ctx.lineTo(-5, 4);
      ctx.closePath();
      ctx.fill();
      ctx.stroke();
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.arc(0, -1.5, 1.5, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.restore();
  };

  /* ---------------- Enemies ---------------- */
  Art.drawEnemy = function (ctx, e, t) {
    const x = e.x, y = e.y;
    const flash = e.flash > 0;
    const size = e.size;
    const c = flash ? '#ffffff' : e.color;
    const a = flash ? '#ffffff' : e.accent;
    const angle = e.angle || 0;

    ctx.save();
    ctx.translate(x, y);

    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    Art.drawGlow(ctx, 0, 0, size * 1.35, e.glowColor, flash ? 0.5 : 0.28);
    ctx.restore();

    ctx.rotate(angle);

    switch (e.type) {
      case 'drifter': {
        // manta
        ctx.fillStyle = flash ? c : '#33182b';
        ctx.strokeStyle = c;
        ctx.lineWidth = 1.4;
        ctx.beginPath();
        ctx.moveTo(0, -size);
        ctx.lineTo(size * 0.36, -size * 0.2);
        ctx.lineTo(size, size * 0.18);
        ctx.lineTo(size * 0.42, size * 0.42);
        ctx.lineTo(0, size * 0.9);
        ctx.lineTo(-size * 0.42, size * 0.42);
        ctx.lineTo(-size, size * 0.18);
        ctx.lineTo(-size * 0.36, -size * 0.2);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        if (!flash) {
          ctx.fillStyle = 'rgba(255,111,145,.16)';
          ctx.beginPath();
          ctx.moveTo(0, -size * 0.6);
          ctx.lineTo(size * 0.7, size * 0.1);
          ctx.lineTo(size * 0.3, size * 0.25);
          ctx.closePath();
          ctx.fill();
          ctx.beginPath();
          ctx.moveTo(0, -size * 0.6);
          ctx.lineTo(-size * 0.7, size * 0.1);
          ctx.lineTo(-size * 0.3, size * 0.25);
          ctx.closePath();
          ctx.fill();
        }
        ctx.fillStyle = a;
        ctx.beginPath();
        ctx.moveTo(0, -size * 0.5);
        ctx.lineTo(size * 0.18, size * 0.05);
        ctx.lineTo(0, size * 0.3);
        ctx.lineTo(-size * 0.18, size * 0.05);
        ctx.closePath();
        ctx.fill();
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.arc(0, -size * 0.06, 2.2, 0, Math.PI * 2);
        ctx.fill();
        break;
      }
      case 'wasp': {
        ctx.fillStyle = flash ? c : '#2a1229';
        ctx.strokeStyle = c;
        ctx.lineWidth = 1.3;
        ctx.beginPath();
        ctx.moveTo(0, size);
        ctx.lineTo(size * 0.3, size * 0.1);
        ctx.lineTo(size, -size * 0.55);
        ctx.lineTo(size * 0.28, -size * 0.3);
        ctx.lineTo(0, -size * 0.9);
        ctx.lineTo(-size * 0.28, -size * 0.3);
        ctx.lineTo(-size, -size * 0.55);
        ctx.lineTo(-size * 0.3, size * 0.1);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = a;
        ctx.fillRect(-1.1, -size * 0.5, 2.2, size * 0.9);
        break;
      }
      case 'lancer': {
        ctx.fillStyle = flash ? c : '#1b233d';
        ctx.strokeStyle = c;
        ctx.lineWidth = 1.3;
        ctx.beginPath();
        ctx.moveTo(0, size * 0.8);
        ctx.lineTo(size * 0.38, size * 0.4);
        ctx.lineTo(size, size * 0.15);
        ctx.lineTo(size * 0.86, -size * 0.42);
        ctx.lineTo(size * 0.3, -size * 0.5);
        ctx.lineTo(0, -size);
        ctx.lineTo(-size * 0.3, -size * 0.5);
        ctx.lineTo(-size * 0.86, -size * 0.42);
        ctx.lineTo(-size, size * 0.15);
        ctx.lineTo(-size * 0.38, size * 0.4);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.strokeStyle = a;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(size * 0.55, -size * 0.2); ctx.lineTo(size * 0.55, size * 0.05);
        ctx.moveTo(-size * 0.55, -size * 0.2); ctx.lineTo(-size * 0.55, size * 0.05);
        ctx.stroke();
        ctx.fillStyle = a;
        ctx.beginPath();
        ctx.arc(0, size * 0.1, size * 0.2, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.arc(0, size * 0.1, size * 0.09, 0, Math.PI * 2);
        ctx.fill();
        break;
      }
      case 'orbiter': {
        ctx.rotate(t * 0.8);
        ctx.strokeStyle = c;
        ctx.lineWidth = 2.2;
        ctx.beginPath();
        ctx.arc(0, 0, size * 0.85, 0, Math.PI * 2);
        ctx.stroke();
        ctx.strokeStyle = a;
        ctx.lineWidth = 1;
        ctx.setLineDash([size * 0.45, size * 0.5]);
        ctx.beginPath();
        ctx.arc(0, 0, size * 0.62, 0, Math.PI * 2);
        ctx.stroke();
        ctx.setLineDash([]);
        for (let i = 0; i < 3; i++) {
          const ang = (i / 3) * Math.PI * 2;
          ctx.fillStyle = c;
          ctx.beginPath();
          ctx.arc(Math.cos(ang) * size * 0.85, Math.sin(ang) * size * 0.85, 2.8, 0, Math.PI * 2);
          ctx.fill();
        }
        ctx.fillStyle = flash ? '#fff' : '#2a2138';
        ctx.strokeStyle = a;
        ctx.beginPath();
        for (let i = 0; i < 6; i++) {
          const ang = (i / 6) * Math.PI * 2 - Math.PI / 2;
          const px = Math.cos(ang) * size * 0.38;
          const py = Math.sin(ang) * size * 0.38;
          if (i === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
        }
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.arc(0, 0, 3.4, 0, Math.PI * 2);
        ctx.fill();
        break;
      }
      case 'splitter': {
        ctx.fillStyle = flash ? c : '#2b1626';
        ctx.strokeStyle = c;
        ctx.lineWidth = 1.3;
        for (let i = 0; i < 3; i++) {
          const ang = (i / 3) * Math.PI * 2 + Math.PI / 2;
          ctx.beginPath();
          ctx.arc(Math.cos(ang) * size * 0.38, Math.sin(ang) * size * 0.38, size * 0.46, 0, Math.PI * 2);
          ctx.fill();
          ctx.stroke();
        }
        ctx.fillStyle = a;
        ctx.beginPath();
        ctx.arc(0, 0, size * 0.26, 0, Math.PI * 2);
        ctx.fill();
        break;
      }
      case 'bulwark': {
        ctx.fillStyle = flash ? c : '#1a2231';
        ctx.strokeStyle = c;
        ctx.lineWidth = 1.6;
        ctx.beginPath();
        ctx.moveTo(0, size);
        ctx.lineTo(size * 0.72, size * 0.55);
        ctx.lineTo(size, -size * 0.1);
        ctx.lineTo(size * 0.6, -size * 0.72);
        ctx.lineTo(0, -size * 0.9);
        ctx.lineTo(-size * 0.6, -size * 0.72);
        ctx.lineTo(-size, -size * 0.1);
        ctx.lineTo(-size * 0.72, size * 0.55);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.strokeStyle = 'rgba(255,210,122,.55)';
        ctx.lineWidth = 1;
        ctx.strokeRect(-size * 0.42, -size * 0.3, size * 0.84, size * 0.55);
        ctx.fillStyle = a;
        ctx.beginPath();
        ctx.arc(0, -size * 0.02, size * 0.2, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.arc(0, -size * 0.02, size * 0.08, 0, Math.PI * 2);
        ctx.fill();
        for (const sx of [-0.62, 0.62]) {
          ctx.fillStyle = 'rgba(255,255,255,.75)';
          ctx.fillRect(size * sx - 1, size * 0.28, 2, 5);
        }
        break;
      }
      case 'sniper': {
        // long-rail marksman
        ctx.fillStyle = flash ? c : '#0d2529';
        ctx.strokeStyle = c;
        ctx.lineWidth = 1.2;
        ctx.beginPath();
        ctx.moveTo(0, -size);
        ctx.lineTo(size * 0.34, -size * 0.2);
        ctx.lineTo(size * 0.2, size * 0.4);
        ctx.lineTo(size * 0.6, size * 0.62);
        ctx.lineTo(0, size * 0.38);
        ctx.lineTo(-size * 0.6, size * 0.62);
        ctx.lineTo(-size * 0.2, size * 0.4);
        ctx.lineTo(-size * 0.34, -size * 0.2);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        if (!flash) {
          ctx.strokeStyle = 'rgba(105,245,220,.95)';
          ctx.lineWidth = 1.6;
          ctx.beginPath();
          ctx.moveTo(0, size * 0.1);
          ctx.lineTo(0, size * 1.55);
          ctx.stroke();
          ctx.strokeStyle = 'rgba(255,255,255,.55)';
          ctx.lineWidth = 0.8;
          ctx.beginPath();
          ctx.arc(0, -size * 0.32, size * 0.3, 0, Math.PI * 2);
          ctx.stroke();
          ctx.fillStyle = c;
          ctx.beginPath();
          ctx.arc(0, -size * 0.32, size * 0.1, 0, Math.PI * 2);
          ctx.fill();
          ctx.fillStyle = 'rgba(105,245,220,.2)';
          ctx.fillRect(-size * 0.08, -size * 0.2, size * 0.16, size * 0.45);
        }
        break;
      }
      case 'minekeeper': {
        // spiked mine-layer
        ctx.rotate(-t * 0.35);
        ctx.strokeStyle = c;
        ctx.lineWidth = 1.5;
        for (let i = 0; i < 8; i++) {
          const a = (i / 8) * Math.PI * 2;
          ctx.beginPath();
          ctx.moveTo(Math.cos(a) * size * 0.55, Math.sin(a) * size * 0.55);
          ctx.lineTo(Math.cos(a) * size * 1.05, Math.sin(a) * size * 1.05);
          ctx.stroke();
        }
        ctx.fillStyle = flash ? c : '#2b2114';
        ctx.beginPath();
        ctx.arc(0, 0, size * 0.62, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = c;
        ctx.stroke();
        ctx.strokeStyle = a;
        ctx.lineWidth = 1;
        ctx.setLineDash([size * 0.2, size * 0.16]);
        ctx.beginPath();
        ctx.arc(0, 0, size * 0.78, 0, Math.PI * 2);
        ctx.stroke();
        ctx.setLineDash([]);
        ctx.fillStyle = a;
        ctx.beginPath();
        ctx.arc(0, 0, size * 0.22, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.arc(0, 0, size * 0.08, 0, Math.PI * 2);
        ctx.fill();
        break;
      }
      case 'sidewinder': {
        // crescent interceptor, nose points along travel
        ctx.fillStyle = flash ? c : '#211a38';
        ctx.strokeStyle = c;
        ctx.lineWidth = 1.4;
        ctx.beginPath();
        ctx.moveTo(0, -size);
        ctx.quadraticCurveTo(size * 0.2, -size * 0.3, size, size * 0.5);
        ctx.quadraticCurveTo(size * 0.4, size * 0.28, 0, size * 0.62);
        ctx.quadraticCurveTo(-size * 0.4, size * 0.28, -size, size * 0.5);
        ctx.quadraticCurveTo(-size * 0.2, -size * 0.3, 0, -size);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = a;
        ctx.beginPath();
        ctx.moveTo(0, -size * 0.5);
        ctx.lineTo(size * 0.14, size * 0.25);
        ctx.lineTo(0, size * 0.45);
        ctx.lineTo(-size * 0.14, size * 0.25);
        ctx.closePath();
        ctx.fill();
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.arc(0, -size * 0.1, 1.8, 0, Math.PI * 2);
        ctx.fill();
        break;
      }
      case 'carrier': {
        // rectangular hangar ship with two launch bays
        ctx.fillStyle = flash ? c : '#182031';
        ctx.strokeStyle = c;
        ctx.lineWidth = 1.6;
        ctx.beginPath();
        ctx.moveTo(-size, -size * 0.55);
        ctx.lineTo(size * 0.62, -size * 0.55);
        ctx.lineTo(size, -size * 0.15);
        ctx.lineTo(size, size * 0.5);
        ctx.lineTo(size * 0.5, size * 0.72);
        ctx.lineTo(-size * 0.7, size * 0.72);
        ctx.lineTo(-size, size * 0.35);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.strokeStyle = 'rgba(155,140,255,.55)';
        ctx.lineWidth = 1;
        ctx.strokeRect(-size * 0.75, -size * 0.32, size * 0.55, size * 0.5);
        ctx.strokeRect(size * 0.2, -size * 0.32, size * 0.55, size * 0.5);
        ctx.fillStyle = 'rgba(255,210,122,.85)';
        ctx.fillRect(-size * 0.55, size * 0.15, size * 0.22, size * 0.16);
        ctx.fillRect(size * 0.34, size * 0.15, size * 0.22, size * 0.16);
        ctx.fillStyle = a;
        ctx.beginPath();
        ctx.arc(0, -size * 0.38, size * 0.16, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = a;
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(0, -size * 0.55);
        ctx.lineTo(0, -size * 0.9);
        ctx.stroke();
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.arc(0, -size * 0.9, size * 0.06, 0, Math.PI * 2);
        ctx.fill();
        break;
      }
      case 'shardling': {
        ctx.fillStyle = flash ? '#fff' : '#3a1527';
        ctx.strokeStyle = c;
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(0, -size * 1.2);
        ctx.lineTo(size * 0.7, 0);
        ctx.lineTo(0, size * 0.8);
        ctx.lineTo(-size * 0.7, 0);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = a;
        ctx.beginPath();
        ctx.moveTo(0, -size * 0.5);
        ctx.lineTo(size * 0.22, 0);
        ctx.lineTo(0, size * 0.35);
        ctx.lineTo(-size * 0.22, 0);
        ctx.closePath();
        ctx.fill();
        break;
      }
      case 'rock': {
        ctx.rotate(e.spin || 0);
        ctx.fillStyle = flash ? c : '#232739';
        ctx.strokeStyle = flash ? c : 'rgba(143,164,201,.5)';
        ctx.lineWidth = 1.2;
        ctx.beginPath();
        const seed = e.seed || 1;
        for (let i = 0; i < 9; i++) {
          const ang = (i / 9) * Math.PI * 2;
          const rr = size * (0.78 + Math.sin(i * 2.7 + seed) * 0.18);
          const px = Math.cos(ang) * rr;
          const py = Math.sin(ang) * rr;
          if (i === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
        }
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = 'rgba(255,255,255,.12)';
        ctx.beginPath();
        ctx.arc(-size * 0.2, -size * 0.18, size * 0.18, 0, Math.PI * 2);
        ctx.fill();
        break;
      }
    }

    ctx.restore();

    if (e.elite) {
      ctx.save();
      ctx.translate(x, y);
      ctx.rotate(t * 0.8);
      ctx.strokeStyle = 'rgba(255,210,122,.8)';
      ctx.lineWidth = 1;
      ctx.setLineDash([4, 5]);
      ctx.beginPath();
      ctx.arc(0, 0, size * 1.32, 0, Math.PI * 2);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.restore();
    }
  };

  /* ---------------- Bosses ---------------- */
  Art.drawBoss = function (ctx, boss, t) {
    // 持续射击时不做整体白化，只增强命中泛光，避免 Boss 一直看起来是白色
    const flash = false;
    const s = boss.size;
    const time = t;
    ctx.save();
    ctx.translate(boss.x, boss.y);

    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    Art.drawGlow(ctx, 0, 0, s * 1.7, boss.glowColor, flash ? 0.55 : 0.3);
    ctx.restore();

    if (boss.type === 'gatekeeper') {
      // rotating outer segments
      ctx.save();
      ctx.rotate(time * 0.22);
      ctx.strokeStyle = flash ? '#fff' : 'rgba(255,111,145,.55)';
      ctx.lineWidth = 2;
      for (let i = 0; i < 3; i++) {
        ctx.beginPath();
        ctx.arc(0, 0, s * 1.16, i * (Math.PI * 2 / 3), i * (Math.PI * 2 / 3) + 0.7);
        ctx.stroke();
      }
      ctx.restore();

      // side pods
      ctx.fillStyle = flash ? '#fff' : '#1a1f31';
      ctx.strokeStyle = flash ? '#fff' : 'rgba(255,111,145,.75)';
      ctx.lineWidth = 1.4;
      for (const dir of [-1, 1]) {
        ctx.fillStyle = flash ? '#fff' : '#1a1f31';
        ctx.strokeStyle = flash ? '#fff' : 'rgba(255,111,145,.75)';
        ctx.beginPath();
        ctx.moveTo(dir * s * 0.4, -s * 0.34);
        ctx.lineTo(dir * s * 1.02, -s * 0.5);
        ctx.lineTo(dir * s * 1.12, s * 0.05);
        ctx.lineTo(dir * s * 0.78, s * 0.34);
        ctx.lineTo(dir * s * 0.36, s * 0.2);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = flash ? '#fff' : 'rgba(255,111,145,.9)';
        ctx.fillRect(dir * s * 0.96 - 2, -s * 0.3, 4, s * 0.34);
      }

      // main hexagonal fortress
      ctx.fillStyle = flash ? '#fff' : '#222638';
      ctx.strokeStyle = flash ? '#fff' : 'rgba(255,111,145,.9)';
      ctx.lineWidth = 2;
      ctx.beginPath();
      for (let i = 0; i < 6; i++) {
        const ang = (i / 6) * Math.PI * 2 + Math.PI / 6;
        const px = Math.cos(ang) * s * 0.72;
        const py = Math.sin(ang) * s * 0.72;
        if (i === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
      }
      ctx.closePath();
      ctx.fill();
      ctx.stroke();

      ctx.rotate(-time * 0.5);
      ctx.strokeStyle = 'rgba(255,210,122,.5)';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.55, 0, Math.PI * 2);
      ctx.stroke();
      ctx.setLineDash([6, 8]);
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.42, 0, Math.PI * 2);
      ctx.stroke();
      ctx.setLineDash([]);

      // eye
      const eyePulse = 1 + Math.sin(time * 4) * 0.08;
      ctx.fillStyle = flash ? '#fff' : '#ff6f91';
      ctx.beginPath();
      ctx.ellipse(0, 0, s * 0.2 * eyePulse, s * 0.26 * eyePulse, 0, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.08, 0, Math.PI * 2);
      ctx.fill();
    } else if (boss.type === 'weaver') {
      // wing crescents
      ctx.save();
      ctx.rotate(Math.sin(time * 0.8) * 0.06);
      for (const dir of [-1, 1]) {
        ctx.fillStyle = flash ? '#fff' : '#191a35';
        ctx.strokeStyle = flash ? '#fff' : 'rgba(155,140,255,.85)';
        ctx.lineWidth = 1.6;
        ctx.beginPath();
        ctx.moveTo(dir * s * 0.16, -s * 0.6);
        ctx.bezierCurveTo(dir * s * 1.14, -s * 0.92, dir * s * 1.24, s * 0.28, dir * s * 0.34, s * 0.6);
        ctx.bezierCurveTo(dir * s * 0.62, s * 0.18, dir * s * 0.56, -s * 0.2, dir * s * 0.16, -s * 0.6);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();

        ctx.strokeStyle = 'rgba(255,210,122,.65)';
        ctx.lineWidth = 1.2;
        ctx.beginPath();
        ctx.moveTo(dir * s * 0.88, -s * 0.35);
        ctx.quadraticCurveTo(dir * s * 0.98, s * 0.04, dir * s * 0.6, s * 0.36);
        ctx.stroke();
      }
      ctx.restore();

      // orbit shards
      ctx.save();
      ctx.rotate(time * 0.5);
      for (let i = 0; i < 3; i++) {
        const ang = (i / 3) * Math.PI * 2;
        const dist = s * 0.86;
        ctx.save();
        ctx.translate(Math.cos(ang) * dist, Math.sin(ang) * dist);
        ctx.rotate(ang + Math.PI / 2);
        ctx.fillStyle = flash ? '#fff' : '#9b8cff';
        ctx.beginPath();
        ctx.moveTo(0, -9); ctx.lineTo(4.5, 0); ctx.lineTo(0, 9); ctx.lineTo(-4.5, 0);
        ctx.closePath();
        ctx.fill();
        ctx.restore();
      }
      ctx.restore();

      // central spindle
      ctx.fillStyle = flash ? '#fff' : '#20233f';
      ctx.strokeStyle = flash ? '#fff' : 'rgba(201,188,255,.9)';
      ctx.lineWidth = 1.6;
      ctx.beginPath();
      ctx.moveTo(0, -s * 0.68);
      ctx.quadraticCurveTo(s * 0.3, -s * 0.1, 0, s * 0.68);
      ctx.quadraticCurveTo(-s * 0.3, -s * 0.1, 0, -s * 0.68);
      ctx.closePath();
      ctx.fill();
      ctx.stroke();

      const pulse = 1 + Math.sin(time * 3.4) * 0.1;
      ctx.fillStyle = flash ? '#fff' : '#ff6f91';
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.16 * pulse, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.06, 0, Math.PI * 2);
      ctx.fill();
    } else {
      // helios — reactor core
      ctx.save();
      ctx.rotate(time * 0.35);
      for (let i = 0; i < 4; i++) {
        ctx.rotate(Math.PI / 2);
        ctx.fillStyle = flash ? '#fff' : '#1d2236';
        ctx.strokeStyle = flash ? '#fff' : 'rgba(255,210,122,.75)';
        ctx.lineWidth = 1.3;
        ctx.beginPath();
        ctx.moveTo(-s * 0.16, -s * 0.44);
        ctx.lineTo(s * 0.16, -s * 0.44);
        ctx.lineTo(s * 0.24, -s * 0.98);
        ctx.lineTo(0, -s * 1.12);
        ctx.lineTo(-s * 0.24, -s * 0.98);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = flash ? '#fff' : 'rgba(255,111,145,.8)';
        ctx.beginPath();
        ctx.arc(0, -s * 0.8, 3, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();

      // outer rings
      ctx.save();
      ctx.rotate(-time * 0.18);
      ctx.strokeStyle = flash ? '#fff' : 'rgba(155,140,255,.6)';
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.9, 0.2, Math.PI - 0.2);
      ctx.stroke();
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.9, Math.PI + 0.2, Math.PI * 2 - 0.2);
      ctx.stroke();
      ctx.strokeStyle = 'rgba(255,210,122,.4)';
      ctx.setLineDash([3, 9]);
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.72, 0, Math.PI * 2);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.restore();

      // core
      const body = ctx.createRadialGradient(0, -s * 0.05, s * 0.05, 0, 0, s * 0.56);
      body.addColorStop(0, flash ? '#ffffff' : '#fff6dc');
      body.addColorStop(0.25, flash ? '#ffffff' : '#ffd27a');
      body.addColorStop(0.6, flash ? '#ffffff' : '#b06a3a');
      body.addColorStop(1, flash ? '#ffffff' : '#241a26');
      ctx.fillStyle = body;
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.56, 0, Math.PI * 2);
      ctx.fill();
      ctx.strokeStyle = 'rgba(255,210,122,.9)';
      ctx.lineWidth = 2;
      ctx.stroke();

      ctx.save();
      ctx.rotate(time * 0.9);
      ctx.strokeStyle = 'rgba(255,255,255,.7)';
      ctx.lineWidth = 1;
      for (let i = 0; i < 3; i++) {
        ctx.beginPath();
        ctx.arc(0, 0, s * (0.28 + i * 0.07), i * 2.1, i * 2.1 + 1.2);
        ctx.stroke();
      }
      ctx.restore();

      const pulse = 1 + Math.sin(time * 4.5) * 0.09;
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.arc(0, 0, s * 0.13 * pulse, 0, Math.PI * 2);
      ctx.fill();
    }

    // brief additive hit bloom without whiting out the whole hull
    if (boss.flash > 0) {
      ctx.save();
      ctx.globalCompositeOperation = 'lighter';
      Art.drawGlow(ctx, 0, 0, s * 0.95, '#ffffff', Math.min(0.2, boss.flash * 2.2));
      ctx.restore();
    }

    ctx.restore();
  };

  /* ---------------- Pickups ---------------- */
  Art.drawPickup = function (ctx, p, t) {
    const bob = Math.sin(t * 3 + p.seed) * 2;
    ctx.save();
    ctx.translate(p.x, p.y + bob);
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    const col = p.type === 'power' ? P.teal
      : p.type === 'swap' ? P.teal
      : p.type === 'shield' ? P.violet
      : p.type === 'drone' ? P.violet
      : p.type === 'prism' ? P.violet
      : p.type === 'swarm' ? P.gold
      : p.type === 'reflect' ? P.teal
      : P.rose;
    Art.drawGlow(ctx, 0, 0, 30, col, 0.35 + Math.sin(t * 4) * 0.08);
    ctx.restore();

    ctx.rotate(t * 0.7);
    ctx.strokeStyle = p.type === 'power' ? P.gold : p.type === 'prism' ? P.violet : p.type === 'swarm' ? P.gold : p.type === 'reflect' ? P.gold : p.type === 'swap' ? P.teal : col;
    ctx.fillStyle = 'rgba(6,10,22,.85)';
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    ctx.moveTo(0, -13);
    ctx.lineTo(11, -5);
    ctx.lineTo(11, 7);
    ctx.lineTo(0, 14);
    ctx.lineTo(-11, 7);
    ctx.lineTo(-11, -5);
    ctx.closePath();
    ctx.fill();
    ctx.stroke();
    ctx.rotate(-t * 0.7);

    ctx.fillStyle = p.type === 'power' ? P.gold : p.type === 'prism' ? P.violet : p.type === 'swarm' ? P.gold : p.type === 'reflect' ? P.teal : col;
    if (p.type === 'power') {
      ctx.beginPath();
      ctx.moveTo(0, -6); ctx.lineTo(4.5, 0); ctx.lineTo(0, 6); ctx.lineTo(-4.5, 0);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#fff';
      ctx.fillRect(-0.7, -4, 1.4, 8);
    } else if (p.type === 'prism') {
      // three-sided prism: single-target piercing identity
      ctx.beginPath();
      ctx.moveTo(0, -7); ctx.lineTo(6.5, 5); ctx.lineTo(-6.5, 5);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.moveTo(0, -3.5); ctx.lineTo(3, 3); ctx.lineTo(-3, 3);
      ctx.closePath(); ctx.fill();
    } else if (p.type === 'swarm') {
      // four micro-missile seeds
      for (let i = 0; i < 4; i++) {
        const a = (i / 4) * Math.PI * 2 + t * 0.5;
        ctx.beginPath();
        ctx.arc(Math.cos(a) * 4.5, Math.sin(a) * 4.5, 2.1, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.fillStyle = '#fff';
      ctx.beginPath(); ctx.arc(0, 0, 1.6, 0, Math.PI * 2); ctx.fill();
    } else if (p.type === 'reflect') {
      // prism field: concentric rings
      ctx.strokeStyle = P.gold;
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      ctx.arc(0, 0, 5.4, 0, Math.PI * 2);
      ctx.stroke();
      ctx.strokeStyle = P.teal;
      ctx.beginPath();
      ctx.arc(0, 0, 3, 0.5, Math.PI * 1.6);
      ctx.stroke();
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.arc(0, 0, 1.4, 0, Math.PI * 2);
      ctx.fill();
    } else if (p.type === 'shield') {
      ctx.beginPath();
      for (let i = 0; i < 6; i++) {
        const ang = (i / 6) * Math.PI * 2 - Math.PI / 2;
        const px = Math.cos(ang) * 5.5;
        const py = Math.sin(ang) * 5.5;
        if (i === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
      }
      ctx.closePath();
      ctx.fill();
      ctx.fillStyle = '#0b1226';
      ctx.beginPath();
      ctx.arc(0, 0, 2.4, 0, Math.PI * 2);
      ctx.fill();
    } else if (p.type === 'swap') {
      // two opposing chevrons: this crate moves you to the other gun
      ctx.fillStyle = P.teal;
      ctx.beginPath();
      ctx.moveTo(-6.5, -5); ctx.lineTo(-0.5, 0); ctx.lineTo(-6.5, 5); ctx.lineTo(-6.5, 2.2);
      ctx.lineTo(-2.8, 0); ctx.lineTo(-6.5, -2.2);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = P.rose;
      ctx.beginPath();
      ctx.moveTo(6.5, 5); ctx.lineTo(0.5, 0); ctx.lineTo(6.5, -5); ctx.lineTo(6.5, -2.2);
      ctx.lineTo(2.8, 0); ctx.lineTo(6.5, 2.2);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#fff';
      ctx.beginPath(); ctx.arc(0, 0, 1.3, 0, Math.PI * 2); ctx.fill();
    } else if (p.type === 'drone') {
      ctx.beginPath();
      ctx.moveTo(0, -6); ctx.lineTo(6, 4); ctx.lineTo(0, 1.5); ctx.lineTo(-6, 4);
      ctx.closePath(); ctx.fill();
    } else {
      // life heart
      ctx.beginPath();
      ctx.moveTo(0, 5);
      ctx.bezierCurveTo(-8, -1, -4, -7, 0, -3);
      ctx.bezierCurveTo(4, -7, 8, -1, 0, 5);
      ctx.fill();
    }
    ctx.restore();
  };
})();
