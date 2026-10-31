// A contract screenshot read into the Mission Library (#437, the admin's only): he pastes a screenshot of a contract's card in the mobiGlas (Ctrl+V in the
// paste box, or drops the file), and Tesseract, a free text reader, reads its fields in his browser: the name, the Reward, Contracted By and the
// description (for "a bonus of 18 MG Scrip"). The server makes values of them and finds the mission and the faction (ContractCardReader); he checks them
// and saves. Tesseract comes from the jsDelivr CDN the first time a screenshot is pasted (with its English text data, about 10 MB).
(function () {
  'use strict';
  const box = document.getElementById('shot');
  if (!box || !window.fetch || !window.createImageBitmap) return;
  const paste = box.querySelector('[data-shot-paste]');
  const status = box.querySelector('[data-shot-status]');
  const result = box.querySelector('[data-shot-result]');
  const token = (document.querySelector('#shotToken input[name="__RequestVerificationToken"]') || {}).value || '';
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

  function say(text) { status.textContent = text; }

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
  async function prepared(file) {
    const img = await createImageBitmap(file);
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

  async function readCard(file) {
    say('Reading the screenshot…');
    result.hidden = true;
    const c = await prepared(file);
    const w = await tesseract();
    async function read(name, params, r) {
      await w.setParameters(params);
      return (await w.recognize(c, { rectangle: r || rect(BOXES[name], c.width, c.height) })).data;
    }
    const free = { tessedit_char_whitelist: '', tessedit_pageseg_mode: '6' };
    const line = { tessedit_char_whitelist: '', tessedit_pageseg_mode: '7' };
    const title = (await read('title', free)).text;
    const contractor = (await read('contractor', line)).text;
    const details = (await read('details', free)).text;
    // the reward in two passes: read freely, the currency sign before the number is a word of its own; then only the number, with digits only (read
    // with digits only, the sign was a 1: 35,000 became 135,000)
    let reward = '';
    const rb = rect(BOXES.reward, c.width, c.height);
    const words = (await read('reward', line, rb)).words.filter(function (x) { return /\d/.test(x.text); });
    if (words.length > 0) {
      const left = Math.max(rb.left, words[words.length - 1].bbox.x0 - 4);
      reward = (await read('reward', { tessedit_char_whitelist: '0123456789,.', tessedit_pageseg_mode: '7' },
        { left: left, top: rb.top, width: rb.left + rb.width - left, height: rb.height })).text.trim();
    }
    say('Finding the mission…');
    const form = new FormData();
    form.append('title', title); form.append('reward', reward); form.append('contractor', contractor); form.append('details', details);
    form.append('__RequestVerificationToken', token);
    const res = await fetch(box.dataset.readUrl, { method: 'POST', body: form, credentials: 'same-origin' });
    if (!res.ok) throw new Error('The server said ' + res.status + '.');
    show(await res.json());
  }

  function el(tag, attrs, text) {
    const e = document.createElement(tag);
    for (const k in attrs || {}) e.setAttribute(k, attrs[k]);
    if (text != null) e.textContent = text;
    return e;
  }
  const nf = new Intl.NumberFormat('en-US');
  function n(v) { return v == null ? '' : nf.format(v); }

  // what was read, beside what the library has for the mission chosen; every field can be changed before it is saved
  function show(d) {
    result.innerHTML = '';
    const f = el('form', { class: 'shot-form' });
    const table = el('table', { class: 'shot-table' });
    const head = el('tr');
    ['', 'Read from the screenshot', 'In the library now'].forEach(function (t) { head.appendChild(el('th', null, t)); });
    table.appendChild(el('thead')).appendChild(head);
    const body = el('tbody');
    table.appendChild(body);

    const pick = el('select', { name: 'key', 'aria-label': 'Mission' });
    (d.choices || []).forEach(function (c) { pick.appendChild(el('option', { value: c.key }, c.name)); });
    pick.appendChild(el('option', { value: '' }, 'A new mission: ' + (d.title || '(no name read)')));
    const name = el('input', { name: 'title', value: d.title || '', 'aria-label': 'Name of the new mission', class: 'name' });
    const missionCell = el('td');
    missionCell.appendChild(pick);
    missionCell.appendChild(name);
    const nowCells = {};
    function row(label, input, key) {
      const tr = el('tr');
      tr.appendChild(el('th', null, label));
      const td = el('td');
      td.appendChild(input);
      tr.appendChild(td);
      const now = el('td', { class: 'muted' });
      nowCells[key] = now;
      tr.appendChild(now);
      body.appendChild(tr);
    }
    const missionRow = el('tr');
    missionRow.appendChild(el('th', null, 'Mission'));
    missionRow.appendChild(missionCell);
    nowCells.mission = el('td', { class: 'muted' });
    missionRow.appendChild(nowCells.mission);
    body.appendChild(missionRow);
    row('Reward (aUEC)', el('input', { name: 'auec', value: d.reward == null ? '' : String(d.reward), class: 'num', inputmode: 'numeric' }), 'auec');
    row('MG scrip', el('input', { name: 'mg', value: d.mg == null ? '' : String(d.mg), class: 'num', inputmode: 'numeric' }), 'mg');
    row('Council scrip', el('input', { name: 'council', value: d.council == null ? '' : String(d.council), class: 'num', inputmode: 'numeric' }), 'council');
    row('Faction', el('input', { name: 'giver', value: d.faction || '', list: 'factions' }), 'giver');
    f.appendChild(table);

    function now() {
      const c = (d.choices || []).find(function (x) { return x.key === pick.value; });
      name.hidden = !!c;
      nowCells.mission.textContent = c ? '' : 'it is added to the library';
      nowCells.auec.textContent = c ? (n(c.auec) || '–') + (c.wiki ? '  (Wiki: ' + c.wiki + ')' : '') : '';
      nowCells.mg.textContent = c ? (n(c.mg) || '–') : '';
      nowCells.council.textContent = c ? (n(c.council) || '–') : '';
      nowCells.giver.textContent = c ? (c.faction || '–') : '';
    }
    pick.addEventListener('change', now);
    now();

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
      if (!name.hidden) data.set('title', name.value); else data.delete('title');
      data.append('__RequestVerificationToken', token);
      try {
        const res = await fetch(box.dataset.saveUrl, { method: 'POST', body: data, credentials: 'same-origin' });
        const r = await res.json();
        say(r.ok ? 'Saved in the library. Paste the next one.' : r.said);
        if (r.ok) result.hidden = true;
      } catch (err) {
        say('Not saved: ' + err.message);
      } finally {
        save.disabled = false;
      }
    });
    result.appendChild(f);
    result.hidden = false;
    say((d.choices || []).length > 0 ? 'Check what was read, then save.' : 'No mission of the library has this name: check the name, it is added as a new one.');
  }

  async function take(file) {
    if (!file || !/^image\//.test(file.type)) { say('That is not a picture: paste or drop a screenshot.'); return; }
    try { await readCard(file); } catch (err) { say('It could not be read: ' + err.message); }
  }

  // Ctrl+V while the paste box (or anything of this section) has the focus; a dropped file anywhere on the box
  document.addEventListener('paste', function (e) {
    if (!box.contains(document.activeElement)) return;
    const items = (e.clipboardData && e.clipboardData.items) || [];
    for (let i = 0; i < items.length; i++) {
      if (items[i].type.indexOf('image/') === 0) { e.preventDefault(); take(items[i].getAsFile()); return; }
    }
  });
  paste.addEventListener('dragover', function (e) { e.preventDefault(); paste.classList.add('over'); });
  paste.addEventListener('dragleave', function () { paste.classList.remove('over'); });
  paste.addEventListener('drop', function (e) {
    e.preventDefault();
    paste.classList.remove('over');
    take(e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0]);
  });
})();
