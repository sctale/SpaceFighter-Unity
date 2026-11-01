/* ============================================================
   boot.js — application entry
   ============================================================ */
(function () {
  'use strict';
  const SF = window.SF;

  function boot() {
    SF.Art.init();
    SF.Game = new SF.GameClass();
    SF.Game.init();
    SF.UI.init(SF.Game);
    SF.Game.uiReady = true;
    SF.Game.startLoop();

    if (window.matchMedia && window.matchMedia('(pointer: coarse)').matches) {
      document.body.classList.add('is-touch');
    }

    const unlock = () => { SF.Audio.init(); };
    window.addEventListener('pointerdown', unlock, { capture: true, once: true });
    window.addEventListener('keydown', unlock, { capture: true, once: true });
    document.addEventListener('gesturestart', (e) => e.preventDefault());
    document.addEventListener('dblclick', (e) => e.preventDefault(), { passive: false });

    window.__SF_READY = true;
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
