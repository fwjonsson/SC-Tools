// A contract screenshot read into the Mission Library, right where the admin is (#437; Frank, 31 Oct 2026). Every mission row he sees has a 📷 button
// (data-shot-for: the mission's key, or its name as the table has it; data-shot-name: what to call it): on the Mission Library, the Companion's contract
// tables and the Hauling Planner's Session tables. It opens a little box beside the row for that mission alone: he presses Ctrl+V with the screenshot of
// its contract's card in the mobiGlas (or drops the file), Tesseract, a free text reader, reads it in his browser (the name, the Reward, Contracted By
// and the description: its MG or Council scrip, its reputation), the server makes values of it (ContractCardReader), he checks them and saves. Never a
// mission guessed by its name or added: only the one whose 📷 he pressed. Tesseract comes from the jsDelivr CDN the first time (about 10 MB).
(function () {
  'use strict';
  if (!window.fetch || !window.createImageBitmap) return;
  const READ = '/admin/missions/screenshot';
  const SAVE = '/admin/missions/screenshot/save';
  const TESSERACT = 'https://cdn.jsdelivr.net/npm/tesseract.js@5.1.1/dist/tesseract.min.js';

  // The fields of a contract's card in pixels of a 3840x1600 screenshot (Frank's, 31 Oct 2026): x, y, width, height. The mobiGlas is in the middle of the
  // screen and as big as its height, so another size is the same boxes scaled by its height around its middle.
  const BOXES = {
    title: [1490, 250, 900, 175],
    reward: [2840, 272, 260, 46],
    contractor: [2600, 342, 500, 46],
    details: [1500, 500, 700, 700],
  };
  function rect(b, w, h) {
    const s = h / 1600;
    return { left: Math.max(0, Math.round(w / 2 + (b[0] - 1920) * s)), top: Math.round(b[1] * s), width: Math.round(b[2] * s), height: Math.round(b[3] * s) };
  }
  // A box cut out; enlarged: as big as on a 3840x1600 screenshot, so a smaller picture (one pasted from somewhere that shrank it) has its small text read
  // too. Not the reward: its big digits read best as they are (enlarged, a 2000x833 picture's 387,000 became 00).
  function crop(c, b, enlarged) {
    const r = rect(b, c.width, c.height);
    const k = enlarged === false ? 1 : Math.max(1, 1600 / c.height);
    const out = document.createElement('canvas');
    out.width = Math.round(r.width * k);
    out.height = Math.round(r.height * k);
    const g = out.getContext('2d');
    g.imageSmoothingQuality = 'high';
    g.drawImage(c, r.left, r.top, r.width, r.height, 0, 0, out.width, out.height);
    return out;
  }

  let worker = null;
  function load(src) {
    return new Promise(function (ok, fail) {
      const s = document.createElement('script');
      s.src = src; s.onload = ok; s.onerror = function () { fail(new Error('Tesseract could not be fetched (' + src + ').')); };
      document.head.appendChild(s);
    });
  }
  async function tesseract() {
    if (!window.Tesseract) await load(TESSERACT);
    if (!worker) worker = await window.Tesseract.createWorker('eng');
    return worker;
  }

  // the screenshot grey and inverted: the game's light text on a dark background becomes dark text on light, which Tesseract reads best
  function prepared(img) {
    const c = document.createElement('canvas');
    c.width = img.width; c.height = img.height;
    const g = c.getContext('2d', { willReadFrequently: true });
    g.drawImage(img, 0, 0);
    const d = g.getImageData(0, 0, c.width, c.height);
    const p = d.data;
    for (let i = 0; i < p.length; i += 4) {
      const v = 255 - Math.round((p[i] + p[i + 1] + p[i + 2]) / 3);
      p[i] = p[i + 1] = p[i + 2] = v;
    }
    g.putImageData(d, 0, 0);
    return c;
  }

  // A box without the soft light behind it: a lamp or a glow of the hangar shines through the mobiGlas, and on a smaller picture it swallows the text in
  // front of it (Frank's "7 MG Scrip" of 31 Oct 2026). The box in grey, enlarged, less a blurred copy of itself (the glow), stretched and inverted.
  function deglowed(img, b) {
    const r = rect(b, img.width, img.height);
    const k = Math.max(1, 1600 / img.height);
    const W = Math.round(r.width * k), H = Math.round(r.height * k);
    const a = document.createElement('canvas'); a.width = W; a.height = H;
    const ga = a.getContext('2d', { willReadFrequently: true });
    ga.imageSmoothingQuality = 'high';
    ga.filter = 'grayscale(1)';
    ga.drawImage(img, r.left, r.top, r.width, r.height, 0, 0, W, H);
    const bl = document.createElement('canvas'); bl.width = W; bl.height = H;
    const gb = bl.getContext('2d', { willReadFrequently: true });
    gb.filter = 'blur(12px)';
    gb.drawImage(a, 0, 0);
    const pa = ga.getImageData(0, 0, W, H), pb = gb.getImageData(0, 0, W, H).data, p = pa.data;
    let max = 1;
    for (let i = 0; i < p.length; i += 4) { const v = Math.max(0, p[i] - pb[i]); p[i] = v; if (v > max) max = v; }
    for (let i = 0; i < p.length; i += 4) { const v = 255 - Math.min(255, Math.round(p[i] * 255 / max)); p[i] = p[i + 1] = p[i + 2] = v; }
    ga.putImageData(pa, 0, 0);
    return a;
  }

  function el(tag, attrs, text) {
    const e = document.createElement(tag);
    for (const k in attrs || {}) e.setAttribute(k, attrs[k]);
    if (text != null) e.textContent = text;
    return e;
  }
  const nf = new Intl.NumberFormat('en-US');
  function n(v) { return v == null ? '' : nf.format(v); }
  function token() { const i = document.querySelector('input[name="__RequestVerificationToken"]'); return i ? i.value : ''; }

  // ------------------------------------------------------------------ the little box beside the row
  let pop = null;   // the one open: { box, paste, status, result, key, name }

  function close() {
    if (pop) pop.box.remove();
    pop = null;
  }

  function open(anchor, key, name) {
    close();
    const box = el('div', { class: 'shotpop card', role: 'dialog', 'aria-label': 'Screenshot of ' + name });
    const head = el('div', { class: 'shotpop-head' });
    head.appendChild(el('span', null, '📷 '));
    head.appendChild(el('b', null, name));
    const x = el('button', { type: 'button', class: 'shotpop-close', title: 'Close', 'aria-label': 'Close' }, '×');
    x.addEventListener('click', close);
    head.appendChild(x);
    box.appendChild(head);
    const paste = el('div', { class: 'paste', tabindex: '0' }, 'Press Ctrl+V with the screenshot of this contract (its card in the mobiGlas), or drop it here.');
    box.appendChild(paste);
    const status = el('p', { class: 'hint', 'aria-live': 'polite' });
    box.appendChild(status);
    const result = el('div');
    result.hidden = true;
    box.appendChild(result);
    paste.addEventListener('dragover', function (e) { e.preventDefault(); paste.classList.add('over'); });
    paste.addEventListener('dragleave', function () { paste.classList.remove('over'); });
    paste.addEventListener('drop', function (e) {
      e.preventDefault();
      paste.classList.remove('over');
      take(e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0]);
    });
    // beside the row: under its button, kept inside the page's width
    const r = anchor.getBoundingClientRect();
    document.body.appendChild(box);
    const width = box.offsetWidth;
    box.style.left = Math.max(8, Math.min(r.left + window.scrollX, window.scrollX + document.documentElement.clientWidth - width - 8)) + 'px';
    box.style.top = (r.bottom + window.scrollY + 4) + 'px';
    pop = { box: box, paste: paste, status: status, result: result, key: key, name: name };
    paste.focus({ preventScroll: true });
  }

  function say(text) { if (pop) pop.status.textContent = text; }

  async function take(file) {
    if (!pop) return;
    if (!file || !/^image\//.test(file.type)) { say('That is not a picture: paste or drop a screenshot.'); return; }
    const mine = pop;
    try { await readCard(file, mine); } catch (err) { if (pop === mine) say('It could not be read: ' + err.message); }
  }

  async function readCard(file, mine) {
    say('Reading the screenshot…');
    mine.result.hidden = true;
    const img = await createImageBitmap(file);
    const c = prepared(img);
    const w = await tesseract();
    async function read(canvas, params, r) {
      await w.setParameters(params);
      return (await w.recognize(canvas, r ? { rectangle: r } : {})).data;
    }
    const free = { tessedit_char_whitelist: '', tessedit_pageseg_mode: '6' };
    const line = { tessedit_char_whitelist: '', tessedit_pageseg_mode: '7' };
    const title = (await read(crop(c, BOXES.title), free)).text;
    const contractor = (await read(crop(c, BOXES.contractor), line)).text;
    // the description twice, as it is and without the glow behind it (each finds what the other misses: the scrip of a big clean screenshot, of a small
    // one with a light behind it); the server looks for the scrip and the reputation in both
    const details = (await read(crop(c, BOXES.details), free)).text + '\n' + (await read(deglowed(img, BOXES.details), free)).text;
    // the reward in two passes: read freely, the currency sign before the number is a word of its own; then only the number, with digits only (read
    // with digits only, the sign was a 1: 35,000 became 135,000)
    let reward = '';
    const rc = crop(c, BOXES.reward, false);
    const words = (await read(rc, line)).words.filter(function (x) { return /\d/.test(x.text); });
    if (words.length > 0) {
      const left = Math.max(0, words[words.length - 1].bbox.x0 - 4);
      reward = (await read(rc, { tessedit_char_whitelist: '0123456789,.', tessedit_pageseg_mode: '7' },
        { left: left, top: 0, width: rc.width - left, height: rc.height })).text.trim();
    }
    if (pop !== mine) return;   // closed meanwhile
    say('Making values of it…');
    const form = new FormData();
    form.append('key', mine.key);
    form.append('title', title); form.append('reward', reward); form.append('contractor', contractor); form.append('details', details);
    form.append('__RequestVerificationToken', token());
    const res = await fetch(READ, { method: 'POST', body: form, credentials: 'same-origin' });
    if (!res.ok) throw new Error('The server said ' + res.status + '.');
    const d = await res.json();
    if (pop === mine) show(d, mine);
  }

  // A field of the box: a number, or a pick from the page's list (data-shot-list: the admin's factions; with what was read when it is not in the list).
  function field(name, value, cls, list) {
    const source = list && document.getElementById(list);
    if (!source) {
      const input = el('input', { name: name, value: value == null ? '' : String(value), class: cls || '' });
      if (cls === 'num') input.setAttribute('inputmode', 'numeric');
      return input;
    }
    const select = el('select', { name: name });
    select.appendChild(el('option', { value: '' }, '–'));
    const values = Array.from(source.querySelectorAll('option')).map(function (o) { return o.value; });
    if (value && values.indexOf(value) < 0) values.unshift(value);
    values.forEach(function (v) {
      const o = el('option', { value: v }, v);
      if (v === value) o.selected = true;
      select.appendChild(o);
    });
    return select;
  }

  // what was read, beside what the library has for the mission; every field can be changed before it is saved
  function show(d, mine) {
    const result = mine.result;
    result.textContent = '';
    if (d.error) { say(d.error); return; }
    const m = d.mission || {};
    const f = el('form');
    const table = el('table', { class: 'shot-table' });
    const head = el('tr');
    ['', 'Read', 'In the library now'].forEach(function (t) { head.appendChild(el('th', null, t)); });
    table.appendChild(el('thead')).appendChild(head);
    const body = el('tbody');
    table.appendChild(body);
    function row(label, name, value, now, cls, list) {
      const tr = el('tr');
      tr.appendChild(el('th', null, label));
      const td = el('td');
      td.appendChild(field(name, value, cls, list));
      tr.appendChild(td);
      tr.appendChild(el('td', { class: 'muted' }, now || '–'));
      body.appendChild(tr);
    }
    row('Reward (aUEC)', 'auec', d.reward, (n(m.auec) || '–') + (m.wiki ? '  (Wiki: ' + m.wiki + ')' : ''), 'num');
    row('MG scrip', 'mg', d.mg, n(m.mg), 'num');
    row('Council scrip', 'council', d.council, n(m.council), 'num');
    row('Faction', 'giver', d.faction, m.faction, '', 'factions');
    row('XP', 'xp', d.xp, n(m.xp), 'num');
    f.appendChild(table);
    if (d.title) f.appendChild(el('p', { class: 'hint' }, 'The screenshot says: ' + d.title));
    if ((d.notes || []).length > 0) {
      const ul = el('ul', { class: 'shot-notes' });
      d.notes.forEach(function (t) { ul.appendChild(el('li', null, t)); });
      f.appendChild(ul);
    }
    const save = el('button', { type: 'submit' }, 'Save');
    f.appendChild(save);
    f.addEventListener('submit', async function (e) {
      e.preventDefault();
      save.disabled = true;
      const data = new FormData(f);
      data.append('key', m.key || mine.key);
      data.append('__RequestVerificationToken', token());
      try {
        const res = await fetch(SAVE, { method: 'POST', body: data, credentials: 'same-origin' });
        const r = await res.json();
        if (pop !== mine) return;
        say(r.ok ? 'Saved in the Mission Library.' : r.said);
        if (r.ok) { result.hidden = true; setTimeout(function () { if (pop === mine) close(); }, 1500); }
      } catch (err) {
        say('Not saved: ' + err.message);
      } finally {
        save.disabled = false;
      }
    });
    result.appendChild(f);
    result.hidden = false;
    say('Check what was read, then save.');
  }

  // a 📷 of a mission row opens the box for it; Esc closes it; Ctrl+V while it is open reads the screenshot into it
  document.addEventListener('click', function (e) {
    const b = e.target.closest && e.target.closest('[data-shot-for]');
    if (!b) return;
    e.preventDefault();
    open(b, b.dataset.shotFor, b.dataset.shotName || b.dataset.shotFor);
  });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && pop) close(); });
  document.addEventListener('paste', function (e) {
    if (!pop) return;
    const items = (e.clipboardData && e.clipboardData.items) || [];
    for (let i = 0; i < items.length; i++) {
      if (items[i].type.indexOf('image/') === 0) { e.preventDefault(); take(items[i].getAsFile()); return; }
    }
  });
})();
