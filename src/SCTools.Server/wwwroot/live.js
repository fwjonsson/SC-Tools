// The live dot of the top bar (TopBar.cs), on every page: green "● live" while the site answers, red while it does not. A page that polls data of its own
// (the Hauling Planner, the Map, the Companion) says how its poll went (siteLive); any other page asks the site itself every 15 seconds (/live, nothing in
// it). A tab nobody looks at does not ask; it does when it is shown again.
(function () {
  let told = 0;   // when a page's own poll last said it
  function show(ok) {
    const el = document.getElementById('live');
    if (!el) return;
    el.textContent = ok ? '● live' : '● connection lost, trying again';
    el.className = 'live ' + (ok ? 'ok' : 'bad');
  }
  window.siteLive = function (ok) { told = Date.now(); show(ok); };
  async function ask() {
    if (document.hidden || Date.now() - told < 20000) return;   // the page's own poll says it
    try {
      const res = await fetch('/live', { cache: 'no-store' });
      show(res.ok);
    } catch (e) {
      show(false);
    }
  }
  ask();
  setInterval(ask, 15000);
  document.addEventListener('visibilitychange', function () { if (!document.hidden) ask(); });
})();
