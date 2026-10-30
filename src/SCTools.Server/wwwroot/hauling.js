var last = null;
var shownStatus = -1;   // the last status line text that was faded in

// who is looking (a player logged in by his tracker, or the admin): the page says (data-me on .hauling); empty for everybody else
const PAGE = document.querySelector('.hauling') || { dataset: {} };
const ME = (PAGE.dataset.me || '').toLowerCase();
// the admin: he picks whose session the tabs show (everybody else sees his own party's)
const ADMIN = !!PAGE.dataset.admin;
// what was picked last (the party tab, what the Trip Planner shows), kept in this browser only
function remembered(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }
function remember(key, value) { try { localStorage.setItem(key, value); } catch (e) { } }
function among(names, name) { return names.some(function (n) { return n.toLowerCase() === name.toLowerCase(); }); }
function myParty() { return ME ? (last.parties || []).find(function (p) { return among(p.names, ME); }) : null; }
function tabButtons(el, choices, on, pick) {
  el.textContent = '';
  choices.forEach(function (c) {
    const b = document.createElement('button'); b.textContent = c.text; if (c.key === on) b.className = 'on';
    b.onclick = function () { pick(c.key); };
    el.appendChild(b);
  });
}

function table(el, g) {
  el.textContent = '';
  if (!g || !g.headers || !g.rows) {
    const d = document.createElement('div'); d.className = 'empty'; d.textContent = 'Nothing yet.'; el.appendChild(d); return;
  }
  const t = document.createElement('table');   // an empty table still shows its header, like in the program
  const hr = t.createTHead().insertRow();
  g.headers.forEach(function (h) { const th = document.createElement('th'); th.textContent = h.text; hr.appendChild(th); });
  const tb = t.createTBody();
  g.rows.forEach(function (r) {
    const tr = tb.insertRow();
    if (r.bold) tr.style.fontWeight = '700';
    r.cells.forEach(function (v, i) {
      const td = tr.insertCell();
      td.textContent = v;
      td.style.textAlign = g.headers[i].align;
      if (r.back[i]) td.style.background = r.back[i];
      if (r.fore[i]) td.style.color = r.fore[i];
    });
  });
  el.appendChild(t);
}

// Clocks that run (the session time and the run time of runs on the way) count up on the page every second from when they started (data-since, UTC in
// the data), by the browser's clock, like the Session tool's clock. The text that comes with the data is only the first value: it was made when the data
// was made, which can be a while before the page fetched it, so counting on from it made the clocks lag and jump (Frank, 30 Oct 2026).
function fmtHms(n) { const h = Math.floor(n / 3600), m = Math.floor(n % 3600 / 60), s = n % 60; return h + ':' + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0'); }
function addTicker(td, since) { if (since) td.dataset.since = since; }
setInterval(function () {
  if (document.hidden) return;
  document.querySelectorAll('[data-since]').forEach(function (td) { const s = Math.floor((Date.now() - Date.parse(td.dataset.since)) / 1000); if (s >= 0) td.textContent = fmtHms(s); });
}, 1000);

// The first columns of Stops, Finished stops and Cargo runs are as wide as the same columns of the Session leaderboard, so they line up (like in the program).
// Since the leaderboard has a tab of its own (Frank, 30 Oct 2026) it can only be measured when that tab is shown; on the Session tab its tables get the
// least widths below, which line them up with each other.
function alignColumns() {
  // the lined-up columns are never narrower than Stops needs them: with nobody on the leaderboard yet its columns are only as wide as their titles,
  // so Pickup Location wrapped and the rest of Stops moved (#, Pickup Location, Batches, Cargo, Value; the last column, Drop-off, takes the rest)
  const least = [36, 140, 80, 110, 110];
  const lead = document.querySelector('#leaders table');
  let w = least;
  if (lead && lead.tHead && lead.offsetParent) w = leaderWidths(lead, least);   // the leaderboard is on screen: its columns as they come out
  // after the lined-up columns: the column with the long text (wide) takes what is left, the others are as wide as their numbers need
  const narrow = { 'Batches': 70, 'Value': 100, 'Started': 76, 'Completed': 90, 'Time': 80, 'In flight': 76, 'aUEC/h': 96, 'Lost Cargo': 96 };
  function fix(id, n, wide) {
    const t = document.querySelector('#' + id + ' table');
    if (!t || !t.tHead) return;
    const cells = t.tHead.rows[0].cells;
    t.style.tableLayout = 'fixed';
    for (let i = 0; i < cells.length; i++) {
      const h = cells[i].textContent;
      if (i < n && i < cells.length - 1 && w[i]) cells[i].style.width = w[i] + 'px';
      else if (wide && h !== wide && narrow[h]) cells[i].style.width = narrow[h] + 'px';
      else cells[i].style.width = '';   // the last column (or the wide one) takes what is left
    }
  }
  fix('pickups', 5); fix('finished', 5); fix('runs', 2, 'Pickup Location & Time');
}

// The widths of the leaderboard's columns, once it is laid out: every column as wide as its title and its numbers need, never less than least.
function leaderWidths(lead, least) {
  const cells = Array.prototype.slice.call(lead.tHead.rows[0].cells);
  // every column of the leaderboard as wide as its title (on one line) and its numbers need, then what is left of the width is shared evenly by all
  // but #, so the columns are balanced (before, the first six had a set width and the others got all the room, however short their titles)
  function measure() {
    lead.style.tableLayout = 'auto'; lead.style.width = 'auto';
    cells.forEach(function (c) { c.style.width = ''; });
    return cells.map(function (c, i) { return Math.max(Math.ceil(c.getBoundingClientRect().width) + 1, least[i] || 0); });   // + 1: rounding
  }
  lead.classList.remove('tight');
  const need = measure();
  const total = need.reduce(function (x, y) { return x + y; }, 0);
  const room = lead.parentElement.clientWidth - 2 - total;   // - 2: the outer borders
  let w;
  if (room >= 0) {
    // the widths as shares of the table's width (100%), so together they never come out a pixel wider than the page and make it scroll sideways
    const share = need.map(function (n, i) { return i > 0 ? n + room / (cells.length - 1) : n; });
    const sum = share.reduce(function (x, y) { return x + y; }, 0);
    lead.style.width = ''; lead.style.tableLayout = 'fixed';
    cells.forEach(function (c, i) { c.style.width = (share[i] / sum * 100).toFixed(4) + '%'; });
    w = cells.map(function (c) { return c.getBoundingClientRect().width; });
  } else {
    // a narrow window, where the titles on one line do not fit: as before, they may take two lines (.tight), with the first six columns as wide as Stops needs
    lead.classList.add('tight'); lead.style.width = ''; lead.style.tableLayout = '';
    cells.forEach(function (c, i) { c.style.width = i < least.length ? least[i] + 'px' : ''; });
    w = cells.map(function (c) { return c.getBoundingClientRect().width; });
  }
  return w;
}

// Every title on one line: a column of a table with set widths that is too narrow for its title (another font, a smaller window) is made as wide as the
// title needs, instead of cutting it.
function fitHeaders() {
  document.querySelectorAll('table').forEach(function (t) {
    // (the leaderboard is left alone: alignColumns made every column as wide as its title)
    if (!t.tHead || !t.offsetParent || t.closest('#leaders') || getComputedStyle(t).tableLayout !== 'fixed') return;
    Array.prototype.forEach.call(t.tHead.rows[0].cells, function (th) {
      const over = th.scrollWidth - th.clientWidth;
      if (over > 0) th.style.width = Math.ceil(th.getBoundingClientRect().width + over + 1) + 'px';
    });
  });
}
function layout() { alignColumns(); fitHeaders(); }
window.addEventListener('resize', layout);

function summary(el, items) {
  el.textContent = '';
  const t = document.createElement('table'); t.className = 'kv';
  const tb = t.createTBody();
  items.forEach(function (it) {
    const tr = tb.insertRow();
    tr.insertCell().textContent = it.k;
    const td = tr.insertCell(); td.textContent = it.v; td.style.textAlign = 'right';
    if (it.live) addTicker(td, it.since);
  });
  el.appendChild(t);
}

function section(parent, tag, title, g) {
  const h = document.createElement(tag); h.textContent = title; parent.appendChild(h);
  const box = document.createElement('div'); box.className = 'scroll'; parent.appendChild(box);
  table(box, g);
  return box;
}

// green when somebody owes the member (+), red when he owes them (-), like in the program
function signColors(box, col, d, rows) {
  // the sign is read from the second column (balance) of every row, the colour goes to column col
  (rows || box.querySelectorAll('tbody tr')).forEach(function (tr) {
    const td = tr.cells[col];
    const sign = (rows ? tr.cells[1] : td);
    if (!td || !sign) return;
    if (sign.textContent.charAt(0) === '+') td.style.background = d.pos;
    else if (sign.textContent.charAt(0) === '-') td.style.background = d.neg;
  });
}

// ------------------------------------------------------------------ the map of the Trip Planner
// The map is the Map tool's (SystemMap, systemmap.js: Pyro from above, everybody playing where his quantum jumps put him, moving along his route while he is
// in quantum, and the objectives of the missions going on as diamonds), with the planner's routes drawn on it: every worker's planned trips from Ruin through
// his stops and back, bold for the trip he flies, dotted for the queued ones, arrows for the direction, and slow dark waves on the leg he flies now. The
// players and the objectives are the site's /hauling/map.json (the Map tool's Pyro), every 3 seconds while the tab is shown; the program's own page has no
// such data and shows the routes alone. Under every worker's name, the map's line for him (where he is, what he does) and his speed meter while he is in quantum.
const NS = 'http://www.w3.org/2000/svg';
const K = SystemMap.W / 520;   // the map is drawn in 760 px and shown in about 520: the lines are a little thicker so they look as they did
// The waves on the map move with the clock of the page, not with an SVG animation (that would start again every time the map is drawn anew).
const WAVE_LEN = 28 * K, WAVE_SPEED = 20 * K;   // px from one dark band to the next, px per second
function slideWave(w, now) {
  const shift = (now / 1000 * WAVE_SPEED) % WAVE_LEN;
  w.el.setAttribute('gradientTransform', 'translate(' + (w.ux * shift).toFixed(2) + ' ' + (w.uy * shift).toFixed(2) + ')');
}
function moveWaves(now) {
  const pm = planMaps[shownTab];   // the waves of the planner tab shown (each planner tab has a map of its own)
  if (pm && pm.waves.length && !document.hidden) pm.waves.forEach(function (w) { slideWave(w, now); });
  requestAnimationFrame(moveWaves);
}
requestAnimationFrame(moveWaves);

let mapData = null, mapOff = false;   // the Map tool's Pyro (its players and objectives) fetched last; off: this page has no map.json (the program's own)
const planMaps = {};   // the map of the planner tab (P: the Route Planner): its engine, how to draw the routes again, whose players it shows, its waves
function systemIn(d, id) { return d && (d.systems || []).find(function (s) { return s.id === id; }); }   // a system of the map's data (map.json has every system since stage 2)

function darker(hex, f) {
  const m = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || '');
  if (!m) return '#000000';
  return '#' + [1, 2, 3].map(function (k) { return Math.round(parseInt(m[k], 16) * f).toString(16).padStart(2, '0'); }).join('');
}

// The map in its box: the legend (who flies which route) above it, a row of the systems the routes touch when there is more than one (stage 2: a route
// through a gateway is drawn on both systems' tabs, each part to or from the gateway), then the Map tool's system with the routes on it. names: the workers
// shown (their players and objectives are drawn, nobody else's). tab: the planner tab it is on (its map is live while that tab is shown).
function mapSvg(el, p, routes, names, tab) {
  el.textContent = '';
  const waves = [];
  const places = p.places || [];
  const systems = (p.maps || []).filter(function (s) { return places.some(function (pl) { return pl.system === s.id; }); });
  const prev = planMaps[tab];
  let shownSys = prev && prev.system && systems.some(function (s) { return s.id === prev.system; }) ? prev.system
    : systems.length === 0 || systems.some(function (s) { return s.id === 'pyro'; }) ? 'pyro' : systems[0].id;   // the system looked at stays; Pyro first
  function sysData(id) { return systemIn(mapData, id) || (p.maps || []).find(function (s) { return s.id === id; }); }
  const legend = routes.filter(function (r) { return r.label; });
  if (legend.length) {
    const lg = document.createElement('div'); lg.className = 'maplegend';
    legend.forEach(function (r) {
      const item = document.createElement('span');
      const sw = document.createElementNS(NS, 'svg'); sw.setAttribute('width', '18'); sw.setAttribute('height', '8'); sw.setAttribute('aria-hidden', 'true');
      const ln = document.createElementNS(NS, 'line');
      ln.setAttribute('x1', '0'); ln.setAttribute('y1', '4'); ln.setAttribute('x2', '18'); ln.setAttribute('y2', '4'); ln.setAttribute('stroke', r.color); ln.setAttribute('stroke-width', '3');
      if (r.dashed) { ln.setAttribute('stroke-dasharray', '1 5'); ln.setAttribute('stroke-linecap', 'round'); }
      sw.appendChild(ln);
      item.appendChild(sw); item.appendChild(document.createTextNode(r.label));
      lg.appendChild(item);
    });
    el.appendChild(lg);
  }
  let row = null;   // the systems' row, like the Map tool's (Frank's answer 8 of 30 Oct 2026), only when the routes are in more than one
  if (systems.length > 1) { row = document.createElement('div'); row.className = 'sub systems'; el.appendChild(row); }
  const box = document.createElement('div'); box.className = 'mapsvg'; el.appendChild(box);
  const map = SystemMap.create(box, { active: function () { return shownTab === tab; } });
  const keep = function (name) { return among(names, name || ''); };
  const sysOf = {}, nameOf = {};
  places.forEach(function (pl) { sysOf[pl.id] = pl.system; nameOf[pl.id] = pl.name; });

  // the routes, in the engine's own layer (between the system and the objectives), at the places of the planner's stations on it
  function drawRoutes() {
    const g = map.layer();
    if (!g) return;
    g.textContent = '';
    waves.length = 0;
    const pos = {};
    places.forEach(function (pl) { if (pl.system === shownSys) pos[pl.id] = map.at(pl.key); });   // the places on this system's map
    const usedGates = {};
    function gateTo(other) { const key = shownSys + '/' + other + '-gateway'; usedGates[key] = true; return map.at(key); }
    // the two ends of a leg on this map: both places here, or the gateway that leads to (or comes from) the other system, with the far end's name on the way out
    function legEnds(x, y) {
      const a = pos[x], b = pos[y], sa = sysOf[x], sb = sysOf[y];
      if (a && b) return { a: a, b: b, label: null };
      if (a && !b && sb && sb !== shownSys) { const gb = gateTo(sb); return gb ? { a: a, b: gb, label: '\u21e2 ' + (nameOf[y] || y) } : null; }
      if (!a && b && sa && sa !== shownSys) { const ga = gateTo(sa); return ga ? { a: ga, b: b, label: null } : null; }
      return null;
    }
    function add(tag, attrs) { return map.el(tag, attrs, g); }
    // Darker waves that run along one leg of a solid route (from where he is to his next stop), in the direction of the arrow.
    function wave(a, b, ux, uy, len, color, width) {
      const ALPHA = 215 / 255, INSET = 8 * K;   // how dark the middle of a band is, and clear of the stations at both ends of the leg
      if (len <= 2 * INSET + 2) return;   // a leg that short is hardly more than its two stations
      const id = 'wave' + tab + waves.length, shade = darker(color, 0.18);   // the tab in the id: the two planner tabs' maps are in the same document
      // one wave goes from a to a + WAVE_LEN along the leg and repeats all along it; moveWaves() slides the pattern on towards the end of the leg
      const grad = add('linearGradient', { id: id, gradientUnits: 'userSpaceOnUse', x1: a[0], y1: a[1], x2: a[0] + ux * WAVE_LEN, y2: a[1] + uy * WAVE_LEN, spreadMethod: 'repeat' });
      for (let k = 0; k <= 8; k++) {
        const t = k / 8;
        const stop = document.createElementNS(NS, 'stop');
        stop.setAttribute('offset', t);
        stop.setAttribute('stop-color', shade);
        stop.setAttribute('stop-opacity', (ALPHA * (1 - Math.cos(2 * Math.PI * t)) / 2).toFixed(3));   // clear, dark in the middle, clear again
        grad.appendChild(stop);
      }
      waves.push({ el: grad, ux: ux, uy: uy });
      slideWave(waves[waves.length - 1], performance.now());
      add('line', { x1: a[0] + ux * INSET, y1: a[1] + uy * INSET, x2: b[0] - ux * INSET, y2: b[1] - uy * INSET, stroke: 'url(#' + id + ')', 'stroke-width': width });
    }
    const n = routes.length;
    [false, true].forEach(function (boldPass) {
      routes.forEach(function (r, i) {
        if (!!r.bold !== boldPass) return;
        const ids = r.places || [r.from || 'Ruin'].concat(r.stops, ['Ruin']);   // the route's places from its start to its end (a trip ends where its cargo goes, stage 1b)
        const shift = (i - (n - 1) / 2) * 3.5 * K;
        let waved = false;   // the waves run on one leg only: the first, or the one after the stops of a run that are done
        for (let s = 0; s + 1 < ids.length; s++) {
          if (ids[s] === ids[s + 1]) continue;
          if (r.bold && !r.dashed && s < (r.passed || 0)) continue;   // a leg to a stop that is done is gone: the run starts at the leg he is flying now
          const ends = legEnds(ids[s], ids[s + 1]);
          if (!ends) continue;   // a leg in another system: on that system's tab
          let a = ends.a, b = ends.b;
          const dx = b[0] - a[0], dy = b[1] - a[1], len = Math.sqrt(dx * dx + dy * dy);
          if (len < 1) continue;
          const ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
          a = [a[0] + nx * shift, a[1] + ny * shift];
          b = [b[0] + nx * shift, b[1] + ny * shift];
          const wide = (r.bold ? 3.5 : r.dashed ? 2.5 : 2) * K;
          if (r.dashed) add('line', { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: r.color, 'stroke-width': wide, 'stroke-opacity': 0.92, 'stroke-dasharray': '1 ' + 5 * K, 'stroke-linecap': 'round' });
          else add('line', { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: r.color, 'stroke-width': wide, 'stroke-opacity': r.bold ? 1 : 0.67 });
          if (r.bold && !r.dashed && !waved && s >= (r.passed || 0)) { waved = true; wave(a, b, ux, uy, len, r.color, 3.5 * K); }
          const cx = a[0] + (b[0] - a[0]) * 0.62, cy = a[1] + (b[1] - a[1]) * 0.62, z = (r.bold ? 8 : 6) * K;
          const pts = [
            [cx + ux * z, cy + uy * z],
            [cx - ux * z * 0.7 + nx * z * 0.6, cy - uy * z * 0.7 + ny * z * 0.6],
            [cx - ux * z * 0.7 - nx * z * 0.6, cy - uy * z * 0.7 - ny * z * 0.6]
          ].map(function (q) { return q[0] + ',' + q[1]; }).join(' ');
          add('polygon', { points: pts, fill: r.color });
          if (ends.label) map.el('text', { x: b[0] + 10, y: b[1] + 4, fill: r.color, 'font-size': 11, 'font-weight': 700, stroke: '#111', 'stroke-width': 3, 'paint-order': 'stroke', 'stroke-linejoin': 'round' }, g, ends.label);
        }
      });
    });
    // a ring around every station a route touches (where it starts, stops or ends): no station looks special, Ruin neither (Frank, 29 Oct 2026: the
    // planner is to work with every kind of hauling and lose its Ruin bias, so the map knows no home station)
    const used = {};
    routes.forEach(function (r) { (r.places || [r.from || 'Ruin', 'Ruin'].concat(r.stops)).forEach(function (id) { used[id] = true; }); });
    places.forEach(function (pl) {
      const q = pos[pl.id];
      if (q && used[pl.id]) add('circle', { cx: q[0], cy: q[1], r: 7, fill: 'none', stroke: '#ecece8', 'stroke-opacity': 0.7, 'stroke-width': 1.5 });
    });
    Object.keys(usedGates).forEach(function (key) {   // and the gateways the routes go through
      const q = map.at(key);
      if (q) add('circle', { cx: q[0], cy: q[1], r: 7, fill: 'none', stroke: '#ecece8', 'stroke-opacity': 0.7, 'stroke-width': 1.5 });
    });
  }
  // the system on the map: the one picked in the row (or the one looked at before); its routes, then the map's players and objectives in it
  function showSystem(id) {
    shownSys = id;
    if (planMaps[tab]) planMaps[tab].system = id;
    if (row) tabButtons(row, systems.map(function (s) { return { key: s.id, text: s.name }; }), shownSys, showSystem);
    map.show(sysData(id));
    drawRoutes();
    if (mapData) map.draw(mapData, keep);
  }
  planMaps[tab] = { map: map, drawRoutes: drawRoutes, keep: keep, waves: waves, system: shownSys, sysData: sysData };
  showSystem(shownSys);
  return map;
}

// The players and the objectives of the Trip Planner's map (the Map tool's Pyro, /hauling/map.json), every 3 seconds while the tab is shown. The program's
// own page has no such address (404): then the map shows the routes alone and the poll stops.
// The missions going on, as the Map tool lists them under its map: a copy above the contracts of the Session tab (Frank, 30 Oct 2026: Active Contracts).
function missionsTable(box, list) {
  box.textContent = '';
  if (!list || list.length === 0) { const p = document.createElement('p'); p.className = 'muted'; p.textContent = 'No contracts going on.'; box.appendChild(p); return; }
  const t = document.createElement('table'); t.className = 'missions view';
  const hr = t.createTHead().insertRow();
  [['#', ''], ['Mission', ''], ['Who', ''], ['Open', 'right'], ['Faction', ''], ['Category', ''], ['SCU', 'right'], ['Reward', 'right'], ['MG scrip', 'right'], ['Council scrip', 'right'], ['XP', 'right'], ['Legality', '']]
    .forEach(function (h) { const th = document.createElement('th'); th.textContent = h[0]; if (h[1]) th.className = h[1]; hr.appendChild(th); });
  const tb = t.createTBody();
  list.forEach(function (m, n) {
    const tr = tb.insertRow();
    [String(n + 1), m.title, m.who.join(', '), String(m.open), m.faction, m.category, m.scu, m.reward, m.mg, m.council, m.xp, m.legality].forEach(function (v, i) {
      const td = tr.insertCell(); td.textContent = v;
      if (i === 1) td.className = 'name'; else if (i === 3 || (i >= 6 && i <= 10)) td.className = 'right';
    });
  });
  const scroll = document.createElement('div'); scroll.className = 'scroll'; scroll.appendChild(t); box.appendChild(scroll);
}
function renderActive() {
  if (mapData && changed('active', mapData.missions || [])) missionsTable(document.getElementById('active'), mapData.missions);
}

async function refreshMap() {
  if (document.hidden || mapOff || (shownTab !== 'P' && shownTab !== 'S')) return;   // the Session tab wants the map's Active Contracts (30 Oct 2026)
  try {
    const res = await fetch('/hauling/map.json', { cache: 'no-store' });
    if (res.status === 404) { mapOff = true; return; }
    if (!res.ok) throw new Error('HTTP ' + res.status);
    mapData = await res.json();
    if (shownTab === 'S') renderActive();
    const pm = planMaps[shownTab], pd = last && last.planner;
    if (pm && pd) {
      if (pm.map.show(pm.sysData(pm.system))) pm.drawRoutes();   // the places changed (the admin's Map places): drawn anew
      pm.map.draw(mapData, pm.keep);
    }
  } catch (e) {
    // the next poll tries again
  }
}
setInterval(refreshMap, 3000);

// What the Trip Planner shows: Everyone, one party (its members' routes), or Only me (the viewer's own, when he is logged in and one of the workers).
// The viewer's party first. The plan itself is the same: this only picks whose routes are drawn and listed.
let planShown = null;   // 'all', 'me', or the leader of a party
function planChoices(p) {
  const choices = [{ key: 'all', text: 'Everyone' }];
  const parties = (last.parties || []).filter(function (x) { return x.playing !== false; });   // the plan is for the ones playing
  if (parties.length > 1) parties.forEach(function (x) { choices.push({ key: x.leader, text: x.title }); });
  if (ME && p.workers.some(function (w) { return (w.name || '').toLowerCase() === ME; })) choices.push({ key: 'me', text: 'Only me' });
  return choices;
}
function planNames(p, choices) {
  if (planShown === null) {
    const kept = remembered('plan'), mine = myParty();
    planShown = kept !== null ? kept : (mine && (last.parties || []).length > 1 ? mine.leader : 'all');
  }
  if (!choices.some(function (c) { return c.key === planShown; })) planShown = 'all';
  if (planShown === 'me') return [ME];
  if (planShown === 'all') return null;
  return (last.parties || []).find(function (x) { return x.leader === planShown; }).names;
}

// A part of the page rebuilt from nothing (the planner's whole body, every few seconds while somebody plays) makes the page shorter for a moment, and the
// browser pulls a reader who has scrolled down back up (Frank, 30 Oct 2026: watching the Cargo Runs under the map). The part keeps its height while it is
// rebuilt, and the page its scroll position after it.
function keepScroll(part, build) {
  const y = window.scrollY, h = part.offsetHeight;
  part.style.minHeight = h + 'px';
  try { build(); } finally {
    part.style.minHeight = '';
    if (window.scrollY !== y) window.scrollTo(0, y);
  }
}

// The Route Planner tab (P, data.planner): the planner without a hub (docs/planner-any-hauling.md), the one planner since the evening of 30 Oct 2026 (for a day
// a second tab, N, showed it beside the old planner; the drawing is still by tab, so a second one could come back).
function renderPlanner(tab) { keepScroll(document.getElementById('planBody' + tab), function () { buildPlanner(tab); }); }
function buildPlanner(tab) {
  const p = last.planner;
  document.getElementById('planNote' + tab).textContent = '';   // no line above the map (like in the program): the tables get the room
  const body = document.getElementById('planBody' + tab);
  body.textContent = '';
  const choices = planChoices(p);
  const names = planNames(p, choices);
  let pick = null;   // the choice (Everyone, a party, Only me): in the logged in player's own route card, top right (Frank, 30 Oct 2026); at the top when he has no card shown
  if (choices.length > 1) {
    pick = document.createElement('div'); pick.className = 'sub';
    tabButtons(pick, choices, planShown, function (key) { planShown = key; remember('plan', key); renderPlanner(tab); fitHeaders(); });
  }
  const shown = function (name) { return !names || among(names, name || ''); };
  const workers = p.workers.filter(function (w) { return shown(w.name); });
  const ownCard = ME && workers.some(function (w) { return (w.name || '').toLowerCase() === ME; });
  if (pick && !ownCard) body.appendChild(pick);
  // the map on the left with the short copy of Cargo Runs under it, the tables of everybody on the right (like in the program)
  const wrap = document.createElement('div'); wrap.className = 'planwrap';
  const left = document.createElement('div'); left.className = 'planleft';
  const mapBox = document.createElement('div'); mapBox.className = 'mapbox';
  const tables = document.createElement('div'); tables.className = 'plantables';
  left.appendChild(mapBox); wrap.appendChild(left); wrap.appendChild(tables); body.appendChild(wrap);
  const routes = [];
  workers.forEach(function (w) {
    w.routes.forEach(function (r, i) { routes.push({ label: i === 0 ? w.name : '', color: w.color, stops: r.stops, places: r.places, from: r.from, passed: r.passed || 0, bold: !!r.solid, dashed: !r.solid }); });
  });
  mapSvg(mapBox, p, routes, workers.map(function (w) { return w.name || ''; }), tab);
  renderStatus(tab, mapBox);
  workers.forEach(function (w) {
    const frame = document.createElement('div'); frame.className = 'planworker'; tables.appendChild(frame);   // every worker in his own frame
    const h = document.createElement('h2'); frame.appendChild(h);
    const nm = document.createElement('span'); nm.textContent = w.heading || w.name || ''; if (w.color) nm.style.color = w.color; h.appendChild(nm);   // his name in capitals, in his colour
    h.appendChild(document.createTextNode(w.title));
    if (pick && ownCard && (w.name || '').toLowerCase() === ME) h.appendChild(pick);   // his own card: the choice at its right end, the name and the location stay left (Frank, 30 Oct 2026)
    // the map's line for him (where he is, what he does) and his speed meter: the line goes into the heading after the location, not under it (Frank,
    // 30 Oct 2026: not the place twice): "At Ruin Station since 01:49" becomes "since 01:49" when it is the station of the heading, anything else
    // (in quantum, off the grid, elsewhere) comes after a dot. The map keeps the line itself up to date every frame, so a watcher follows it
    const meter = planMaps[tab].map.meter(w.name || '');
    frame.appendChild(meter);
    const what = meter.querySelector('.what');
    if (what) {
      const since = document.createElement('span'); since.className = 'since';
      h.insertBefore(since, pick && pick.parentNode === h ? pick : null);
      const follow = function () { since.textContent = sinceText(what.textContent, w.location || ''); };
      new MutationObserver(follow).observe(what, { childList: true, characterData: true, subtree: true });
      follow();
    }
    if (!w.current) return;   // nothing to do for him
    const pair = document.createElement('div'); pair.className = 'planpair'; frame.appendChild(pair);
    [['plancur', 'Current Route', w.current], ['planque', 'Queued routes', w.queued]].forEach(function (x) {
      const col = document.createElement('div'); col.className = x[0]; pair.appendChild(col);
      const t = document.createElement('h3'); t.textContent = x[1]; col.appendChild(t);
      if (x[0] === 'plancur') submitButton(t, w, p.tripEnd);   // the Submit button at the right end of the title while he has a run on the way (30 Oct 2026)
      const box = document.createElement('div'); box.className = 'scroll'; col.appendChild(box);
      table(box, x[2]);
      if (x[0] === 'plancur' && p.actionWidth) { const th = box.querySelector('th:nth-child(3)'); if (th) th.style.width = p.actionWidth + 'px'; }
      if (x[0] === 'plancur') tripScuFields(box, w, p.tripScu);
    });
  });
  // the short copy of Cargo Runs under the map: #, Worker, Value, Started, Completed, Time and In flight (the Session tab has the whole table); the Time
  // of a run on the way counts up here too (Frank, 29 Oct 2026)
  const runs = last.planRuns || last.runs;
  const live = [];
  const rows = runs ? runs.rows.filter(function (r, i) { const ok = !names || shown(r.cells[1]); if (ok) live.push((last.runsLive || [])[i] || null); return ok; }) : [];
  const runsBox = section(left, 'h2', 'Cargo Runs', runs ? { headers: runs.headers, rows: rows } : runs);
  runsBox.querySelectorAll('tbody tr').forEach(function (tr, i) { if (live[i] && tr.cells[5]) addTicker(tr.cells[5], live[i]); });
}

// The small team status line under the map: it fades in only when the text really changed. It is drawn on its own, so a new line (every few seconds while
// somebody plays) does not draw the map and the tables anew.
function renderStatus(tab, mapBox) {
  const p = last.planner;
  mapBox = mapBox || document.querySelector('#planBody' + tab + ' .mapbox');
  if (!mapBox) return;
  let st = mapBox.querySelector('.status');
  if (!p || !p.status) { if (st) st.remove(); return; }
  if (!st) { st = document.createElement('div'); st.className = 'status'; mapBox.appendChild(st); }
  st.textContent = p.status;
  if (p.statusKey !== shownStatus) {
    st.classList.remove('fade'); void st.offsetWidth;   // the fade starts again on a line that is already there
    st.classList.add('fade');
    shownStatus = p.statusKey;
  }
}

function renderDebts() { keepScroll(document.getElementById('viewD'), buildDebts); }
function buildDebts() {
  const el = document.getElementById('viewD');
  el.textContent = '';
  const d = last.debts;
  section(el, 'h2', 'Earnings', d.earnings);
  signColors(el.lastChild, 3, d);
  const h = document.createElement('h2'); h.textContent = 'Balances'; el.appendChild(h);
  d.balances.forEach(function (b) {
    section(el, 'h3', b.name, b.grid);
    el.lastChild.classList.add('bal');   // the same column widths in every table
    signColors(el.lastChild, 1, d);
    signColors(el.lastChild, 2, d, el.lastChild.querySelectorAll('tbody tr'));   // the meaning has the colour of its balance
  });
  section(el, 'h2', 'Money transfers', d.transfers);
  paymentTicks(el.lastChild, d.transfersEdit || [], last.debtsPay);
  section(el, 'h2', 'Mission payouts', d.payouts);
}

// The Payment column of the money transfers: the player who sent the money ticks whether it pays off a debt (sent at once in the background, as the SCU
// field); everybody else sees yes or nothing. Watching (not logged in), or the program's own page: text only.
function paymentTicks(box, edits, url) {
  if (!url || !ME) return;
  const rows = box.querySelectorAll('tbody tr');
  edits.forEach(function (e, i) {
    const row = rows[i];
    if (!e || !row || !row.cells[4] || (e.from || '').toLowerCase() !== ME) return;
    const cell = row.cells[4];
    cell.textContent = '';
    const input = document.createElement('input');
    input.type = 'checkbox';
    input.checked = !!e.payment;
    input.setAttribute('aria-label', 'Payment');
    input.title = 'Tick when this transfer pays off a debt';
    input.addEventListener('change', function () {
      const token = document.querySelector('input[name="__RequestVerificationToken"]');
      const body = new FormData();
      body.append('key', e.key); body.append('payment', input.checked ? 'true' : 'false');
      if (token) body.append('__RequestVerificationToken', token.value);
      input.disabled = true;
      fetch(url, { method: 'POST', body: body, headers: { 'X-Autosave': '1' }, credentials: 'same-origin' })
        .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
        .then(function (d) { input.disabled = false; if (!d.ok) { input.checked = !input.checked; input.title = d.said || 'Not saved.'; } })
        .catch(function () { input.disabled = false; input.checked = !input.checked; input.title = 'Not saved: the site could not be reached. Try again.'; });
    });
    cell.appendChild(input);
  });
}

// The SCU column of a Current Route: on the stop a drop-off of a planned trip goes to, the hauler of that trip (or the admin) types how much goes there
// (Frank, 29 Oct 2026); it is kept for that drop-off only, and the plan is worked out again with it. Everybody else sees the number.
function tripScuFields(box, w, url) {
  if (!url || !ME || !w.lines || !(ADMIN || (w.name || '').toLowerCase() === ME)) return;
  const rows = box.querySelectorAll('tbody tr');
  w.lines.forEach(function (l, i) {
    const row = rows[i];
    if (!l || !l.typable || !l.keys || l.keys.length !== 1 || !row || !row.cells[3]) return;
    const cell = row.cells[3], was = l.scu > 0 ? String(l.scu) : '';
    cell.textContent = '';
    const input = document.createElement('input');
    input.className = 'scuin'; input.inputMode = 'numeric'; input.value = was; input.placeholder = '?';
    input.setAttribute('aria-label', 'SCU to ' + (row.cells[1].textContent || 'this stop'));
    input.title = 'How many SCU go to this stop (your own trip only; the boxes follow). Empty: back to what the game or the library says.';
    input.addEventListener('change', function () {
      const token = document.querySelector('input[name="__RequestVerificationToken"]');
      const body = new FormData();
      body.append('key', l.keys[0]); body.append('value', input.value.trim());
      if (token) body.append('__RequestVerificationToken', token.value);
      input.className = 'scuin saving';
      fetch(url, { method: 'POST', body: body, headers: { 'X-Autosave': '1' }, credentials: 'same-origin' })
        .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
        .then(function (d) { input.className = 'scuin ' + (d.ok ? 'saved' : 'unsaved'); input.title = d.said || ''; if (d.ok) refresh(); })
        .catch(function () { input.className = 'scuin unsaved'; input.title = 'Not saved: the site could not be reached. Try again.'; });
    });
    cell.appendChild(input);
  });
}

// The Submit button at the right end of the Current Route title (Frank, 30 Oct 2026): the worker himself (or the admin) ends his run on the way here; it is
// counted as it is, with what it picked up, and the plan goes on from where he is. Only while he has a run on the way, and only after "Are you sure?".
function submitButton(title, w, url) {
  if (!url || !ME || !w.running || !(ADMIN || (w.name || '').toLowerCase() === ME)) return;
  const b = document.createElement('button'); b.type = 'button'; b.className = 'submitroute'; b.textContent = 'Submit';
  b.title = 'End the current route here: it is counted as it is, with what was picked up';
  b.addEventListener('click', function () {
    if (!confirm('Are you sure? You will end the current route, and it is submitted as it is: what was picked up counts, the rest is planned again.')) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]');
    const body = new FormData();
    body.append('name', w.name || '');
    if (token) body.append('__RequestVerificationToken', token.value);
    b.disabled = true;
    fetch(url, { method: 'POST', body: body, headers: { 'X-Autosave': '1' }, credentials: 'same-origin' })
      .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
      .then(function (d) { b.disabled = false; b.title = d.said || ''; if (d.ok) refresh(); else alert(d.said || 'Not done.'); })
      .catch(function () { b.disabled = false; alert('Not done: the site could not be reached. Try again.'); });
  });
  title.appendChild(b);
}

// The map's line for a worker, shortened for the heading of his route card: "At Ruin Station since 01:49" is " since 01:49" when Ruin Station is
// the heading's station; at another place, in quantum or off the grid, the whole line comes after a dot; nothing when the map has nothing.
function sinceText(line, station) {
  const m = /^At (.+?) since (\S+)(.*)$/.exec(line || '');
  if (m && m[1] === station) return ' since ' + m[2] + m[3];
  return line ? ' · ' + line.charAt(0).toLowerCase() + line.slice(1) : '';
}

// The Lost Cargo column of the Cargo runs: the admin picks None, Little or All right here (sent at once in the background, as the SCU field) instead of
// on his Session page (Frank, 29 Oct 2026); everybody else sees the text. The table is drawn anew when the data comes back with the pick in it.
function lostPicks(box, keys, url) {
  if (!url || !ADMIN || !keys) return;
  const rows = box.querySelectorAll('tbody tr');
  keys.forEach(function (key, i) {
    const row = rows[i];
    if (!key || !row || !row.cells[10]) return;
    const cell = row.cells[10], was = cell.textContent;
    cell.textContent = '';
    const pick = document.createElement('select');
    pick.className = 'lostpick';
    pick.setAttribute('aria-label', 'Lost cargo');
    ['None', 'Little', 'All'].forEach(function (v) { const o = document.createElement('option'); o.value = v; o.textContent = v; o.selected = v === was; pick.appendChild(o); });
    pick.addEventListener('change', function () {
      const token = document.querySelector('input[name="__RequestVerificationToken"]');
      const body = new FormData();
      body.append('key', key); body.append('level', pick.value);
      if (token) body.append('__RequestVerificationToken', token.value);
      pick.disabled = true;
      fetch(url, { method: 'POST', body: body, headers: { 'X-Autosave': '1' }, credentials: 'same-origin' })
        .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
        .then(function (d) { pick.disabled = false; pick.title = d.said || ''; if (!d.ok) pick.value = was; else refresh(); })
        .catch(function () { pick.disabled = false; pick.value = was; pick.title = 'Not saved: the site could not be reached. Try again.'; });
    });
    cell.appendChild(pick);
  });
}

function renderHistory() { keepScroll(document.getElementById('viewH'), buildHistory); }
function buildHistory() {
  const el = document.getElementById('viewH');
  el.textContent = '';
  const d = last.history;
  // the All-time leaderboard is on the Leaderboard tab (Frank, 30 Oct 2026); the best session line is under the saved sessions (Frank, the same day)
  section(el, 'h2', 'Saved sessions', d.saved);
  const best = document.createElement('div'); best.className = 'empty'; best.textContent = d.best || ''; el.appendChild(best);
}

// Every tab has its own address: /hauling/session, /hauling/planner, /hauling/debts, /hauling/history, /hauling/leaderboard on the site; the program's
// own page (at /) keeps them after a # (#planner). The old #plan links still open the Trip Planner, and /hauling/contracts the Session tab, where the
// contracts are since 30 Oct 2026 (Frank; the Leaderboard tab is new that day: the Session leaderboard and the All-time leaderboard).
const TABS = { S: 'session', P: 'planner', D: 'debts', H: 'history', L: 'leaderboard' };
const ON_SITE = /^\/hauling(\/|$)/.test(location.pathname);
// Only the tab shown is drawn when data comes; the others are drawn when they are shown (dirty: the tabs with data they have not shown yet).
const dirty = { S: true, P: true, D: true, H: true, L: true };
const HAVE = Object.keys(TABS).filter(function (k) { return !!document.getElementById('tab' + k); });   // the tabs on this page
const FIRST = HAVE.indexOf('P') >= 0 ? 'P' : HAVE[0];   // the Route Planner first (Frank, 29 Oct 2026)
let shownTab = FIRST;
function tabOf(name) {
  name = (name || '').replace(/^#/, '');
  if (name === 'plan') name = 'planner';
  if (name === 'contracts') name = 'session';   // the contracts are on the Session tab since 30 Oct 2026
  return HAVE.find(function (k) { return TABS[k] === name; });
}
function show(which) {
  shownTab = which;
  if (which === 'P' || which === 'S') refreshMap();   // the map's players (and the Session tab's Active Contracts) at once, not at the next poll
  HAVE.forEach(function (k) {
    document.getElementById('view' + k).classList.toggle('hidden', k !== which);
    document.getElementById('tab' + k).classList.toggle('on', k === which);
  });
  // the Session tab has more than its view: the session beside the tabs, the contracts and the runs under both
  document.getElementById('sideS').classList.toggle('hidden', which !== 'S');
  document.getElementById('belowS').classList.toggle('hidden', which !== 'S');
  try { history.replaceState(null, '', ON_SITE ? '/hauling/' + TABS[which] : '#' + TABS[which]); } catch (e) { }
  if (last && dirty[which]) renderTab(which);
  layout();   // the tables of a tab that was hidden could not be measured
}
HAVE.forEach(function (k) { document.getElementById('tab' + k).onclick = function (e) { if (e && e.preventDefault) e.preventDefault(); show(k); }; });   // a link to the tab's address: shown here, no reload
show((ON_SITE && tabOf(location.pathname.split('/')[2])) || tabOf(location.hash) || FIRST);

// Whose session the Session and Leaderboard tabs show: with one party, the whole session is theirs; with more than one (the ones in the game, then the ones
// that hauled in this session and are not playing now), a player sees his own party by itself (its stops, contracts, session, leaderboard and runs: the
// missions its players have by their own logs, and their runs), and all of them together when he is in none (or not logged in). The admin looks as a
// player through the top bar's pick (30 Oct 2026, PointOfView: the page is that player's then) and sees everything together in his own view; the Route
// Planner has its own choice (Everyone, a party, Only me).
let partyText = '';   // whose session is shown, for the contracts on the Session tab
function partyView() {
  const parties = last.parties || [];
  const several = parties.length > 1;
  const mine = several ? myParty() : null;
  const shown = mine && mine.view ? mine : null;
  partyText = !several ? ''
    : shown ? 'Your party: ' + shown.title + ' (' + shown.names.join(', ') + ')' + (shown.playing === false ? ', not playing now' : '') + '. The others haul in this session too.'
    : 'All parties together: ' + parties.map(function (p) { return p.title; }).join(', ') + (ME ? ' (you are in none of them).' : ' (log in to see your own).');
  return shown ? shown.view : last;
}

// ------------------------------------------------------------------ the SCU field of the Other hauling board
// A hauler logged in types the SCU of a mission the game gave none of (its row's field): sent at once in the background (as on the Mission Library), it
// counts for that mission only, and the library gets it as the suggestion for its kind. Until the board brings it back, the field keeps what he typed.

function typingIn(box) {
  const a = document.activeElement;
  return document.hasFocus() && a && box.contains(a) && a.tagName === 'INPUT';
}


// What every part of the page was drawn from last (as JSON text): a part whose data is the same is left as it is, so an update that changes one table
// draws one table, and the status line alone draws nothing but the status line.
const drawn = {};
function changed(key, value) {
  const s = JSON.stringify(value === undefined ? null : value);
  if (drawn[key] === s) return false;
  drawn[key] = s;
  return true;
}

// Everything on the page from the data fetched last (again when a choice on the Trip Planner is clicked): the tab shown now is drawn, the others when they
// are shown.
function render() {
  Object.keys(dirty).forEach(function (k) { dirty[k] = true; });
  const v = partyView();   // whose session: the viewer's party; the session line in the tabs' row on every tab, not only the Session tab (30 Oct 2026)
  document.getElementById('session').textContent = v.session || last.session || '';
  renderTab(shownTab);
}

function renderTab(k) {
  dirty[k] = false;
  const d = last;
  const v = k === 'S' || k === 'L' ? partyView() : null;
  if (k === 'S') {
    renderActive();   // the map's missions going on, fetched last (refreshMap keeps them fresh while the tab is shown)
    let measure = false;   // a table that lines up with the others was drawn anew: line them up again
    if (changed('pickups', v.pickups)) { table(document.getElementById('pickups'), v.pickups); measure = true; }
    document.getElementById('finishedBox').classList.toggle('hidden', !v.finished || !v.finished.rows || v.finished.rows.length === 0);
    if (changed('finished', v.finished)) { table(document.getElementById('finished'), v.finished); measure = true; }
    // (the Other hauling board that was here until 30 Oct 2026 is gone: the hauling jobs of every system are on the Stops board, and the SCU of a job's drop-off
    // is typed on the Current Route, like a load from Ruin)
    if (changed('summary', v.summary || [])) summary(document.getElementById('summary'), v.summary || []);
    // the contracts of the party shown, under the stops where the Session leaderboard was (Frank, 30 Oct 2026; a tab of their own before)
    if (changed('rewards', v.rewards)) table(document.getElementById('rewards'), v.rewards);
    document.getElementById('rewardsParty').textContent = partyText;
    if (changed('runs', [v.runs, v.runsLive, v.runKeys])) {
      table(document.getElementById('runs'), v.runs);
      measure = true;
      if (v.runsLive) {
        const runRows = document.querySelectorAll('#runs tbody tr');
        v.runsLive.forEach(function (live, i) { if (live && runRows[i] && runRows[i].cells[7]) addTicker(runRows[i].cells[7], live); });
      }
      lostPicks(document.getElementById('runs'), v.runKeys, d.runsLost);
    }
    if (measure) alignColumns();
  } else if (k === 'L') {
    // the Leaderboard tab (Frank, 30 Oct 2026): the Session leaderboard of the party shown, and the All-time leaderboard of every saved session
    if (changed('leaders', v.leaders)) { table(document.getElementById('leaders'), v.leaders); alignColumns(); }
    if (d.history && changed('allTime', d.history.allTime)) table(document.getElementById('allTime'), d.history.allTime);
  } else if (k === 'P') {
    if (!d.planner) return;
    const rest = Object.assign({}, d.planner);
    delete rest.status; delete rest.statusKey;   // the status line is drawn on its own
    if (changed('planner', [rest, d.planRuns || d.runs, d.parties, planShown])) renderPlanner(k);
    else renderStatus(k);
  } else if (k === 'D') {
    if (d.debts && changed('debts', d.debts)) renderDebts();
  } else if (k === 'H') {
    if (d.history && changed('history', d.history)) renderHistory();
  }
  fitHeaders();
}

// The poll, every 3 seconds: the ETag of the data fetched last goes with it (If-None-Match), and the same data again comes back as 304 with no body, which
// costs nothing to speak of on either side. A tab nobody looks at does not poll; it does when it is shown again.
let etag = null;
async function refresh() {
  if (document.hidden) return;
  const live = document.getElementById('live');
  try {
    const res = await fetch('/data.json', { cache: 'no-store', headers: etag ? { 'If-None-Match': etag } : {} });
    if (res.status === 304) { live.textContent = '● live'; live.className = 'live ok'; return; }   // the same data as last time: nothing to draw
    if (!res.ok) throw new Error('HTTP ' + res.status);
    const d = await res.json();
    etag = res.headers.get('ETag');
    last = d;
    // d.theme (the program's palette) is not applied: the page has the site's colours (site.css) since it became a page of the site, 29 Oct 2026
    document.getElementById('updated').textContent = 'updated ' + d.updated;
    live.textContent = '● live'; live.className = 'live ok';
    render();
  } catch (e) {
    live.textContent = '● connection lost, trying again'; live.className = 'live bad';
  }
}
document.addEventListener('visibilitychange', function () { if (!document.hidden) { refresh(); refreshMap(); } });
refresh();
setInterval(refresh, 3000);
