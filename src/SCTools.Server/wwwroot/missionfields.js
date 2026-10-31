// The admin's fields of a mission in the contract tables of the tools (the Companion's, the Hauling Planner's Session tab: Frank, 31 Oct 2026, "editable
// anywhere they are visible, just like in the Mission Library"). A field is a little form of its own that autosave.js sends in the background when it
// changes, to the Mission Library (/admin/missions/set: the kind's key, or its name as a table has it). Faction, Category and Legality are drop-downs of
// the admin's lists (the page's datalists: Admin › Settings, and Legal or Illegal), with the one it has when it is not in the list any more; empty: the
// Star Citizen Wiki's, in grey.
window.SCToolsMissionFields = (function () {
  'use strict';
  const LISTS = { giver: 'factions', category: 'categories', legality: 'legalities' };

  function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined) e.textContent = text; return e; }
  function token() { const i = document.querySelector('input[name="__RequestVerificationToken"]'); return i ? i.value : ''; }

  // id: the kind's key (or its name); name: the field (name, giver, category, scu, auec, mg, council, xp, legality); value: what is typed in; hint: what
  // shows in grey when nothing is (the Wiki's, the suggestion)
  function field(id, name, value, hint, cls) {
    const form = el('form');
    form.method = 'post';
    form.action = '/admin/missions/set';
    form.setAttribute('data-autosave', 'quiet');
    [['__RequestVerificationToken', token()], ['id', id], ['field', name]].forEach(function (h) {
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

  // The admin is in one of a table's fields: it is not drawn again until he leaves it (what he types or picks is not lost).
  function busy(box) {
    const a = document.activeElement;
    return !!box && !!a && box.contains(a) && (a.tagName === 'INPUT' || a.tagName === 'SELECT');
  }

  // The cells of a contract row (map.json's missions: m.edit for the admin) as the Mission Library has them: [text, class, field or null].
  function cells(m, e) {
    return [
      [m.title, 'name', e && ['name', e.name, e.nameHint, 'name']],
      [m.faction, '', e && ['giver', e.giver, e.giverHint, 'giver']],
      [m.category, '', e && ['category', e.category, e.categoryHint, 'category']],
      [m.scu, 'right', e && ['scu', e.scu, e.scuHint, 'num scu']],
      [m.reward, 'right', e && ['auec', e.auec, e.auecHint, 'num']],
      [m.mg, 'right', e && ['mg', e.mg, '', 'num scrip']],
      [m.council, 'right', e && ['council', e.council, '', 'num scrip']],
      [m.xp, 'right', e && ['xp', e.xp, e.xpHint, 'num']],
      [m.legality, '', e && ['legality', e.legality, e.legalityHint, 'legality']],
    ];
  }

  return { field: field, busy: busy, cells: cells };
})();
