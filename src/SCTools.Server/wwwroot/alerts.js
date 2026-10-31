// The admin's notice of places the map does not know (#414, Frank 31 Oct 2026), on every page he has open (the layout loads it for him alone). Every few
// seconds it asks the site (/admin/places/unknown.json, UnknownPlaceAlerts) for what came since it last asked:
// - a toast in the top right for each notice ("Map places: unknown place needs registration"), with a short sound; a click opens Map places. The sound can
//   be switched off in the toast (kept in this browser). A browser plays sound only after the page was clicked once since it loaded.
// - on the Companion (#unknownHere under the map) a card for every unknown place his accounts were at lately, with Map places' form to say what it is
//   (sent in the background: he stays on the Companion, the card says what was done; Frank, 31 Oct 2026) and Later (gone until his next visit there;
//   the place stays on Map places).
(function () {
  'use strict';
  let seq = null;   // the last notice this page has: what was said before it opened is not news to it
  const box = document.getElementById('unknownHere');

  function token() { const t = document.querySelector('input[name=__RequestVerificationToken]'); return t ? t.value : ''; }
  function soundOn() { try { return localStorage.getItem('sct-unknown-sound') !== 'off'; } catch (e) { return true; } }
  function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined) e.textContent = text; return e; }

  // a short two-note ping (no sound file to fetch)
  function ping() {
    if (!soundOn()) return;
    if (navigator.userActivation && !navigator.userActivation.hasBeenActive) return;   // not allowed before the first click on the page
    try {
      const ctx = new (window.AudioContext || window.webkitAudioContext)();
      [[880, 0], [1320, 0.12]].forEach(function (n) {
        const o = ctx.createOscillator(), g = ctx.createGain();
        o.frequency.value = n[0];
        g.gain.setValueAtTime(0.0001, ctx.currentTime + n[1]);
        g.gain.exponentialRampToValueAtTime(0.15, ctx.currentTime + n[1] + 0.02);
        g.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + n[1] + 0.25);
        o.connect(g); g.connect(ctx.destination);
        o.start(ctx.currentTime + n[1]); o.stop(ctx.currentTime + n[1] + 0.3);
      });
      setTimeout(function () { ctx.close(); }, 1000);
    } catch (e) { /* no sound, the toast is there */ }
  }

  function stack() {
    let s = document.getElementById('toasts');
    if (!s) { s = el('div', 'toasts'); s.id = 'toasts'; s.setAttribute('aria-live', 'polite'); document.body.appendChild(s); }
    return s;
  }

  function toast(a) {
    const t = el('div', 'toast');
    t.setAttribute('role', 'status');
    t.appendChild(el('div', 'title', 'Map places: unknown place needs registration'));
    t.appendChild(el('div', 'what', a.text + ' · ' + a.kind + ' · seen by ' + a.player + (a.his ? ' (you are there)' : '')));
    const row = el('div', 'row');
    const sound = el('button', 'link', soundOn() ? 'Sound: on' : 'Sound: off');
    sound.type = 'button';
    sound.addEventListener('click', function (e) {
      e.stopPropagation();
      try { localStorage.setItem('sct-unknown-sound', soundOn() ? 'off' : 'on'); } catch (x) { /* only this page then */ }
      sound.textContent = soundOn() ? 'Sound: on' : 'Sound: off';
    });
    const close = el('button', 'link', 'Close');
    close.type = 'button';
    close.addEventListener('click', function (e) { e.stopPropagation(); t.remove(); });
    row.appendChild(sound); row.appendChild(close);
    t.appendChild(row);
    t.addEventListener('click', function () { location.href = '/admin/map#unknown'; });   // always the Map tab's list of them, the Companion too (Frank)
    stack().appendChild(t);
    setTimeout(function () { t.remove(); }, 60000);
    ping();
  }

  // the Companion's cards: drawn again only when they changed, so a pick in a form is not lost
  let drawn = '';
  function cards(d) {
    if (!box) return;
    if (box.querySelector('.unknowncard.done')) return;   // a card says it was saved: drawn again when that is gone
    const sig = JSON.stringify(d.here.map(function (h) { return [h.text, h.kindKey, h.when]; }));
    if (sig === drawn) return;
    drawn = sig;
    box.textContent = '';
    d.here.forEach(function (h) {
      const c = el('div', 'unknowncard');
      const head = el('div', 'head');
      head.appendChild(el('strong', '', 'Unknown place here: '));
      head.appendChild(el('code', '', h.text));
      head.appendChild(document.createTextNode(' · ' + h.kind + ' · seen ' + new Date(h.when).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) + ' by ' + h.player));
      c.appendChild(head);
      const f = el('form', 'alias');
      f.method = 'post'; f.action = '/admin/places/alias';
      [['__RequestVerificationToken', token()], ['text', h.text], ['back', '/companion']].forEach(function (p) {
        const i = el('input'); i.type = 'hidden'; i.name = p[0]; i.value = p[1]; f.appendChild(i);
      });
      f.appendChild(el('span', 'muted', 'It is: '));
      const pick = el('select'); pick.name = 'key'; pick.setAttribute('aria-label', 'What it is');
      const first = el('option', '', 'Pick what it is…'); first.value = ''; pick.appendChild(first);
      const none = el('option', '', 'Not a place to show'); none.value = d.notAPlace; pick.appendChild(none);
      (d.places || []).forEach(function (g) {
        const og = el('optgroup'); og.label = g.name;
        g.places.forEach(function (p) { const o = el('option', '', p.name); o.value = p.key; og.appendChild(o); });
        pick.appendChild(og);
      });
      f.appendChild(pick);
      const save = el('button', '', 'Save'); save.type = 'submit'; f.appendChild(save);
      f.addEventListener('submit', function (e) {
        e.preventDefault();   // in the background: he stays on the Companion
        if (!pick.value) { pick.focus(); return; }
        save.disabled = true;
        fetch(f.action, { method: 'POST', body: new FormData(f), headers: { 'X-Autosave': '1' }, credentials: 'same-origin' })
          .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
          .then(function (j) {
            c.textContent = '';
            c.classList.add('done');
            c.appendChild(el('span', '', j.said || 'Saved.'));   // "RR_JP_NyxCastra is Stanton Gateway."
            setTimeout(function () { c.remove(); drawn = ''; }, 6000);
          })
          .catch(function () { save.disabled = false; save.textContent = 'Not saved: try again'; });
      });
      const add = el('a', 'small', 'add it as a new place'); add.href = '/admin/map?add=' + encodeURIComponent(h.text) + '#add'; f.appendChild(add);
      const later = el('button', 'link', 'Later'); later.type = 'button';
      later.addEventListener('click', function () {
        const b = new FormData();
        b.append('text', h.text); b.append('kind', h.kindKey); b.append('__RequestVerificationToken', token());
        c.remove();
        fetch('/admin/places/later', { method: 'POST', body: b, headers: { 'X-Autosave': '1' }, credentials: 'same-origin' }).catch(function () { /* it comes back with the next ask */ });
      });
      f.appendChild(later);
      c.appendChild(f);
      box.appendChild(c);
    });
  }

  async function ask() {
    if (document.hidden) return;
    try {
      const res = await fetch('/admin/places/unknown.json' + (seq !== null ? '?after=' + seq : ''), { cache: 'no-store', credentials: 'same-origin' });
      if (!res.ok) return;
      const d = await res.json();
      if (seq !== null) d.alerts.forEach(toast);
      seq = d.seq;
      cards(d);
    } catch (e) {
      // the next ask tries again
    }
  }
  ask();
  setInterval(ask, 5000);
  document.addEventListener('visibilitychange', function () { if (!document.hidden) ask(); });
})();
