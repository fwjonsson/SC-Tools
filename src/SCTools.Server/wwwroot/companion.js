// The Companion (#380): your own page to keep open while you play, started as a copy of the Map tool (map.js). The map is SystemMap (systemmap.js, the one
// map engine of the site) with you and your party; while it shows the system you are in, it goes with you when you jump to another one. Beside it the
// players with their speed meters (the admin's own view has everybody on the map, and the lists keep to his party: data.party), your game session (session.json: the Session tool's figures for the session going on, or the last one) and the
// objectives; under it your party's Active Contracts (map.json's missions) and the contracts it ended this session (Completed Contracts). The admin gets the
// Mission Library's fields in the contracts' rows, sent in the background to Edit Mission Info's address (autosave.js) as soon as one changes.
(function () {
  'use strict';
  let data = null, sess = null, sessTag = null;
  let shown = null, chosen = false;   // the system on the map; chosen: picked by a tab (or the address), not by where you are
  let mine = null;                    // the system you were in when the data came last: the map follows you from it
  const map = SystemMap.create(document.getElementById('map'));
  const tokenField = document.querySelector('input[name="__RequestVerificationToken"]');
  const editor = !!document.querySelector('.companion[data-editor]') && !!tokenField;
  const made = {};   // what each table was drawn from last: drawn again only when that changed

  function systemOf(id) { return data.systems.find(function (s) { return s.id === id; }); }
  function nameOfSystem(id) { const s = systemOf(id); return s ? s.name : id; }
  function same(a, b) { return String(a || '').toLowerCase() === String(b || '').toLowerCase(); }
  function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined) e.textContent = text; return e; }
  function muted(text) { return el('p', 'muted', text); }
  function hhmm(iso) { return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); }

  // ------------------------------------------------------------------ the tabs: a system, and how many of your party are in it
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
  // The system you are in (null: you are not playing, or the map does not know).
  function mySystem(d) {
    const me = d.me && d.players.find(function (p) { return same(p.name, d.me); });
    return me && me.system ? me.system : null;
  }
  // The system to show: the one picked (and when it was the one you were in and you jumped to another system, that one: #380); else the one you are in;
  // else where most of your party is.
  function choose(d) {
    const valid = function (id) { return d.systems.some(function (s) { return s.id === id; }); };
    if (shown === null) {
      const h = location.hash.replace('#', '').toLowerCase();
      if (valid(h)) { shown = h; chosen = true; }
    }
    const now = mySystem(d);
    const follow = mine !== null && now !== null && now !== mine && shown === mine;
    if (now !== null) mine = now;
    if (follow) {
      if (chosen) { try { history.replaceState(null, '', '#' + now); } catch (e) { /* only the address */ } }
      return now;
    }
    if (chosen && valid(shown)) return shown;
    if (now !== null) return now;
    const n = counts(d);
    let best = d.systems[0].id;
    d.systems.forEach(function (s) { if (n[s.id] > n[best]) best = s.id; });
    if (shown === null || !valid(shown) || (n[shown] === 0 && n[best] > 0)) return best;
    return shown;
  }

  // ------------------------------------------------------------------ the map, the players and the objectives
  // Yours to list: everybody the data has, or, when the map has everybody (the admin's own view), the ones of your party.
  function ours(name) { return !data.party || data.party.some(function (n) { return same(n, name); }); }

  function render() {
    const d = data;
    tabs(d);
    map.draw(d);
    const here = function (sys) { return sys === map.system(); };
    const players = d.players.filter(function (pl) { return ours(pl.name); });
    const objectives = d.objectives.filter(function (o) { return (o.who || [o.player]).some(ours); });

    const side = document.getElementById('players');
    side.textContent = '';
    if (players.length === 0) side.appendChild(muted(d.me ? 'Nobody of your party is playing now.' : 'Log in to see yourself and your party here.'));
    players.forEach(function (pl) {
      const card = el('div', 'mapplayer');
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
      const who = el('div', 'who', pl.name);
      if (pl.system) who.appendChild(el('span', 'sys', nameOfSystem(pl.system)));
      card.appendChild(who);
      card.appendChild(el('div', 'what', [pl.ship, pl.drive].filter(function (x) { return x; }).join(' · ')));
      card.appendChild(map.meter(pl.name));   // what he does, and his speed while he is in quantum
      side.appendChild(card);
    });

    const objs = document.getElementById('objectives');
    objs.textContent = '';
    const list = el('ul', 'objs');
    const lines = {};   // the same place with the same people once, with how many objectives; no mission names (the Active Contracts table has them)
    objectives.forEach(function (o) {
      const text = (o.where || '') + (o.system && !here(o.system) ? ' · ' + nameOfSystem(o.system) : '') + ' (' + (o.who || [o.player]).join(', ') + ')';
      lines[text] = (lines[text] || 0) + 1;
    });
    Object.keys(lines).forEach(function (text) { list.appendChild(el('li', '', text + (lines[text] > 1 ? ' ×' + lines[text] : ''))); });
    if (objectives.length) objs.appendChild(list);
    const note = el('p', 'hint');
    note.textContent = (objectives.length === 0 ? 'No objectives going on. ' : '') +
      (d.unplaced > 0 && !d.party ? (d.unplaced === 1 ? 'One objective is' : d.unplaced + ' objectives are') + ' only in the list: the game does not say where ' +
        (d.unplaced === 1 ? 'it is.' : 'they are.') : '');
    if (note.textContent) objs.appendChild(note);

    contracts('missions', d.missions || [], false, 'No contracts going on.');
  }

  // ------------------------------------------------------------------ the contract tables
  // A field of the Mission Library, as on Edit Mission Info: a little form of its own that autosave.js sends in the background when it changes.
  function field(edit, name, value, hint, cls) {
    const form = el('form');
    form.method = 'post';
    form.action = '/admin/missions/set';
    form.setAttribute('data-autosave', 'quiet');
    [['__RequestVerificationToken', tokenField.value], ['id', edit.key], ['field', name]].forEach(function (h) {
      const i = el('input'); i.type = 'hidden'; i.name = h[0]; i.value = h[1]; form.appendChild(i);
    });
    const input = el('input', cls);
    input.name = 'value';
    input.value = value || '';
    input.placeholder = hint || '';
    input.autocomplete = 'off';
    input.setAttribute('aria-label', name === 'giver' ? 'faction' : name);
    if (name === 'giver') input.setAttribute('list', 'factions');
    form.appendChild(input);
    form.addEventListener('submit', function (e) { e.preventDefault(); input.blur(); });   // Enter saves it (the change), the page stays
    return form;
  }

  // Active Contracts (ended: false) or Completed Contracts (ended: true): the same columns, but how it ended and when in place of the open objectives.
  function contracts(id, rows, ended, none) {
    const box = document.getElementById(id);
    if (!box) return;   // a visitor: no tables
    const sig = JSON.stringify(rows);
    if (made[id] === sig) return;
    if (box.contains(document.activeElement) && document.activeElement.tagName === 'INPUT') return;   // not while the admin types in it: again after
    made[id] = sig;
    box.textContent = '';
    if (rows.length === 0) { box.appendChild(muted(none)); return; }
    const edits = editor && rows.some(function (m) { return m.edit; });
    const t = el('table', 'missions' + (edits ? '' : ' view'));
    const hr = t.createTHead().insertRow();
    [['#', ''], ['Mission', ''], ['Who', ''], [ended ? 'Ended' : 'Open', ended ? '' : 'right'], ['Faction', ''], ['Category', ''], ['SCU', 'right'], ['Reward', 'right'],
      ['MG scrip', 'right'], ['Council scrip', 'right'], ['XP', 'right'], ['Legality', '']]
      .forEach(function (h) { const th = el('th', h[1], h[0]); hr.appendChild(th); });
    const tb = t.createTBody();
    rows.forEach(function (m, n) {
      const tr = tb.insertRow();
      if (ended) tr.className = m.outcome === 'Completed' ? '' : 'failed';
      const e = editor ? m.edit : null;
      const cells = [
        [String(n + 1)],
        [m.title, 'name', e && ['name', e.name, e.nameHint, 'name']],
        [m.who.join(', ')],
        [ended ? m.outcome + ' ' + hhmm(m.ended) : String(m.open), ended ? 'nowrap' : 'right'],
        [m.faction, '', e && ['giver', e.giver, e.giverHint, 'giver']],
        [m.category],
        [m.scu, 'right', e && ['scu', e.scu, e.scuHint, 'num scu']],
        [m.reward, 'right', e && ['auec', e.auec, e.auecHint, 'num']],
        [m.mg, 'right', e && ['mg', e.mg, '', 'num scrip']],
        [m.council, 'right', e && ['council', e.council, '', 'num scrip']],
        [m.xp, 'right'],
        [m.legality],
      ];
      cells.forEach(function (c) {
        const td = tr.insertCell();
        if (c[1]) td.className = c[1];
        if (c[2]) td.appendChild(field(e, c[2][0], c[2][1], c[2][2], c[2][3]));
        else td.textContent = c[0];
      });
    });
    const scroll = el('div', 'scroll'); scroll.appendChild(t); box.appendChild(scroll);
  }

  // ------------------------------------------------------------------ the game session
  function clock(seconds) {
    const s = Math.max(0, Math.floor(seconds)), h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60;
    return h + ':' + String(m).padStart(2, '0') + ':' + String(s % 60).padStart(2, '0');
  }
  function money(v) { return Math.round(v).toLocaleString('en-US'); }
  // The time played in the session: the games that ended, and the game going on counted on from its start by this browser's clock (the clock rule).
  function played(s) { return s.playedBefore + (s.running ? Math.max(0, (Date.now() - Date.parse(s.running)) / 1000) : 0); }

  function renderSession() {
    const box = document.getElementById('session');
    if (!box || !sess) return;
    box.textContent = '';
    const s = sess.session;
    if (!s) {
      box.appendChild(muted('No games yet: your session starts when you start the game (with the SC Tools Tracker running).'));
    } else {
      const state = el('p');
      if (s.running) { state.appendChild(el('span', 'dot')); state.appendChild(document.createTextNode('Playing since ' + hhmm(s.start) + '.')); }
      else if (!s.over) state.textContent = 'Not playing: the game ended ' + hhmm(s.lastEnd) + '. The session goes on if you start it again within the hour.';
      else state.textContent = 'Ended: your last session, ' + hhmm(s.start) + ' to ' + hhmm(s.lastEnd) + ' (' + new Date(s.start).toLocaleDateString() + ').';
      const now = el('div', 'playnow'); now.appendChild(state); box.appendChild(now);
      const t = el('table', 'play session');
      const body = t.createTBody();
      const line = function (name, value, cls) {
        const tr = body.insertRow(); if (cls) tr.className = cls;
        tr.appendChild(el('th', '', name));
        const td = tr.insertCell(); td.className = 'right'; td.textContent = value;
        return td;
      };
      const time = line('Time played', clock(played(s)));
      time.classList.add('clock');
      line('Crashes', String(s.crashes));
      line('Missions completed', String(s.completed));
      line('aUEC', money(s.auec));
      line('MG scrip', money(s.mg));
      line('Council scrip', money(s.council));
      line('Earned (scrip counted in)', money(s.value), 'sum');
      const hour = line('Per hour', '');
      hour.classList.add('perhour');
      box.appendChild(t);
      tick();
      const hint = el('p', 'hint');
      hint.textContent = 'Your own play since the game started (a game started again within an hour of the last one is the same session). Earned is what the ' +
        'Mission Library says your completed missions pay, with MG scrip at ' + money(s.mgValue) + ' aUEC and Council scrip at ' + money(s.councilValue) + ' aUEC.' +
        (s.withoutReward > 0 ? ' ' + s.withoutReward + (s.withoutReward === 1 ? ' completed mission has' : ' completed missions have') + ' no reward in the library yet.' : '');
      box.appendChild(hint);
    }
    contracts('completed', sess.completed || [], true, 'No contracts ended this session.');
  }
  // Every second: the time played and the per hour of a game going on.
  function tick() {
    const s = sess && sess.session;
    if (!s) return;
    const secs = played(s);
    const time = document.querySelector('#session .clock'), hour = document.querySelector('#session .perhour');
    if (time) time.textContent = clock(secs);
    if (hour) hour.textContent = secs >= 60 ? money(s.value / (secs / 3600)) : '';
  }

  // ------------------------------------------------------------------ the polls
  async function refresh() {
    if (document.hidden) return;   // nobody is looking: the page polls again when the tab is shown
    const state = document.getElementById('mapState');
    try {
      const res = await fetch('/companion/map.json', { cache: 'no-store' });
      if (!res.ok) throw new Error('HTTP ' + res.status);
      data = await res.json();
      data.clockOffset = Date.parse(data.updated) - Date.now();   // the server's clock against this browser's, when the data came (not when it is drawn)
      show(choose(data), false);
      state.textContent = 'Updated ' + new Date().toLocaleTimeString();
    } catch (e) {
      state.textContent = 'The map could not be updated: trying again.';
    }
  }
  async function refreshSession() {
    if (document.hidden || !document.getElementById('session')) return;
    try {
      const res = await fetch('/companion/session.json', { cache: 'no-store', headers: sessTag ? { 'If-None-Match': sessTag } : {} });
      if (res.status === 304) return;   // the same as last time: nothing to draw
      if (!res.ok) throw new Error('HTTP ' + res.status);
      sess = await res.json();
      sessTag = res.headers.get('ETag');
      renderSession();
    } catch (e) { /* tried again in a few seconds */ }
  }
  window.addEventListener('hashchange', function () {
    const h = location.hash.replace('#', '').toLowerCase();
    if (data && data.systems.some(function (s) { return s.id === h; }) && h !== shown) show(h, true);
  });
  document.addEventListener('visibilitychange', function () { if (!document.hidden) { refresh(); refreshSession(); } });
  refresh();
  refreshSession();
  setInterval(refresh, 3000);
  setInterval(refreshSession, 5000);
  setInterval(tick, 1000);
})();
