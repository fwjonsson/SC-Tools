// The team status line ("zzoega has spent 12m 04s on this trip", the fun facts of the session), at the right end of a gameplay tool's row of tabs (the
// Hauling Planner, the Companion, the Gauntlet Tracker, the Map: an element with data-ticker; Frank, 31 Oct 2026: under the Route Planner's map before, where the
// page had to make room for it when it came). The row is there from the start, so nothing moves when a line comes. Asked for every 3 seconds while the
// page is shown (/status.json, SiteStatusController); a new line fades in, the whole of it in its tooltip when the row is too narrow for it.
(function () {
  const box = document.querySelector('[data-ticker]');
  if (!box) return;
  let shown = null;
  async function ask() {
    if (document.hidden) return;
    try {
      const res = await fetch('/status.json', { cache: 'no-store' });
      if (!res.ok) return;
      const p = await res.json();
      box.textContent = p.status || '';
      box.title = p.status || '';
      if (p.status && p.key !== shown) {
        box.classList.remove('fade'); void box.offsetWidth;   // the fade starts again on a line that is already there
        box.classList.add('fade');
      }
      shown = p.key;
    } catch (e) {
      // the next ask tries again
    }
  }
  ask();
  setInterval(ask, 3000);
  document.addEventListener('visibilitychange', function () { if (!document.hidden) ask(); });
})();
