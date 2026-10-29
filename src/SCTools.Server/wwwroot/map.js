// The Map tool: a star system from above (Pyro, Stanton or Nyx, by the tabs over the map), everybody playing where his quantum jumps put him, and the
// objectives of the missions going on. The map itself is SystemMap (systemmap.js, the one map engine of the site, shared with the Hauling Planner's Trip
// Planner): this script fetches map.json every few seconds, picks the system to show, and fills the lists beside and under the map (the players with their
// speed meters, the objectives, the missions going on).
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
        const b = document.createElement('button');
        b.type = 'button';
        b.setAttribute('role', 'tab');
        b.dataset.sys = s.id;
        b.addEventListener('click', function () { show(s.id, true); });
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

    // beside the map: every player (the ones in another system say which; a click shows it) and his speed meter, then the objectives
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

    const objs = document.getElementById('objectives');
    objs.textContent = '';
    const list = document.createElement('ul'); list.className = 'objs';
    const lines = {};   // the same objective of a mission at the same place once, with how many
    d.objectives.forEach(function (o) {
      const text = o.title + ' · ' + (o.where || '') + (o.system && !here(o.system) ? ' · ' + nameOfSystem(o.system) : '') + ' (' + (o.who || [o.player]).join(', ') + ')';
      lines[text] = (lines[text] || 0) + 1;
    });
    Object.keys(lines).forEach(function (text) {
      const li = document.createElement('li');
      li.textContent = text + (lines[text] > 1 ? ' ×' + lines[text] : '');
      list.appendChild(li);
    });
    if (d.objectives.length) objs.appendChild(list);
    const note = document.createElement('p'); note.className = 'hint';
    note.textContent = (d.objectives.length === 0 ? 'No objectives going on. ' : '') +
      (d.unplaced > 0 ? (d.unplaced === 1 ? 'One objective is' : d.unplaced + ' objectives are') + ' only in the list: the game does not say where ' +
        (d.unplaced === 1 ? 'it is.' : 'they are.') : '');
    if (note.textContent) objs.appendChild(note);

    // under the map: the missions going on, with who has them and what the Mission Library says about them
    const mis = document.getElementById('missions');
    mis.textContent = '';
    if (!d.missions || d.missions.length === 0) { const p = document.createElement('p'); p.className = 'muted'; p.textContent = 'No missions going on.'; mis.appendChild(p); }
    else {
      const t = document.createElement('table'); t.className = 'missions view';
      const hr = t.createTHead().insertRow();
      [['Mission', ''], ['Who', ''], ['Open', 'right'], ['Faction', ''], ['Category', ''], ['SCU', 'right'], ['Reward', 'right'], ['MG scrip', 'right'], ['Council scrip', 'right'], ['XP', 'right'], ['Legality', '']]
        .forEach(function (h) { const th = document.createElement('th'); th.textContent = h[0]; if (h[1]) th.className = h[1]; hr.appendChild(th); });
      const tb = t.createTBody();
      d.missions.forEach(function (m) {
        const tr = tb.insertRow();
        [m.title, m.who.join(', '), String(m.open), m.faction, m.category, m.scu, m.reward, m.mg, m.council, m.xp, m.legality].forEach(function (v, i) {
          const td = tr.insertCell(); td.textContent = v;
          if (i === 0) td.className = 'name'; else if (i === 2 || (i >= 5 && i <= 9)) td.className = 'right';
        });
      });
      const box = document.createElement('div'); box.className = 'scroll'; box.appendChild(t); mis.appendChild(box);
    }
  }

  async function refresh() {
    if (document.hidden) return;   // nobody is looking: the map polls again when the tab is shown
    const state = document.getElementById('mapState');
    try {
      const res = await fetch('/map/map.json', { cache: 'no-store' });
      if (!res.ok) throw new Error('HTTP ' + res.status);
      data = await res.json();
      show(choose(data), false);
      state.textContent = 'Updated ' + new Date().toLocaleTimeString();
    } catch (e) {
      state.textContent = 'The map could not be updated: trying again.';
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
