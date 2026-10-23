var last = null;
var shownStatus = -1;   // the last status line text that was faded in

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

// Clocks that run (the session time and the run time of runs on the way) count up on the page every second between the updates.
let tickers = [];
let fetchedAt = Date.now();
function parseHms(s) { const m = /^(\d+):(\d\d):(\d\d)$/.exec(s || ''); return m ? (+m[1]) * 3600 + (+m[2]) * 60 + (+m[3]) : null; }
function fmtHms(n) { const h = Math.floor(n / 3600), m = Math.floor(n % 3600 / 60), s = n % 60; return h + ':' + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0'); }
function addTicker(td) { const b = parseHms(td.textContent); if (b !== null) tickers.push({ td: td, base: b }); }
setInterval(function () {
  const dt = Math.floor((Date.now() - fetchedAt) / 1000);
  tickers.forEach(function (t) { t.td.textContent = fmtHms(t.base + dt); });
}, 1000);

// The first columns of Stops, Finished stops and Cargo runs are as wide as the same columns of the Session leaderboard, so they line up (like in the program).
function alignColumns() {
  const lead = document.querySelector('#leaders table');
  if (!lead || !lead.tHead) return;
  const w = Array.prototype.map.call(lead.tHead.rows[0].cells, function (c) { return c.getBoundingClientRect().width; });
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
  fix('pickups', 6); fix('finished', 6); fix('runs', 2, 'Pickup Location & Time');
}
window.addEventListener('resize', alignColumns);

function summary(el, items) {
  el.textContent = '';
  const t = document.createElement('table'); t.className = 'kv';
  const tb = t.createTBody();
  items.forEach(function (it) {
    const tr = tb.insertRow();
    tr.insertCell().textContent = it.k;
    const td = tr.insertCell(); td.textContent = it.v; td.style.textAlign = 'right';
    if (it.live) addTicker(td);
  });
  el.appendChild(t);
}

function section(parent, tag, title, g) {
  const h = document.createElement(tag); h.textContent = title; parent.appendChild(h);
  const box = document.createElement('div'); box.className = 'scroll'; parent.appendChild(box);
  table(box, g);
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

// The waves on the map move with the clock of the page, not with an SVG animation (that would start again every time the map is drawn anew).
const WAVE_LEN = 28, WAVE_SPEED = 20;   // px from one dark band to the next, px per second
let waveGrads = [];
function slideWave(w, now) {
  const shift = (now / 1000 * WAVE_SPEED) % WAVE_LEN;
  w.el.setAttribute('gradientTransform', 'translate(' + (w.ux * shift).toFixed(2) + ' ' + (w.uy * shift).toFixed(2) + ')');
}
function moveWaves(now) {
  if (waveGrads.length && !document.hidden && !document.getElementById('viewP').classList.contains('hidden')) waveGrads.forEach(function (w) { slideWave(w, now); });
  requestAnimationFrame(moveWaves);
}
requestAnimationFrame(moveWaves);

function mapSvg(el, stations, routes, bg) {
  el.textContent = '';
  waveGrads = [];
  const NS = 'http://www.w3.org/2000/svg';
  const W = 520, H = 560, L = 40, R = 150, B = 24;
  const legend = routes.filter(function (r) { return r.label; });
  const cols = Math.max(1, Math.floor(W / 170));
  const T = 14 + Math.ceil(legend.length / cols) * 16;
  let minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
  stations.forEach(function (s) { minX = Math.min(minX, s.x); maxX = Math.max(maxX, s.x); minY = Math.min(minY, s.y); maxY = Math.max(maxY, s.y); });
  const w = W - L - R, h = H - T - B;
  const sc = Math.min(w / (maxX - minX), h / (maxY - minY));
  const ox = L + (w - (maxX - minX) * sc) / 2, oy = T + (h - (maxY - minY) * sc) / 2;
  const pos = {};
  stations.forEach(function (s) { pos[s.id] = [ox + (s.x - minX) * sc, oy + (s.y - minY) * sc]; });
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
  svg.setAttribute('width', '100%');
  function add(tag, attrs, text) {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (text !== undefined) e.textContent = text;
    svg.appendChild(e);
    return e;
  }
  // Darker waves that run along one leg of a solid route (from where he is to his next stop), in the direction of the arrow.
  function darker(hex, f) {
    const m = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || '');
    if (!m) return '#000000';
    return '#' + [1, 2, 3].map(function (k) { return Math.round(parseInt(m[k], 16) * f).toString(16).padStart(2, '0'); }).join('');
  }
  function wave(a, b, ux, uy, len, color, width) {
    const ALPHA = 215 / 255, INSET = 8;   // how dark the middle of a band is, and clear of the stations at both ends of the leg
    if (len <= 2 * INSET + 2) return;   // a leg that short is hardly more than its two stations
    const id = 'wave' + waveGrads.length, shade = darker(color, 0.18);
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
    waveGrads.push({ el: grad, ux: ux, uy: uy });
    slideWave(waveGrads[waveGrads.length - 1], performance.now());
    add('line', { x1: a[0] + ux * INSET, y1: a[1] + uy * INSET, x2: b[0] - ux * INSET, y2: b[1] - uy * INSET, stroke: 'url(#' + id + ')', 'stroke-width': width });
  }
  add('rect', { x: 0, y: 0, width: W, height: H, fill: '#08080c' });
  const sx = ox + (bg.sun[0] - minX) * sc, sy = oy + (bg.sun[1] - minY) * sc;
  bg.orbits.forEach(function (r) { add('circle', { cx: sx, cy: sy, r: r * sc, fill: 'none', stroke: '#ebebf5', 'stroke-opacity': 0.37, 'stroke-width': 1 }); });
  bg.planets.forEach(function (q) { add('circle', { cx: ox + (q[0] - minX) * sc, cy: oy + (q[1] - minY) * sc, r: Math.max(3.5, 10 * sc), fill: '#8c5514' }); });
  const sr = Math.max(7, bg.sunRadius * sc);
  add('circle', { cx: sx, cy: sy, r: sr * 1.9, fill: '#ff461e', 'fill-opacity': 0.22 });
  add('circle', { cx: sx, cy: sy, r: sr, fill: '#ff3a18' });
  const n = routes.length;
  [false, true].forEach(function (boldPass) {
    routes.forEach(function (r, i) {
      if (!!r.bold !== boldPass) return;
      const ids = [r.from || 'Ruin'].concat(r.stops, ['Ruin']);
      const shift = (i - (n - 1) / 2) * 3.5;
      let waved = false;   // the waves run on one leg only: the first, or the one after the stops of a run that are done
      for (let s = 0; s + 1 < ids.length; s++) {
        if (ids[s] === ids[s + 1]) continue;
        if (r.bold && !r.dashed && s < (r.passed || 0)) continue;   // a leg to a stop that is done is gone: the run starts at the leg he is flying now
        let a = pos[ids[s]], b = pos[ids[s + 1]];
        if (!a || !b) continue;
        const dx = b[0] - a[0], dy = b[1] - a[1], len = Math.sqrt(dx * dx + dy * dy);
        if (len < 1) continue;
        const ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
        a = [a[0] + nx * shift, a[1] + ny * shift];
        b = [b[0] + nx * shift, b[1] + ny * shift];
        if (r.dashed) add('line', { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: r.color, 'stroke-width': r.bold ? 3.5 : 2.5, 'stroke-opacity': 0.92, 'stroke-dasharray': '1 5', 'stroke-linecap': 'round' });
        else add('line', { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: r.color, 'stroke-width': r.bold ? 3.5 : 2, 'stroke-opacity': r.bold ? 1 : 0.67 });
        if (r.bold && !r.dashed && !waved && s >= (r.passed || 0)) { waved = true; wave(a, b, ux, uy, len, r.color, 3.5); }
        const cx = a[0] + (b[0] - a[0]) * 0.62, cy = a[1] + (b[1] - a[1]) * 0.62, z = r.bold ? 8 : 6;
        const pts = [
          [cx + ux * z, cy + uy * z],
          [cx - ux * z * 0.7 + nx * z * 0.6, cy - uy * z * 0.7 + ny * z * 0.6],
          [cx - ux * z * 0.7 - nx * z * 0.6, cy - uy * z * 0.7 - ny * z * 0.6]
        ].map(function (p) { return p[0] + ',' + p[1]; }).join(' ');
        add('polygon', { points: pts, fill: r.color });
      }
    });
  });
  const used = {};
  routes.forEach(function (r) { r.stops.forEach(function (id) { used[id] = true; }); });
  stations.forEach(function (s) {
    const p = pos[s.id];
    if (s.id === 'Ruin') {
      add('rect', { x: p[0] - 6, y: p[1] - 6, width: 12, height: 12, style: 'fill:#ecece8' });
      add('text', { x: p[0] + 9, y: p[1] + 4, style: 'fill:#ecece8;font-size:12px;font-weight:700' }, s.name);
      return;
    }
    const on = !!used[s.id];
    const st = on ? 'fill:#ecece8' : 'fill:#ecece8;opacity:.55';
    add('circle', { cx: p[0], cy: p[1], r: on ? 5 : 3.5, style: st });
    if (s.id === '73041') add('text', { x: p[0] - 8, y: p[1] + 4, 'text-anchor': 'end', style: st + ';font-size:11px' }, s.name);
    else add('text', { x: p[0] + 8, y: p[1] + 4, style: st + ';font-size:11px' }, s.name);
  });
  legend.forEach(function (r, i) {
    const x = 8 + (i % cols) * 170, y = 6 + Math.floor(i / cols) * 16;
    add('line', r.dashed ? { x1: x, y1: y + 8, x2: x + 16, y2: y + 8, stroke: r.color, 'stroke-width': 3, 'stroke-dasharray': '1 5', 'stroke-linecap': 'round' } : { x1: x, y1: y + 8, x2: x + 16, y2: y + 8, stroke: r.color, 'stroke-width': 3 });
    add('text', { x: x + 20, y: y + 12, style: 'fill:#ecece8;font-size:11px' }, r.label);
  });
  el.appendChild(svg);
}

function renderPlanner() {
  const p = last.planner;
  document.getElementById('planNote').textContent = '';   // no line above the map (like in the program): the tables get the room
  const body = document.getElementById('planBody');
  body.textContent = '';
  // the map on the left with the short copy of Cargo Runs under it, the tables of everybody on the right (like in the program)
  const wrap = document.createElement('div'); wrap.className = 'planwrap';
  const left = document.createElement('div'); left.className = 'planleft';
  const mapBox = document.createElement('div'); mapBox.className = 'mapbox';
  const tables = document.createElement('div'); tables.className = 'plantables';
  left.appendChild(mapBox); wrap.appendChild(left); wrap.appendChild(tables); body.appendChild(wrap);
  const routes = [];
  p.workers.forEach(function (w) {
    w.routes.forEach(function (r, i) { routes.push({ label: i === 0 ? w.name : '', color: w.color, stops: r.stops, from: r.from, passed: r.passed || 0, bold: !!r.solid, dashed: !r.solid }); });
  });
  mapSvg(mapBox, p.stations, routes, p.backdrop);
  if (p.status) {
    // the small team status line under the map: it fades in only when the text really changed (the page is built again every few seconds)
    const st = document.createElement('div'); st.className = 'status'; st.textContent = p.status;
    if (p.statusKey !== shownStatus) { st.classList.add('fade'); shownStatus = p.statusKey; }
    mapBox.appendChild(st);
  }
  p.workers.forEach(function (w) {
    const frame = document.createElement('div'); frame.className = 'planworker'; tables.appendChild(frame);   // every worker in his own frame
    const h = document.createElement('h2'); frame.appendChild(h);
    const nm = document.createElement('span'); nm.textContent = w.heading || w.name || ''; if (w.color) nm.style.color = w.color; h.appendChild(nm);   // his name in capitals, in his colour
    h.appendChild(document.createTextNode(w.title));
    if (!w.current) return;   // nothing to do for him
    const pair = document.createElement('div'); pair.className = 'planpair'; frame.appendChild(pair);
    [['plancur', 'Current Route', w.current], ['planque', 'Queued routes', w.queued]].forEach(function (x) {
      const col = document.createElement('div'); col.className = x[0]; pair.appendChild(col);
      const t = document.createElement('h3'); t.textContent = x[1]; col.appendChild(t);
      const box = document.createElement('div'); box.className = 'scroll'; col.appendChild(box);
      table(box, x[2]);
      if (x[0] === 'plancur' && p.actionWidth) { const th = box.querySelector('th:nth-child(3)'); if (th) th.style.width = p.actionWidth + 'px'; }
    });
  });
  // the short copy of Cargo Runs under the map: #, Worker, Value, Started, Completed, Time and In flight (the Session tab has the whole table)
  section(left, 'h2', 'Cargo Runs', last.planRuns || last.runs);
}

function renderDebts() {
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
  section(el, 'h2', 'Mission payouts', d.payouts);
}

function renderHistory() {
  const el = document.getElementById('viewH');
  el.textContent = '';
  const d = last.history;
  section(el, 'h2', 'All-time leaderboard', d.allTime);
  const best = document.createElement('div'); best.className = 'empty'; best.textContent = d.best || ''; el.appendChild(best);
  section(el, 'h2', 'Saved sessions', d.saved);
}

function show(which) {
  ['S', 'P', 'D', 'H'].forEach(function (k) {
    document.getElementById('view' + k).classList.toggle('hidden', k !== which);
    document.getElementById('tab' + k).classList.toggle('on', k === which);
  });
  try { history.replaceState(null, '', '#' + { S: 'session', P: 'plan', D: 'debts', H: 'history' }[which]); } catch (e) { }
}
['S', 'P', 'D', 'H'].forEach(function (k) { document.getElementById('tab' + k).onclick = function () { show(k); }; });
show({ '#plan': 'P', '#debts': 'D', '#history': 'H' }[location.hash] || 'S');

async function refresh() {
  const live = document.getElementById('live');
  try {
    const res = await fetch('/data.json', { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    const d = await res.json();
    last = d;
    tickers = [];
    fetchedAt = Date.now();
    const root = document.documentElement.style;
    if (d.theme) { root.setProperty('--bg', d.theme.bg); root.setProperty('--fg', d.theme.fg); root.setProperty('--header', d.theme.header); root.setProperty('--line', d.theme.line); root.setProperty('--cell', d.theme.cell); }
    document.getElementById('session').textContent = d.session || '';
    document.getElementById('updated').textContent = 'updated ' + d.updated;
    live.textContent = '● live'; live.className = 'live ok';
    table(document.getElementById('pickups'), d.pickups);
    document.getElementById('finishedBox').classList.toggle('hidden', !d.finished || !d.finished.rows || d.finished.rows.length === 0);
    table(document.getElementById('finished'), d.finished);
    table(document.getElementById('rewards'), d.rewards);
    summary(document.getElementById('summary'), d.summary || []);
    table(document.getElementById('party'), d.party);
    table(document.getElementById('leaders'), d.leaders);
    table(document.getElementById('runs'), d.runs);
    alignColumns();
    if (d.runsLive) {
      const runRows = document.querySelectorAll('#runs tbody tr');
      d.runsLive.forEach(function (live, i) { if (live && runRows[i] && runRows[i].cells[7]) addTicker(runRows[i].cells[7]); });
    }
    if (d.planner) renderPlanner();
    if (d.debts) renderDebts();
    if (d.history) renderHistory();
  } catch (e) {
    live.textContent = '● connection lost, trying again'; live.className = 'live bad';
  }
}
refresh();
setInterval(refresh, 3000);
