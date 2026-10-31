// The Map tool: a star system from above (Pyro, Stanton or Nyx, by the tabs over the map), everybody playing where his quantum jumps put him, and the
// objectives of the missions going on (as marks on the map). The map itself is SystemMap (systemmap.js, the one map engine of the site, shared with the
// Hauling Planner's Trip Planner and the Companion): this script fetches map.json every few seconds, picks the system to show, and fills the players beside
// the map with their speed meters. The objectives list and the Active Contracts went to the Companion (#380), where they are a player's own.
(function () {
  'use strict';
  let data = null;
  let shown = null, chosen = false;   // the system on the map; chosen: picked by a tab (or the address), not by where the players are
  const map = SystemMap.create(document.getElementById('map'));

  function systemOf(id) { return data.systems.find(function (s) { return s.id === id; }); }
  function nameOfSystem(id) { const s = systemOf(id); return s ? s.name : id; }

  // ------------------------------------------------------------------ the tabs: a system, and how many are playing in it
  function counts(d) {
    const n = {};
    d.systems.forEach(function (s) { n[s.id] = 0; });
    d.players.forEach(function (p) { if (p.system && n[p.system] !== undefined) n[p.system]++; });
    return n;
  }
  function show(id, picked) {
    if (picked) { chosen = true; try { history.replaceState(null, '', '#' + id); } catch (e) { /* only the address */ } }
    shown = id;
    map.show(systemOf(id) || data.systems[0]);
    render();
  }
  function tabs(d) {
    const n = counts(d), box = document.getElementById('systems');
    if (box.children.length !== d.systems.length) {   // made once, so a tab keeps the focus
      box.textContent = '';
      d.systems.forEach(function (s) {
        // a link like the tabs of the other tools (the same row; Frank, 30 Oct 2026): #stanton in the address opens that system too
        const b = document.createElement('a');
        b.href = '#' + s.id;
        b.setAttribute('role', 'tab');
        b.dataset.sys = s.id;
        b.addEventListener('click', function (e) { e.preventDefault(); show(s.id, true); });
        box.appendChild(b);
      });
    }
    d.systems.forEach(function (s, i) {
      const b = box.children[i];
      b.setAttribute('aria-selected', s.id === shown ? 'true' : 'false');
      b.className = s.id === shown ? 'on' : '';
      b.textContent = s.name + (n[s.id] ? ' · ' + n[s.id] : '');
      b.title = n[s.id] ? n[s.id] + (n[s.id] === 1 ? ' player' : ' players') + ' in ' + s.name : 'Nobody in ' + s.name;
    });
  }
  // The system to show: the one picked; else where the most players are (and again when everybody left the one on the map for another).
  function choose(d) {
    if (shown === null) {
      const h = location.hash.replace('#', '').toLowerCase();
      if (d.systems.some(function (s) { return s.id === h; })) { shown = h; chosen = true; }
    }
    if (chosen && d.systems.some(function (s) { return s.id === shown; })) return shown;
    const n = counts(d);
    let best = d.systems[0].id;
    d.systems.forEach(function (s) { if (n[s.id] > n[best]) best = s.id; });
    if (shown === null || !d.systems.some(function (s) { return s.id === shown; }) || (n[shown] === 0 && n[best] > 0)) return best;
    return shown;
  }

  function render() {
    const d = data;
    tabs(d);
    map.draw(d);
    const here = function (sys) { return sys === map.system(); };

    // beside the map: every player (the ones in another system say which; a click shows it) and his speed meter
    const side = document.getElementById('players');
    side.textContent = '';
    if (d.players.length === 0) { const p = document.createElement('p'); p.className = 'muted'; p.textContent = 'Nobody is playing now.'; side.appendChild(p); }
    d.players.forEach(function (pl) {
      const card = document.createElement('div');
      card.className = 'mapplayer';
      card.style.setProperty('--c', pl.color);
      const away = pl.system && !here(pl.system);
      if (away) {
        card.classList.add('away');
        card.tabIndex = 0;
        card.title = 'Show ' + nameOfSystem(pl.system);
        const go = function () { show(pl.system, true); };
        card.addEventListener('click', go);
        card.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); go(); } });
      }
      const who = document.createElement('div'); who.className = 'who'; who.textContent = pl.name;
      if (pl.system) { const s = document.createElement('span'); s.className = 'sys'; s.textContent = nameOfSystem(pl.system); who.appendChild(s); }
      card.appendChild(who);
      const ship = document.createElement('div'); ship.className = 'what'; ship.textContent = [pl.ship, pl.drive].filter(function (x) { return x; }).join(' · '); card.appendChild(ship);
      card.appendChild(map.meter(pl.name));   // what he does, and his speed while he is in quantum
      side.appendChild(card);
    });
  }

  async function refresh() {
    if (document.hidden) return;   // nobody is looking: the map polls again when the tab is shown
    const state = document.getElementById('mapState');
    try {
      const res = await fetch('/map/map.json', { cache: 'no-store' });
      if (!res.ok) throw new Error('HTTP ' + res.status);
      data = await res.json();
      data.clockOffset = Date.parse(data.updated) - Date.now();   // the server's clock against this browser's, when the data came (not when it is drawn)
      show(choose(data), false);
      state.textContent = 'Updated ' + new Date().toLocaleTimeString();
      if (window.siteLive) siteLive(true);   // the top bar's live dot (live.js)
    } catch (e) {
      state.textContent = 'The map could not be updated: trying again.';
      if (window.siteLive) siteLive(false);
    }
  }
  window.addEventListener('hashchange', function () {
    const h = location.hash.replace('#', '').toLowerCase();
    if (data && data.systems.some(function (s) { return s.id === h; }) && h !== shown) show(h, true);
  });
  document.addEventListener('visibilitychange', function () { if (!document.hidden) refresh(); });
  refresh();
  setInterval(refresh, 3000);
})();
