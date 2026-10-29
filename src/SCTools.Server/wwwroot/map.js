// The Map tool: a star system from above (Pyro, Stanton or Nyx, by the tabs over the map), everybody playing where his quantum jumps put him, and the
// objectives of the missions going on. map.json comes every few seconds; in between, a player in quantum moves along his route and his speed meter runs,
// worked out from his drive the same way the server does (QtDrive in the core: stage 1 acceleration up to the switch speed, stage 2 up to the drive speed,
// the same way down). The positions are the system's own (Gm, the star at 0, 0); the map has the system's Y to the right and its X down, as the Trip
// Planner's map of Pyro has it.
(function () {
  'use strict';
  const NS = 'http://www.w3.org/2000/svg';
  const W = 760, ROOM = 46, FOLD = 10, C = 299792458;
  const STAR = { pyro: '#ff3a18', stanton: '#ffc94a', nyx: '#ffa86b' };
  let data = null, offset = 0, base = null, movers = [];
  let shown = null, chosen = false;   // the system on the map; chosen: picked by a tab (or the address), not by where the players are

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

  // ------------------------------------------------------------------ the map of a system
  function el(tag, attrs, parent, text) {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (text !== undefined) e.textContent = text;
    (parent || base.svg).appendChild(e);
    return e;
  }
  function systemOf(id) { return data.systems.find(function (s) { return s.id === id; }); }
  function nameOfSystem(id) { const s = systemOf(id); return s ? s.name : id; }

  // A label beside a point, where it does not cover another label or a dot: right, left, under, over; without its second line when it does not fit with
  // it; else nowhere (the name is in the dot's tooltip), or (force) on the right anyway.
  function label(boxes, p, r, lines, force) {
    if (put(boxes, p, r, lines, false)) return true;
    if (lines.length > 1 && put(boxes, p, r, lines.slice(0, 1), false)) return true;
    return force ? put(boxes, p, r, lines.slice(0, 1), true) : false;
  }
  function put(boxes, p, r, lines, force) {
    const w = Math.max.apply(null, lines.map(function (l) { return l.text.length * l.size * 0.58; })) + 2;
    const h = lines.reduce(function (s, l) { return s + l.size * 1.2; }, 0);
    const spots = [
      { x: p[0] + r + 4, y: p[1] - h / 2, a: 'start' }, { x: p[0] - r - 4 - w, y: p[1] - h / 2, a: 'end' },
      { x: p[0] - w / 2, y: p[1] + r + 3, a: 'middle' }, { x: p[0] - w / 2, y: p[1] - r - 3 - h, a: 'middle' }];
    const fits = function (b) {
      if (b.x < 2 || b.y < 2 || b.x + b.w > W - 2 || b.y + b.h > W - 2) return false;
      return !boxes.some(function (o) { return b.x < o.x + o.w && o.x < b.x + b.w && b.y < o.y + o.h && o.y < b.y + b.h; });
    };
    let box = null;
    for (const s of spots) { const b = { x: s.x, y: s.y, w: w, h: h, a: s.a }; if (fits(b)) { box = b; break; } }
    if (!box && force) box = { x: Math.min(W - 2 - w, spots[0].x), y: spots[0].y, w: w, h: h, a: 'start' };
    if (!box) return false;
    boxes.push(box);
    const tx = box.a === 'start' ? box.x : box.a === 'end' ? box.x + w : box.x + w / 2;   // the text sits against the dot
    let y = box.y;
    lines.forEach(function (l) {
      y += l.size;
      el('text', { x: tx.toFixed(1), y: y.toFixed(1), 'text-anchor': box.a, fill: l.fill || '#ecece8', 'font-size': l.size, 'font-weight': l.weight || 400 }, base.labels, l.text);
      y += l.size * 0.2;
    });
    return true;
  }

  function build(id) {   // what does not change: the star, the orbits, the planets, the gateways, the stations
    const sys = systemOf(id) || data.systems[0];
    shown = sys.id;
    const box = document.getElementById('map');
    box.textContent = '';
    const places = sys.places, byKey = {};
    places.forEach(function (p) { byKey[p.key] = p; });
    const star = places.find(function (p) { return p.kind === 'star'; }) || { x: 0, y: 0, name: sys.name };
    const planets = places.filter(function (p) { return p.kind === 'planet' && !p.parent; });
    const orbit = function (q) { return Math.hypot(q.x - star.x, q.y - star.y); };
    // the scale: everything (the places and the orbits) as big as the square holds it, in the middle; across is the system's Y, down its X
    let x0 = Infinity, x1 = -Infinity, y0 = Infinity, y1 = -Infinity;
    const grow = function (across, down) { x0 = Math.min(x0, across); x1 = Math.max(x1, across); y0 = Math.min(y0, down); y1 = Math.max(y1, down); };
    places.forEach(function (p) { grow(p.y, p.x); });
    planets.forEach(function (q) { const r = orbit(q); grow(star.y - r, star.x - r); grow(star.y + r, star.x + r); });
    const sc = (W - 2 * ROOM) / Math.max(x1 - x0, y1 - y0, 1), mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
    const at = function (x, y) { return [W / 2 + (y - mx) * sc, W / 2 + (x - my) * sc]; };
    const cx = at(star.x, star.y)[0], cy = at(star.x, star.y)[1];
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + W);
    svg.setAttribute('width', '100%');
    svg.setAttribute('role', 'img');
    svg.setAttribute('aria-label', 'Map of ' + sys.name);
    box.appendChild(svg);
    base = { svg: svg, sys: sys.id, at: at, anchor: {}, names: {} };
    el('rect', { x: 0, y: 0, width: W, height: W, fill: '#06090e', rx: 6 });

    // the planets around the star; a moon, a city, a station at one of them (or this close on the map) is drawn as the planet
    const apart = function (a, b) { const p = at(a.x, a.y), q = at(b.x, b.y); return Math.hypot(p[0] - q[0], p[1] - q[1]); };
    const host = function (p) {   // its planet by the parents (a station at a moon at a planet), else the nearest planet
      let q = p, guard = 0;
      while (q && q.parent && guard++ < 5) { q = byKey[sys.id + '/' + q.parent]; if (q && q.kind === 'planet' && !q.parent) return q; }
      let best = null;
      planets.forEach(function (pl) { if (!best || apart(pl, p) < apart(best, p)) best = pl; });
      return best;
    };
    const folded = {};   // planet key -> the places drawn as it
    const own = [];
    places.forEach(function (p) {
      base.names[p.key] = p.name;
      if (p.kind === 'star') { base.anchor[p.key] = [cx, cy]; return; }
      if (p.kind === 'planet' && !p.parent) { base.anchor[p.key] = at(p.x, p.y); return; }
      const h = p.kind === 'gateway' ? null : host(p);
      if (h && apart(h, p) < FOLD) {
        base.anchor[p.key] = at(h.x, h.y);
        (folded[h.key] = folded[h.key] || []).push(p);
      } else {
        base.anchor[p.key] = at(p.x, p.y);
        own.push(p);
      }
    });

    planets.forEach(function (q) { el('circle', { cx: cx, cy: cy, r: (orbit(q) * sc).toFixed(1), fill: 'none', stroke: '#ebebf5', 'stroke-opacity': 0.22, 'stroke-width': 1 }); });
    const glow = STAR[sys.id] || '#ffd27a';
    el('circle', { cx: cx, cy: cy, r: 17, fill: glow, 'fill-opacity': 0.22 });
    el('circle', { cx: cx, cy: cy, r: 9, fill: glow });
    const boxes = [{ x: cx - 17, y: cy - 17, w: 34, h: 34 }];
    const dots = el('g', {});
    base.labels = el('g', {});
    planets.forEach(function (q) {
      const p = base.anchor[q.key], g = el('g', {}, dots);
      el('circle', { cx: p[0].toFixed(1), cy: p[1].toFixed(1), r: 6, fill: '#8c5514' }, g);
      const here = (folded[q.key] || []).map(function (f) { return f.name; });
      el('title', {}, g, q.name + (here.length ? '\nAlso here: ' + here.join(', ') : ''));
      boxes.push({ x: p[0] - 7, y: p[1] - 7, w: 14, h: 14 });
    });
    own.forEach(function (s) {
      const p = base.anchor[s.key], g = el('g', {}, dots);
      if (s.kind === 'gateway') el('circle', { cx: p[0].toFixed(1), cy: p[1].toFixed(1), r: 5, fill: '#06090e', stroke: '#7fd3ff', 'stroke-width': 2 }, g);
      else el('circle', { cx: p[0].toFixed(1), cy: p[1].toFixed(1), r: s.kind === 'moon' ? 3 : 3.5, fill: s.kind === 'moon' ? '#a98a63' : '#ecece8', 'fill-opacity': 0.85 }, g);
      el('title', {}, g, s.name);
      boxes.push({ x: p[0] - 5, y: p[1] - 5, w: 10, h: 10 });
    });
    // the names: the planets (with the cities and stations at them), the gateways, the stations, as long as there is room
    planets.forEach(function (q) {
      // the cities and stations at it, the ones in its orbit first (Lorville, Everus Harbor), then the others drawn as it (HUR-L1)
      const at2 = (folded[q.key] || []).filter(function (f) { return f.kind === 'city' || f.kind === 'station'; })
        .sort(function (a, b) { return (b.parent === q.key.split('/')[1]) - (a.parent === q.key.split('/')[1]); });
      const lines = [{ text: q.name, size: 13, weight: 700 }];
      if (at2.length) lines.push({ text: at2.slice(0, 2).map(function (f) { return f.short; }).join(' · ') + (at2.length > 2 ? ' …' : ''), size: 10, fill: '#a7adb8' });
      label(boxes, base.anchor[q.key], 7, lines, true);
    });
    own.filter(function (s) { return s.kind === 'gateway'; }).forEach(function (s) { label(boxes, base.anchor[s.key], 6, [{ text: s.short, size: 11, fill: '#7fd3ff' }], true); });
    own.filter(function (s) { return s.kind !== 'gateway'; }).forEach(function (s) { label(boxes, base.anchor[s.key], 5, [{ text: s.short, size: 11 }], false); });

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

  // A player on the map: an arrow in his colour (pointing where he flies; up when he is somewhere), a ring that pulses out from it so he is easy to spot,
  // and his name beside it in his colour (on the left near the right edge). place() puts it somewhere, turned that many degrees.
  function marker(pl, title) {
    const g = el('g', { 'class': 'player' }, base.people);
    el('circle', { r: 9, fill: pl.color, 'fill-opacity': 0.2, stroke: pl.color, 'stroke-width': 1.5 }, g);
    const pulse = el('circle', { r: 9, fill: 'none', stroke: pl.color, 'stroke-width': 2 }, g);
    el('animate', { attributeName: 'r', values: '9;19', dur: '1.8s', repeatCount: 'indefinite' }, pulse);
    el('animate', { attributeName: 'stroke-opacity', values: '0.8;0', dur: '1.8s', repeatCount: 'indefinite' }, pulse);
    const arrow = el('path', { d: 'M0,-9 L6.5,7 L0,3.5 L-6.5,7 Z', fill: pl.color, stroke: '#06090e', 'stroke-width': 1.5, 'stroke-linejoin': 'round' }, g);
    const name = el('text', { y: 4, fill: pl.color, 'font-size': 12, 'font-weight': 700, stroke: '#06090e', 'stroke-width': 3, 'paint-order': 'stroke', 'stroke-linejoin': 'round' }, g, pl.name);
    el('title', {}, g, title);
    return {
      place: function (x, y, deg) {
        g.setAttribute('transform', 'translate(' + x.toFixed(1) + ',' + y.toFixed(1) + ')');
        arrow.setAttribute('transform', 'rotate(' + (deg || 0).toFixed(0) + ')');
        const left = x > W - 24 - pl.name.length * 7.5;
        name.setAttribute('x', left ? -14 : 14);
        name.setAttribute('text-anchor', left ? 'end' : 'start');
      },
    };
  }

  function clock(seconds) {
    const s = Math.max(0, Math.round(seconds));
    return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
  }
  function since(iso) { return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); }

  // ------------------------------------------------------------------ the tabs: a system, and how many are playing in it
  function counts(d) {
    const n = {};
    d.systems.forEach(function (s) { n[s.id] = 0; });
    d.players.forEach(function (p) { if (p.system && n[p.system] !== undefined) n[p.system]++; });
    return n;
  }
  function show(id, picked) {
    if (picked) { chosen = true; try { history.replaceState(null, '', '#' + id); } catch (e) { /* only the address */ } }
    if (!base || base.sys !== id) build(id);
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
    base.objs.textContent = ''; base.routes.textContent = ''; base.people.textContent = '';
    movers = [];
    const here = function (sys) { return sys === base.sys; };

    // the objectives in this system: the ones at a place as a diamond beside it with how many, the ones in space where they are
    const atPlace = {};
    d.objectives.forEach(function (o) {
      if (!here(o.system)) return;
      if (o.place) {
        const a = base.anchor[o.place]; if (!a) return;
        const k = a.join(',');
        (atPlace[k] = atPlace[k] || { p: a, list: [] }).list.push(o);
      } else if (o.x !== undefined) { const p = base.at(o.x, o.y); diamond(base.objs, p[0], p[1], 5, o.title + ' (' + o.player + ')'); }
    });
    Object.keys(atPlace).forEach(function (k) {
      const p = atPlace[k].p, list = atPlace[k].list;
      diamond(base.objs, p[0] - 2, p[1] - 14, 5, list.map(function (o) { return o.title + ' · ' + o.where; }).join('\n'));
      if (list.length > 1) el('text', { x: p[0] + 5, y: p[1] - 11, fill: '#f2c14e', 'font-size': 10, 'font-weight': 700 }, base.objs, String(list.length));
    });

    // the players in this system: at a place under it (one under the other), on the way to a place the start of which is not known as a ring at it, in
    // quantum on his route
    const stay = {};
    d.players.forEach(function (pl) {
      if (!here(pl.system)) return;
      if (pl.state === 'at' && base.anchor[pl.at]) { const a = base.anchor[pl.at], k = a.join(','); (stay[k] = stay[k] || { p: a, list: [] }).list.push(pl); }
      if (pl.state === 'heading' && base.anchor[pl.to]) {
        const a = base.anchor[pl.to];
        const g = el('g', {}, base.routes);
        el('circle', { cx: a[0], cy: a[1], r: 10, fill: 'none', stroke: pl.color, 'stroke-width': 2, 'stroke-dasharray': '3 3' }, g);
        el('text', { x: a[0] - 14, y: a[1] - 10, 'text-anchor': 'end', fill: pl.color, 'font-size': 12, 'font-weight': 700, stroke: '#06090e', 'stroke-width': 3,
          'paint-order': 'stroke', 'stroke-linejoin': 'round' }, g, pl.name + ' →');
        el('title', {}, g, pl.name + ' on his way to ' + pl.toName);
      }
    });
    Object.keys(stay).forEach(function (k) {
      const p = stay[k].p;
      stay[k].list.forEach(function (pl, i) { marker(pl, pl.name + ' at ' + pl.atName).place(p[0], p[1] + 18 + i * 22, 0); });
    });
    d.players.forEach(function (pl) {
      if (pl.state !== 'jump') return;
      const m = { pl: pl };
      const a = here(pl.system) && base.anchor[pl.from], b = here(pl.system) && base.anchor[pl.to];
      if (a && b) {
        el('line', { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: pl.color, 'stroke-width': 2, 'stroke-dasharray': '2 5', 'stroke-linecap': 'round', 'stroke-opacity': 0.9 }, base.routes);
        m.a = a; m.b = b;
        m.deg = Math.atan2(b[1] - a[1], b[0] - a[0]) * 180 / Math.PI + 90;   // the arrow points up at 0 degrees
        m.mark = marker(pl, pl.name + ': ' + pl.fromName + ' to ' + pl.toName);
      }
      movers.push(m);
    });

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
      const what = document.createElement('div'); what.className = 'what'; card.appendChild(what);
      const of = pl.mission ? ' (' + pl.mission + ')' : '';   // at or to a mission's objective: which mission
      if (pl.state === 'at' || pl.state === 'mission') what.textContent = 'At ' + pl.atName + of + (pl.since ? ' since ' + since(pl.since) : '') + (pl.via ? ' (' + pl.via + ')' : '');
      else if (pl.state === 'heading') what.textContent = 'In quantum to ' + pl.toName + of + (pl.since ? (of ? ', since ' : ' (since ') + since(pl.since) + (of ? '' : ')') : '');
      else if (pl.state === 'unknown') what.textContent = pl.off
        ? 'Off the grid: out of comm-array range' + (pl.since ? ' since ' + since(pl.since) : '') + (pl.lastPing ? ' · last ping at ' + pl.lastPing : '')
        : 'No signal yet: waiting for the first comm-array ping (a quantum jump or an inventory)';
      if (pl.state === 'jump') {
        const meter = document.createElement('div'); meter.className = 'speed';
        const num = document.createElement('span'); num.className = 'num';
        const unit = document.createElement('span'); unit.className = 'unit';
        const bar = document.createElement('div'); bar.className = 'bar';
        const fill = document.createElement('span'); bar.appendChild(fill);
        meter.appendChild(num); meter.appendChild(unit); meter.appendChild(bar);
        card.appendChild(meter);
        const m = movers.find(function (x) { return x.pl === pl; });
        m.what = what; m.num = num; m.unit = unit; m.fill = fill;
      }
      side.appendChild(card);
    });

    const objs = document.getElementById('objectives');
    objs.textContent = '';
    const list = document.createElement('ul'); list.className = 'objs';
    const lines = {};   // the same objective of a mission at the same place once, with how many
    d.objectives.forEach(function (o) {
      const text = o.title + ' · ' + (o.where || '') + (o.system && !here(o.system) ? ' · ' + nameOfSystem(o.system) : '') + ' (' + o.player + ')';
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
    move();
  }

  // ------------------------------------------------------------------ every frame: the players in quantum move on
  function move() {
    const now = Date.now() + offset;
    movers.forEach(function (m) {
      const pl = m.pl, t = (now - pl.start) / 1000;
      const q = motionAt(pl.motion, pl.dist, t);
      if (m.mark) {
        const f = pl.dist > 0 ? q.x / pl.dist : 1;
        m.mark.place(m.a[0] + (m.b[0] - m.a[0]) * f, m.a[1] + (m.b[1] - m.a[1]) * f, m.deg);
      }
      if (m.what) speedText(m, t, q);
    });
  }
  function speedText(m, t, q) {   // his card: what he does, how fast he goes
    const pl = m.pl;
    m.what.textContent = t < 0 ? 'Spooling at ' + pl.fromName + ' for ' + pl.toName + ' (in about ' + clock(-t) + ')'
      : q.left > 0 ? 'Quantum to ' + pl.toName + ' · ' + clock(q.left) + ' left' : 'Arriving at ' + pl.toName;
    m.num.textContent = (q.v / 1e6).toLocaleString(undefined, { maximumFractionDigits: 0 });
    m.unit.textContent = 'Mm/s · ' + (q.v / C).toFixed(2) + ' c · top ' + (q.p.top / 1e6).toLocaleString(undefined, { maximumFractionDigits: 0 }) + ' Mm/s';
    m.fill.style.width = (pl.motion.v > 0 ? Math.min(100, q.v / pl.motion.v * 100) : 0).toFixed(1) + '%';
  }
  function loop() {
    if (movers.length && !document.hidden) move();
    requestAnimationFrame(loop);
  }

  async function refresh() {
    if (document.hidden) return;   // nobody is looking: the map polls again when the tab is shown
    const state = document.getElementById('mapState');
    try {
      const res = await fetch('/map/map.json', { cache: 'no-store' });
      if (!res.ok) throw new Error('HTTP ' + res.status);
      const d = await res.json();
      offset = new Date(d.updated).getTime() - Date.now();   // the server's clock: the jumps start by it
      data = d;
      show(choose(d), false);
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
  requestAnimationFrame(loop);
})();
