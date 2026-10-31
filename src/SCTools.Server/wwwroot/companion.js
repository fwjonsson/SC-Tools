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
  function tabs(d) {   // the engine's buttons in the map's corner (31 Oct 2026; the row under the bar, beside the status line, before); #stanton in the address opens that system too
    const n = counts(d);
    map.systems(d.systems.map(function (s) {
      return { id: s.id, text: s.name + (n[s.id] ? ' · ' + n[s.id] : ''), title: n[s.id] ? n[s.id] + (n[s.id] === 1 ? ' player' : ' players') + ' in ' + s.name : 'Nobody in ' + s.name };
    }), shown, function (id) { show(id, true); });
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
    contracts('missionsAll', d.missions || [], false, 'No contracts going on.');   // the Contracts tab has them too
  }

  // ------------------------------------------------------------------ the contract tables
  // A field of the Mission Library, as on Edit Mission Info: a little form of its own that autosave.js sends in the background when it changes.
  // Faction, Category and Legality are drop-downs of the admin's lists (the page's datalists: the box above the Mission Library, and Legal or Illegal), with
  // the one it has when it is not in the list any more; empty: the Wiki's, in grey.
  const LISTS = { giver: 'factions', category: 'categories', legality: 'legalities' };
  function field(edit, name, value, hint, cls) {
    const form = el('form');
    form.method = 'post';
    form.action = '/admin/missions/set';
    form.setAttribute('data-autosave', 'quiet');
    [['__RequestVerificationToken', tokenField.value], ['id', edit.key], ['field', name]].forEach(function (h) {
      const i = el('input'); i.type = 'hidden'; i.name = h[0]; i.value = h[1]; form.appendChild(i);
    });
    const source = LISTS[name] && document.getElementById(LISTS[name]);
    let input;
    if (source) {
      input = el('select', cls);
      const empty = el('option', '', hint ? '– ' + hint : '–'); empty.value = ''; input.appendChild(empty);
      const values = Array.from(source.querySelectorAll('option')).map(function (o) { return o.value; });
      if (value && values.indexOf(value) < 0) values.unshift(value);
      values.forEach(function (v) { const o = el('option', '', v); o.value = v; if (v === value) o.selected = true; input.appendChild(o); });
    } else {
      input = el('input', cls);
      input.value = value || '';
      input.placeholder = hint || '';
      input.autocomplete = 'off';
    }
    input.name = 'value';
    input.setAttribute('aria-label', name === 'giver' ? 'faction' : name);
    form.appendChild(input);
    form.addEventListener('submit', function (e) { e.preventDefault(); input.blur(); });   // Enter saves it (the change), the page stays
    return form;
  }

  // The admin's 📷 of a contract: a little box beside it reads a screenshot of the contract into its mission (screenshot.js; #437).
  function shotLink(key, name) {
    const b = el('button', 'shotbtn', '📷');
    b.type = 'button';
    b.dataset.shotFor = key;
    b.dataset.shotName = name;
    b.title = 'Read a screenshot of this contract (Ctrl+V)';
    return b;
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
      ['MG scrip', 'right'], ['Council scrip', 'right'], ['XP', 'right'], ['Legality', '']].concat(edits ? [['📷', '']] : [])
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
        [m.category, '', e && ['category', e.category, e.categoryHint, 'category']],
        [m.scu, 'right', e && ['scu', e.scu, e.scuHint, 'num scu']],
        [m.reward, 'right', e && ['auec', e.auec, e.auecHint, 'num']],
        [m.mg, 'right', e && ['mg', e.mg, '', 'num scrip']],
        [m.council, 'right', e && ['council', e.council, '', 'num scrip']],
        [m.xp, 'right', e && ['xp', e.xp, e.xpHint, 'num']],
        [m.legality, '', e && ['legality', e.legality, e.legalityHint, 'legality']],
      ];
      cells.forEach(function (c) {
        const td = tr.insertCell();
        if (c[1]) td.className = c[1];
        if (c[2]) td.appendChild(field(e, c[2][0], c[2][1], c[2][2], c[2][3]));
        else td.textContent = c[0];
      });
      if (edits) { const td = tr.insertCell(); if (e) td.appendChild(shotLink(e.key, m.title)); }
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
    // the admin is typing the start time: drawn when he leaves the field, so what he types is not lost
    const a = document.activeElement;
    if (a && box.contains(a) && a.tagName === 'INPUT') { a.addEventListener('blur', renderSession, { once: true }); return; }
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
      // the start: a field for the admin (Frank, 31 Oct 2026: the same row as the Hauling Planner's), in this browser's time zone
      const startCell = line('Start time', hhmm(s.start));
      if (editor) startField(startCell, s);
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
      // what the numbers are: the table's tooltip, not a text under it (Frank, 31 Oct 2026)
      t.title = 'Your own play since the game started (a game started again within an hour of the last one is the same session). Earned is your part of ' +
        'what the Mission Library says your completed missions pay (the aUEC split among everybody who completed the same mission, the contracts of the Hauling Planner ' +
        'as its payouts; the scrip only for the one who took the contract), with MG scrip at ' + money(s.mgValue) + ' aUEC and Council scrip at ' +
        money(s.councilValue) + ' aUEC.' +
        (s.accounts ? ' The aUEC, scrip, Earned and Per hour are your accounts together (' + s.accounts.join(', ') + ') in this session, per hour of your own time played.' : '') +
        (s.withoutReward > 0 ? ' ' + s.withoutReward + (s.withoutReward === 1 ? ' completed mission has' : ' completed missions have') + ' no reward in the library yet.' : '');
    }
    contracts('completed', sess.completed || [], true, 'No contracts ended this session.');
  }
  // The admin's Start time of the session shown: 19:30 in this browser's time zone, or empty for the start the logs give; sent as soon as it changes.
  function startField(td, s) {
    td.textContent = '';
    const input = el('input', 'time startin' + (s.startSet ? ' typed' : ''));
    const d = new Date(s.start);
    input.value = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); input.placeholder = '19:30'; input.setAttribute('aria-label', 'Start time');
    input.title = s.startSet ? 'Set by you. Empty: the start the logs give again.' : 'When the game started. Type when the session really started (19:30).';
    input.addEventListener('change', function () {
      const body = new FormData();
      [['__RequestVerificationToken', tokenField.value], ['player', sess.me], ['from', s.from], ['startTime', input.value.trim()],
        ['tz', String(new Date(s.from).getTimezoneOffset())]].forEach(function (h) { body.append(h[0], h[1]); });
      input.classList.add('saving');
      fetch('/companion/start', { method: 'POST', body: body, credentials: 'same-origin' })
        .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
        .then(function (d) { input.classList.remove('saving'); input.classList.add(d.ok ? 'saved' : 'unsaved'); input.title = d.said || ''; if (d.ok) refreshSession(); })
        .catch(function () { input.classList.remove('saving'); input.classList.add('unsaved'); input.title = 'Not saved: the site could not be reached. Try again.'; });
    });
    td.appendChild(input);
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
    try {
      const res = await fetch('/companion/map.json', { cache: 'no-store' });
      if (!res.ok) throw new Error('HTTP ' + res.status);
      data = await res.json();
      data.clockOffset = Date.parse(data.updated) - Date.now();   // the server's clock against this browser's, when the data came (not when it is drawn)
      show(choose(data), false);
      if (window.siteLive) siteLive(true);   // the top bar's live text (live.js) says how it went; no "Updated" line of its own (31 Oct 2026)
    } catch (e) {
      if (window.siteLive) siteLive(false);
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

  // ------------------------------------------------------------------ the tabs (Overview, Session, Contracts; Frank, 31 Oct 2026)
  // One page: a tab is shown and the others hidden, its address set without a reload (back and forward go between them), so the map and the polls go on.
  function showTab(key) {
    document.querySelectorAll('section.companiontab').forEach(function (s) { s.hidden = s.dataset.tab !== key; });
    document.querySelectorAll('#companionTabs a[data-tab]').forEach(function (a) {
      const on = a.dataset.tab === key;
      a.classList.toggle('on', on);
      if (on) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    });
  }
  function tabOf(path) { const m = /^\/companion\/(overview|session|contracts)\/?$/i.exec(path); return m ? m[1].toLowerCase() : 'overview'; }
  document.querySelectorAll('#companionTabs a[data-tab]').forEach(function (a) {
    a.addEventListener('click', function (e) {
      if (e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;   // a new tab or window: the browser does it
      e.preventDefault();
      if (tabOf(location.pathname) !== a.dataset.tab) history.pushState(null, '', a.getAttribute('href'));
      showTab(a.dataset.tab);
    });
  });
  window.addEventListener('popstate', function () { showTab(tabOf(location.pathname)); });
  refresh();
  refreshSession();
  setInterval(refresh, 3000);
  setInterval(refreshSession, 5000);
  setInterval(tick, 1000);
})();
