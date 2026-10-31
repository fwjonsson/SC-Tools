// SystemMap: the one map engine of the site. The Map tool (/map, map.js) and the Hauling Planner's Trip Planner tab (app.js) draw a star system with it:
// the system from above (the star, the orbits, the planets with the moons, cities and stations at them, the gateways, the stations), the objectives of the
// missions going on as diamonds (with how many, when several are at one place), and everybody playing where his quantum jumps put him: an arrow in his
// colour with a ring pulsing out from it, moving along his route while he is in quantum, worked out from his drive the same way the server does (QtDrive in
// the core: stage 1 acceleration up to the switch speed, stage 2 up to the drive speed, the same way down), and a speed meter under his name. The positions
// are the system's own (Gm, the star at 0, 0); the map has the system's Y to the right and its X down. The data is the Map tool's map.json (the server's
// MapService.Snapshot: systems with their places, players, objectives); the planner gets the Pyro slice of it (/hauling/map.json). A plain script, no
// modules: WebAssets inlines it into the live page before app.js, and the server sends it to the Map tool as /systemmap.js.
var SystemMap = (function () {
  'use strict';
  const NS = 'http://www.w3.org/2000/svg';
  const W = 760, ROOM = 46, FOLD = 10, C = 299792458;
  const STAR = { pyro: '#ff3a18', stanton: '#ffc94a', nyx: '#ffa86b' };
  const INK = '#06090e', PAPER = '#ecece8', GOLD = '#f2c14e';

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

  function clock(seconds) {
    const s = Math.max(0, Math.round(seconds));
    return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
  }
  function since(iso) { return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); }
  function same(a, b) { return String(a || '').toLowerCase() === String(b || '').toLowerCase(); }

  // What a player does, in words, for his card or the line under his name. A player in quantum gets his line from the meter every frame instead.
  function describe(pl) {
    const of = pl.mission ? ' (' + pl.mission + ')' : '';   // at or to a mission's objective: which mission
    // landed in the station's hangar, or taking off (out of the hangar, still in the station's area)
    const at = pl.hangar === 'landed' ? 'Landed at ' : pl.hangar === 'takeoff' ? 'Taking off from ' : 'At ';
    if (pl.state === 'at' || pl.state === 'mission') return at + pl.atName + of + (pl.since ? ' since ' + since(pl.since) : '') + (pl.via ? ' (' + pl.via + ')' : '')
      + (pl.leaving ? ', leaving for ' + pl.leaving : '');   // he picked a jump at the station: it starts when he leaves its area
    if (pl.state === 'heading') return 'In quantum to ' + pl.toName + of + (pl.since ? (of ? ', since ' : ' (since ') + since(pl.since) + (of ? '' : ')') : '');
    if (pl.state === 'unknown') return pl.off
      ? 'Off the grid: out of comm-array range' + (pl.since ? ' since ' + since(pl.since) : '') + (pl.lastPing ? ' · last ping at ' + pl.lastPing : '')
      : 'No signal yet: waiting for the first comm-array ping (a quantum jump or an inventory)';
    if (pl.state === 'jump') return 'Quantum to ' + pl.toName;
    return '';
  }

  // A map in a box: show(system) draws the system (what does not change), draw(data) the objectives and the players of the data (map.json) on it, and
  // meter(name) makes the line and the speed meter of a player, kept up to date from the data drawn last and every frame while he is in quantum. The caller
  // draws his own things (the planner's routes) in layer(), between the system and the objectives, at the anchors at(key) gives him. options.active: a
  // function that says whether the map is on screen (the players only move while it is).
  function create(box, options) {
    const opts = options || {};
    let base = null, data = null, offset = 0, movers = [];
    const meters = {};   // a player's name (lower case) -> his line and speed meter

    function el(tag, attrs, parent, text) {
      const e = document.createElementNS(NS, tag);
      for (const k in attrs) e.setAttribute(k, attrs[k]);
      if (text !== undefined) e.textContent = text;
      (parent || base.svg).appendChild(e);
      return e;
    }

    // A label beside a point, where it does not cover another label or a dot: right, left, under, over; without its second line when it does not fit
    // with it; else nowhere (the name is in the dot's tooltip), or (force) on the right anyway.
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
      let place = null;
      for (const s of spots) { const b = { x: s.x, y: s.y, w: w, h: h, a: s.a }; if (fits(b)) { place = b; break; } }
      if (!place && force) place = { x: Math.min(W - 2 - w, spots[0].x), y: spots[0].y, w: w, h: h, a: 'start' };
      if (!place) return false;
      boxes.push(place);
      const tx = place.a === 'start' ? place.x : place.a === 'end' ? place.x + w : place.x + w / 2;   // the text sits against the dot
      let y = place.y;
      lines.forEach(function (l) {
        y += l.size;
        el('text', { x: tx.toFixed(1), y: y.toFixed(1), 'text-anchor': place.a, fill: l.fill || PAPER, 'font-size': l.size, 'font-weight': l.weight || 400 }, base.labels, l.text);
        y += l.size * 0.2;
      });
      return true;
    }

    function build(sys, sig) {   // what does not change: the star, the orbits, the planets, the gateways, the stations
      box.textContent = '';
      const places = sys.places, byKey = {};
      places.forEach(function (p) { byKey[p.key] = p; });
      const star = places.find(function (p) { return p.kind === 'star'; }) || { x: 0, y: 0, name: sys.name };
      const planets = places.filter(function (p) { return p.kind === 'planet' && !p.parent; });
      const orbit = function (q) { return Math.hypot(q.x - star.x, q.y - star.y); };
      // the scale: everything (the places and the orbits) as big as the square holds it, in the middle; across is the system's Y, down its X
      let x0 = Infinity, x1 = -Infinity, y0 = Infinity, y1 = -Infinity;
      const grow = function (across, down) { x0 = Math.min(x0, across); x1 = Math.max(x1, across); y0 = Math.min(y0, down); y1 = Math.max(y1, down); };
      places.forEach(function (p) { if (!p.hidden) grow(p.y, p.x); });   // a hidden spot does not change the scale
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
      base = { svg: svg, sys: sys.id, sig: sig, at: at, anchor: {}, names: {} };
      el('rect', { x: 0, y: 0, width: W, height: W, fill: INK, rx: 6 });

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
        // a hidden spot (a mission's beacon placed by its fuel estimate, where a player is or flies to): where it is, and nothing drawn for it
        if (p.hidden) { base.anchor[p.key] = at(p.x, p.y); return; }
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
        if (s.kind === 'gateway') el('circle', { cx: p[0].toFixed(1), cy: p[1].toFixed(1), r: 5, fill: INK, stroke: '#7fd3ff', 'stroke-width': 2 }, g);
        else el('circle', { cx: p[0].toFixed(1), cy: p[1].toFixed(1), r: s.kind === 'moon' ? 3 : 3.5, fill: s.kind === 'moon' ? '#a98a63' : PAPER, 'fill-opacity': 0.85 }, g);
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

      base.layer = el('g', { 'class': 'layer' });   // the caller's: the planner's routes
      base.svg.appendChild(base.labels);   // the names of the places above the routes (Frank, 30 Oct 2026), under the players and the objectives
      base.objs = el('g', {});
      base.routes = el('g', {});
      base.people = el('g', {});
    }

    function diamond(parent, x, y, size, title) {
      const g = el('g', {}, parent);
      el('polygon', { points: [[x, y - size], [x + size, y], [x, y + size], [x - size, y]].map(function (p) { return p.join(','); }).join(' '), fill: GOLD, stroke: INK, 'stroke-width': 1 }, g);
      if (title) el('title', {}, g, title);
      return g;
    }

    // A player on the map: an arrow in his colour (pointing where he flies; up when he is somewhere), a ring that pulses out from it so he is easy to spot,
    // and his name beside it in his colour (on the left near the right edge). place() puts it somewhere, turned that many degrees. Faint: off the grid, at
    // his last ping (half seen, so it does not pass for where he is).
    function marker(pl, title, faint) {
      const g = el('g', faint ? { 'class': 'player', opacity: 0.45 } : { 'class': 'player' }, base.people);
      el('circle', { r: 9, fill: pl.color, 'fill-opacity': 0.2, stroke: pl.color, 'stroke-width': 1.5 }, g);
      const pulse = el('circle', { r: 9, fill: 'none', stroke: pl.color, 'stroke-width': 2 }, g);
      el('animate', { attributeName: 'r', values: '9;19', dur: '1.8s', repeatCount: 'indefinite' }, pulse);
      el('animate', { attributeName: 'stroke-opacity', values: '0.8;0', dur: '1.8s', repeatCount: 'indefinite' }, pulse);
      const arrow = el('path', { d: 'M0,-9 L6.5,7 L0,3.5 L-6.5,7 Z', fill: pl.color, stroke: INK, 'stroke-width': 1.5, 'stroke-linejoin': 'round' }, g);
      const name = el('text', { y: 4, fill: pl.color, 'font-size': 12, 'font-weight': 700, stroke: INK, 'stroke-width': 3, 'paint-order': 'stroke', 'stroke-linejoin': 'round' }, g, pl.name);
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

    // The system on the map: drawn anew when it is another one (or its places changed); true when it was.
    function show(sys) {
      const sig = sys.id + '|' + JSON.stringify(sys.places);
      if (base && base.sig === sig) return false;
      build(sys, sig);
      return true;
    }

    // The objectives and the players of the data (map.json) in the system shown; keep(name): only the players (and the objectives) of these. The
    // objectives at a place are a diamond beside it with how many, the ones in space where they are. A player at a place is under it (several: one under
    // the other), one on the way to a place from a start the map does not know is a ring at it, one in quantum moves along his route.
    function draw(d, keep) {
      data = d;
      // the server's clock: the jumps start by it. Taken when the data came (clockOffset): data drawn again a few seconds later (a tab or a player clicked)
      // measured against the time of the drawing put every player in quantum a few seconds back on his route (Frank, 30 Oct 2026: the clocks jumped)
      offset = d.clockOffset !== undefined ? d.clockOffset : d.updated ? new Date(d.updated).getTime() - Date.now() : 0;
      movers = [];
      if (base) {
        base.objs.textContent = ''; base.routes.textContent = ''; base.people.textContent = '';
        const ok = function (name) { return !keep || keep(name); };
        const here = function (sys) { return sys === base.sys; };

        const atPlace = {};
        (d.objectives || []).forEach(function (o) {
          if (!here(o.system) || !(o.who || [o.player]).some(ok)) return;
          if (o.place) {
            const a = base.anchor[o.place]; if (!a) return;
            const k = a.join(',');
            (atPlace[k] = atPlace[k] || { p: a, list: [] }).list.push(o);
          } else if (o.x !== undefined && o.x !== null) { const p = base.at(o.x, o.y); diamond(base.objs, p[0], p[1], 5, o.title + ' (' + o.player + ')'); }
        });
        Object.keys(atPlace).forEach(function (k) {
          const p = atPlace[k].p, list = atPlace[k].list;
          diamond(base.objs, p[0] - 2, p[1] - 14, 5, list.map(function (o) { return o.title + ' · ' + o.where; }).join('\n'));
          if (list.length > 1) el('text', { x: p[0] + 5, y: p[1] - 11, fill: GOLD, 'font-size': 10, 'font-weight': 700 }, base.objs, String(list.length));
        });

        const stay = {};
        (d.players || []).forEach(function (pl) {
          if (!ok(pl.name)) return;
          if (here(pl.system) && pl.state === 'at' && base.anchor[pl.at]) { const a = base.anchor[pl.at], k = a.join(','); (stay[k] = stay[k] || { p: a, list: [] }).list.push(pl); }
          // off the grid: his marker stays at his last ping, faded (Frank, 31 Oct 2026: a rough overview beats no marker)
          if (pl.state === 'unknown' && pl.off && pl.lastPingAt && base.anchor[pl.lastPingAt]) {
            const a = base.anchor[pl.lastPingAt], k = a.join(',');
            (stay[k] = stay[k] || { p: a, list: [] }).list.push(pl);
          }
          if (here(pl.system) && pl.state === 'heading' && base.anchor[pl.to]) {
            const a = base.anchor[pl.to];
            const g = el('g', {}, base.routes);
            el('circle', { cx: a[0], cy: a[1], r: 10, fill: 'none', stroke: pl.color, 'stroke-width': 2, 'stroke-dasharray': '3 3' }, g);
            el('text', { x: a[0] - 14, y: a[1] - 10, 'text-anchor': 'end', fill: pl.color, 'font-size': 12, 'font-weight': 700, stroke: INK, 'stroke-width': 3,
              'paint-order': 'stroke', 'stroke-linejoin': 'round' }, g, pl.name + ' →');
            el('title', {}, g, pl.name + ' on his way to ' + pl.toName);
          }
          if (pl.state === 'jump') {
            const m = { pl: pl };
            const a = here(pl.system) && base.anchor[pl.from], b = here(pl.system) && base.anchor[pl.to];
            if (a && b) {
              el('line', { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: pl.color, 'stroke-width': 2, 'stroke-dasharray': '2 5', 'stroke-linecap': 'round', 'stroke-opacity': 0.9 }, base.routes);
              m.a = a; m.b = b;
              m.deg = Math.atan2(b[1] - a[1], b[0] - a[0]) * 180 / Math.PI + 90;   // the arrow points up at 0 degrees
              m.mark = marker(pl, pl.name + ': ' + pl.fromName + ' to ' + pl.toName);
            }
            movers.push(m);
          }
        });
        Object.keys(stay).forEach(function (k) {
          const p = stay[k].p;
          stay[k].list.forEach(function (pl, i) {
            const off = pl.state === 'unknown';
            marker(pl, off ? pl.name + ': off the grid, last ping at ' + pl.lastPing : pl.name + ' at ' + pl.atName, off).place(p[0], p[1] + 18 + i * 22, 0);
          });
        });
      }
      Object.keys(meters).forEach(function (key) { if (meters[key].what.isConnected) bind(meters[key]); else delete meters[key]; });
      move();
      wake();   // somebody in quantum: the loop moves him from now on
    }

    // The line and the speed meter of a player, by his name: what he does (describe) and, while he is in quantum, his speed every frame (Mm/s, in c, against
    // his drive's top speed) and how long is left. Made once and kept up to date from every draw(); a player the data does not have is not playing. The
    // meter stays when he is not in quantum, at 0 Mm/s (Frank, 29 Oct 2026): a card keeps its size instead of jumping when a jump starts or ends.
    function meter(name) {
      const wrap = document.createElement('div'); wrap.className = 'mapmeter';
      const what = document.createElement('div'); what.className = 'what'; wrap.appendChild(what);
      const speed = document.createElement('div'); speed.className = 'speed';
      const num = document.createElement('span'); num.className = 'num';
      const unit = document.createElement('span'); unit.className = 'unit';
      const bar = document.createElement('div'); bar.className = 'bar';
      const fill = document.createElement('span'); bar.appendChild(fill);
      speed.appendChild(num); speed.appendChild(unit); speed.appendChild(bar);
      wrap.appendChild(speed);
      const m = { name: name, what: what, speed: speed, num: num, unit: unit, fill: fill };
      meters[String(name).toLowerCase()] = m;
      bind(m);
      return wrap;
    }
    function bind(m) {
      const pl = data ? (data.players || []).find(function (p) { return same(p.name, m.name); }) : null;
      m.what.textContent = pl ? describe(pl) : data ? 'Not playing now (no game with a tracker running).' : '';
      if (!pl || pl.state !== 'jump') {   // standing still: the meter at 0, with his drive's top speed when the data has it
        const top = pl && pl.motion && pl.motion.v > 0 ? pl.motion.v : pl && pl.top > 0 ? pl.top : 0;   // his drive's top speed, standing still too (Frank, 30 Oct 2026)
        m.num.textContent = '0';
        m.unit.textContent = 'Mm/s · 0.00 c' + (top > 0 ? ' · top ' + (top / 1e6).toLocaleString(undefined, { maximumFractionDigits: 0 }) + ' Mm/s' : '');
        m.fill.style.width = '0%';
        return;
      }
      const mv = movers.find(function (x) { return x.pl === pl; });
      if (!mv) return;
      mv.what = m.what; mv.num = m.num; mv.unit = m.unit; mv.fill = m.fill;
      const t = (Date.now() + offset - pl.start) / 1000;
      speedText(mv, t, motionAt(pl.motion, pl.dist, t));
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
    function speedText(m, t, q) {   // his line: what he does, how fast he goes
      const pl = m.pl;
      m.what.textContent = t < 0 ? 'Spooling at ' + pl.fromName + ' for ' + pl.toName + ' (in about ' + clock(-t) + ')'
        : q.left > 0 ? 'Quantum to ' + pl.toName + ' · ' + clock(q.left) + ' left' : 'Arriving at ' + pl.toName;
      m.num.textContent = (q.v / 1e6).toLocaleString(undefined, { maximumFractionDigits: 0 });
      m.unit.textContent = 'Mm/s · ' + (q.v / C).toFixed(2) + ' c · top ' + (q.p.top / 1e6).toLocaleString(undefined, { maximumFractionDigits: 0 }) + ' Mm/s';
      m.fill.style.width = (pl.motion.v > 0 ? Math.min(100, q.v / pl.motion.v * 100) : 0).toFixed(1) + '%';
    }
    // The loop runs only while somebody is in quantum on a map that is shown: with nobody to move it rests (a loop that runs for nothing keeps the browser
    // drawing 60 times a second), and every draw (every few seconds, and when the map is shown) wakes it again when there is somebody.
    let wasOnPage = false, running = false;
    function loop() {
      if (box.isConnected) wasOnPage = true;
      else if (wasOnPage) { running = false; return; }   // the box was taken off the page (the Trip Planner tab was drawn anew): this map is done
      if (movers.length && !document.hidden && (!opts.active || opts.active())) {
        move();
        requestAnimationFrame(loop);
      } else running = false;
    }
    function wake() { if (!running && movers.length) { running = true; requestAnimationFrame(loop); } }

    return {
      show: show,
      draw: draw,
      meter: meter,
      el: el,
      system: function () { return base ? base.sys : null; },
      layer: function () { return base ? base.layer : null; },
      at: function (key) { return base ? base.anchor[key] : undefined; },
      nameOf: function (key) { return base && base.names[key] || key; },
    };
  }

  return { create: create, motionAt: motionAt, clock: clock, since: since, describe: describe, W: W };
})();
