// The Map tool: Pyro from above (as the Trip Planner draws it), everybody playing where his quantum jumps put him, and the objectives of the missions going
// on. map.json comes every few seconds; in between, a player in quantum moves along his route and his speed meter runs, worked out from his drive the same
// way the server does (QtDrive in the core: stage 1 acceleration up to the switch speed, stage 2 up to the drive speed, the same way down).
(function () {
  'use strict';
  const NS = 'http://www.w3.org/2000/svg';
  const W = 760, PAD = 10, C = 299792458;
  let data = null, offset = 0, base = null, jumpers = [];

  // ------------------------------------------------------------------ the motion of a quantum drive (d: v, a1, a2, sw)
  function ramp(d, to) {   // speeding up from standstill to a speed: time and distance
    const s1 = Math.min(to, d.sw);
    let t = s1 / d.a1, x = s1 * s1 / (2 * d.a1);
    if (to > s1) { const t2 = (to - s1) / d.a2; t += t2; x += (s1 + to) / 2 * t2; }
    return { t: t, x: x };
  }
  function profile(d, dist) {   // the top speed of the jump (lower than the drive's when it is short), the ramps and the cruise
    let top = d.v, r = ramp(d, top);
    if (2 * r.x > dist) {
      let lo = 0, hi = top;
      for (let i = 0; i < 60; i++) { const mid = (lo + hi) / 2; if (2 * ramp(d, mid).x > dist) hi = mid; else lo = mid; }
      top = lo; r = ramp(d, top);
    }
    const cruise = Math.max(0, (dist - 2 * r.x) / top);
    return { top: top, rampT: r.t, rampX: r.x, cruise: cruise, total: 2 * r.t + cruise };
  }
  function upAt(d, top, t) {   // t seconds into speeding up to top: the distance and the speed
    const s1 = Math.min(top, d.sw), t1 = s1 / d.a1;
    if (t <= t1) return { x: d.a1 * t * t / 2, v: d.a1 * t };
    const tt = t - t1, v = Math.min(top, s1 + d.a2 * tt);
    return { x: s1 * s1 / (2 * d.a1) + (s1 + v) / 2 * tt, v: v };
  }
  function motionAt(d, dist, t) {   // t seconds into the jump
    const p = profile(d, dist);
    if (t <= 0) return { x: 0, v: 0, left: p.total, p: p };
    if (t >= p.total) return { x: dist, v: 0, left: 0, p: p };
    let r;
    if (t <= p.rampT) r = upAt(d, p.top, t);
    else if (t <= p.rampT + p.cruise) r = { x: p.rampX + p.top * (t - p.rampT), v: p.top };
    else { const back = upAt(d, p.top, p.total - t); r = { x: dist - back.x, v: back.v }; }   // the way down mirrors the way up
    r.left = p.total - t; r.p = p;
    return r;
  }

  // ------------------------------------------------------------------ the map
  function el(tag, attrs, parent, text) {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (text !== undefined) e.textContent = text;
    (parent || base.svg).appendChild(e);
    return e;
  }
  function build(d) {   // what does not change: the sun, the orbits, the planets, the stations
    const box = document.getElementById('map');
    box.textContent = '';
    const bg = d.backdrop, sun = bg.sun;
    const outer = Math.max.apply(null, bg.orbits.concat(d.stations.map(function (s) { return Math.hypot(s.x - sun[0], s.y - sun[1]); })));
    let sc = (W / 2 - PAD) / outer;
    d.stations.forEach(function (s) {   // room for the names on the right (Orbituary has its name on the left)
      const dx = s.x - sun[0], name = s.name.length * 6.6 + 12;
      if (s.id === '73041') { if (dx < 0) sc = Math.min(sc, (W / 2 - PAD - name) / -dx); }
      else if (dx > 0) sc = Math.min(sc, (W / 2 - PAD - name) / dx);
    });
    const cx = W / 2, cy = W / 2;
    const at = function (x, y) { return [cx + (x - sun[0]) * sc, cy + (y - sun[1]) * sc]; };
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + W);
    svg.setAttribute('width', '100%');
    svg.setAttribute('role', 'img');
    svg.setAttribute('aria-label', 'Map of Pyro');
    box.appendChild(svg);
    base = { svg: svg, at: at, sc: sc, pos: {}, names: {} };
    el('rect', { x: 0, y: 0, width: W, height: W, fill: '#06090e', rx: 6 });
    bg.orbits.forEach(function (r) { el('circle', { cx: cx, cy: cy, r: r * sc, fill: 'none', stroke: '#ebebf5', 'stroke-opacity': 0.3, 'stroke-width': 1 }); });
    bg.planets.forEach(function (q) { const p = at(q[0], q[1]); el('circle', { cx: p[0], cy: p[1], r: Math.max(4, 10 * sc), fill: '#8c5514' }); });
    const sr = Math.max(8, bg.sunRadius * sc);
    el('circle', { cx: cx, cy: cy, r: sr * 1.9, fill: '#ff461e', 'fill-opacity': 0.22 });
    el('circle', { cx: cx, cy: cy, r: sr, fill: '#ff3a18' });
    d.stations.forEach(function (s) {
      const p = at(s.x, s.y);
      base.pos[s.id] = p;
      base.names[s.id] = s.name;
      if (s.id === 'Ruin') el('rect', { x: p[0] - 6, y: p[1] - 6, width: 12, height: 12, fill: '#ecece8' });
      else el('circle', { cx: p[0], cy: p[1], r: 4, fill: '#ecece8', 'fill-opacity': 0.8 });
      const left = s.id === '73041';
      el('text', { x: p[0] + (left ? -9 : 9), y: p[1] + 4, 'text-anchor': left ? 'end' : 'start', fill: '#ecece8', 'font-size': s.id === 'Ruin' ? 13 : 12, 'font-weight': s.id === 'Ruin' ? 700 : 400 }, null, s.name);
    });
    base.objs = el('g', {});
    base.routes = el('g', {});
    base.people = el('g', {});
  }

  function diamond(parent, x, y, size, title) {
    const g = el('g', {}, parent);
    el('polygon', { points: [[x, y - size], [x + size, y], [x, y + size], [x - size, y]].map(function (p) { return p.join(','); }).join(' '), fill: '#f2c14e', stroke: '#06090e', 'stroke-width': 1 }, g);
    if (title) el('title', {}, g, title);
    return g;
  }

  function clock(seconds) {
    const s = Math.max(0, Math.round(seconds));
    return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
  }
  function since(iso) { return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); }

  function render() {
    const d = data;
    base.objs.textContent = ''; base.routes.textContent = ''; base.people.textContent = '';
    jumpers = [];

    // the objectives: the ones at a station as a diamond beside it with how many, the ones in space where they are
    const atStation = {};
    d.objectives.forEach(function (o) {
      if (o.station) (atStation[o.station] = atStation[o.station] || []).push(o);
      else if (o.x !== undefined) { const p = base.at(o.x, o.y); diamond(base.objs, p[0], p[1], 5, o.title + ' (' + o.player + ')'); }
    });
    Object.keys(atStation).forEach(function (id) {
      const p = base.pos[id]; if (!p) return;
      const list = atStation[id];
      diamond(base.objs, p[0] - 2, p[1] - 13, 5, list.map(function (o) { return o.title; }).join('\n'));
      if (list.length > 1) el('text', { x: p[0] + 5, y: p[1] - 10, fill: '#f2c14e', 'font-size': 10, 'font-weight': 700 }, base.objs, String(list.length));
    });

    // the players: at a station around it, in quantum on his route
    const here = {};
    d.players.forEach(function (pl) { if (pl.state === 'at' && pl.at) (here[pl.at] = here[pl.at] || []).push(pl); });
    Object.keys(here).forEach(function (id) {
      const p = base.pos[id]; if (!p) return;
      here[id].forEach(function (pl, i) {
        const n = here[id].length, a = -Math.PI / 2 + (n > 1 ? i * 2 * Math.PI / n : 0), r = n > 1 ? 11 : 0;
        const x = p[0] + Math.cos(a) * r + (n > 1 ? 0 : 0), y = p[1] + Math.sin(a) * r + (n > 1 ? 0 : 12);
        const dot = el('circle', { cx: x, cy: y, r: 6, fill: pl.color, stroke: '#06090e', 'stroke-width': 1.5 }, base.people);
        el('title', {}, dot, pl.name + ' at ' + (base.names[id] || id));
      });
    });
    d.players.forEach(function (pl) {
      if (pl.state !== 'jump') return;
      const a = base.pos[pl.from], b = base.pos[pl.to];
      if (!a || !b) return;
      el('line', { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: pl.color, 'stroke-width': 2, 'stroke-dasharray': '2 5', 'stroke-linecap': 'round', 'stroke-opacity': 0.9 }, base.routes);
      const dot = el('circle', { cx: a[0], cy: a[1], r: 7, fill: pl.color, stroke: '#ecece8', 'stroke-width': 1.5 }, base.people);
      const label = el('text', { x: a[0] + 10, y: a[1] - 8, fill: pl.color, 'font-size': 11, 'font-weight': 700 }, base.people, pl.name);
      jumpers.push({ pl: pl, a: a, b: b, dot: dot, label: label });
    });

    // beside the map: every player and his speed meter, then the objectives
    const side = document.getElementById('players');
    side.textContent = '';
    if (d.players.length === 0) { const p = document.createElement('p'); p.className = 'muted'; p.textContent = 'Nobody is playing now.'; side.appendChild(p); }
    d.players.forEach(function (pl) {
      const card = document.createElement('div');
      card.className = 'mapplayer';
      card.style.setProperty('--c', pl.color);
      const who = document.createElement('div'); who.className = 'who'; who.textContent = pl.name; card.appendChild(who);
      const ship = document.createElement('div'); ship.className = 'what'; ship.textContent = [pl.ship, pl.drive].filter(function (x) { return x; }).join(' · '); card.appendChild(ship);
      const what = document.createElement('div'); what.className = 'what'; card.appendChild(what);
      if (pl.state === 'at') what.textContent = 'At ' + (base.names[pl.at] || pl.at) + (pl.since ? ' since ' + since(pl.since) : '');
      else if (pl.state === 'unknown') what.textContent = 'Where: not known yet (no quantum jump to a place the map knows)';
      if (pl.state === 'jump') {
        const meter = document.createElement('div'); meter.className = 'speed';
        const num = document.createElement('span'); num.className = 'num';
        const unit = document.createElement('span'); unit.className = 'unit';
        const bar = document.createElement('div'); bar.className = 'bar';
        const fill = document.createElement('span'); bar.appendChild(fill);
        meter.appendChild(num); meter.appendChild(unit); meter.appendChild(bar);
        card.appendChild(meter);
        const j = jumpers.find(function (x) { return x.pl === pl; });
        if (j) { j.what = what; j.num = num; j.unit = unit; j.fill = fill; }
        else what.textContent = 'In quantum to ' + (pl.toName || pl.to);
      }
      side.appendChild(card);
    });

    const objs = document.getElementById('objectives');
    objs.textContent = '';
    const list = document.createElement('ul'); list.className = 'objs';
    const lines = {};   // the same objective of a mission at the same place once, with how many
    d.objectives.forEach(function (o) {
      const text = o.title + ' · ' + (o.where || '') + ' (' + o.player + ')';
      lines[text] = (lines[text] || 0) + 1;
    });
    Object.keys(lines).forEach(function (text) {
      const li = document.createElement('li');
      li.textContent = text + (lines[text] > 1 ? ' ×' + lines[text] : '');
      list.appendChild(li);
    });
    if (d.objectives.length) objs.appendChild(list);
    const note = document.createElement('p'); note.className = 'hint';
    note.textContent = (d.objectives.length === 0 ? 'No objectives the map can place. ' : '') +
      (d.unplaced > 0 ? d.unplaced + (d.unplaced === 1 ? ' objective is' : ' objectives are') + ' on a planet or a moon, which the map cannot place yet.' : '');
    if (note.textContent) objs.appendChild(note);
    move();
  }

  // ------------------------------------------------------------------ every frame: the players in quantum move on
  function move() {
    const now = Date.now() + offset;
    jumpers.forEach(function (j) {
      const pl = j.pl, t = (now - pl.start) / 1000;
      const m = motionAt(pl.motion, pl.dist, t);
      const f = pl.dist > 0 ? m.x / pl.dist : 1;
      const x = j.a[0] + (j.b[0] - j.a[0]) * f, y = j.a[1] + (j.b[1] - j.a[1]) * f;
      j.dot.setAttribute('cx', x.toFixed(1)); j.dot.setAttribute('cy', y.toFixed(1));
      j.label.setAttribute('x', (x + 10).toFixed(1)); j.label.setAttribute('y', (y - 8).toFixed(1));
      if (!j.what) return;
      const to = pl.toName || pl.to, from = base.names[pl.from] || pl.from;
      j.what.textContent = t < 0 ? 'Spooling at ' + from + ' for ' + to + ' (in about ' + clock(-t) + ')'
        : m.left > 0 ? 'Quantum to ' + to + ' · ' + clock(m.left) + ' left' : 'Arriving at ' + to;
      j.num.textContent = (m.v / 1e6).toLocaleString(undefined, { maximumFractionDigits: 0 });
      j.unit.textContent = 'Mm/s · ' + (m.v / C).toFixed(2) + ' c · top ' + (m.p.top / 1e6).toLocaleString(undefined, { maximumFractionDigits: 0 }) + ' Mm/s';
      j.fill.style.width = (pl.motion.v > 0 ? Math.min(100, m.v / pl.motion.v * 100) : 0).toFixed(1) + '%';
    });
  }
  function loop() {
    if (jumpers.length && !document.hidden) move();
    requestAnimationFrame(loop);
  }

  async function refresh() {
    const state = document.getElementById('mapState');
    try {
      const res = await fetch('/map/map.json', { cache: 'no-store' });
      if (!res.ok) throw new Error('HTTP ' + res.status);
      const d = await res.json();
      offset = new Date(d.updated).getTime() - Date.now();   // the server's clock: the jumps start by it
      data = d;
      if (!base) build(d);
      render();
      state.textContent = 'Updated ' + new Date().toLocaleTimeString();
    } catch (e) {
      state.textContent = 'The map could not be updated: trying again.';
    }
  }
  refresh();
  setInterval(refresh, 3000);
  requestAnimationFrame(loop);
})();
