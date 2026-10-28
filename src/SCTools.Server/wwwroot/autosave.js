// A form marked data-autosave (the fields of the Party page, the scenario of the Gauntlet's Top 10) is sent as soon as one of its fields changes, so there
// is no Save or Show button. data-autosave="quiet" (the Mission Library) sends it in the background: the page stays as it is (no waiting for it to come
// again, the place and the next field kept), the field shows that it was saved (or not, with why when the mouse is on it), and #autosaveState says it.
document.addEventListener('change', function (e) {
  var form = e.target.form;
  if (!form || !form.hasAttribute('data-autosave')) return;
  if (form.getAttribute('data-autosave') !== 'quiet' || !window.fetch) { form.requestSubmit(); return; }
  var field = e.target, state = document.getElementById('autosaveState');
  function done(ok, said) {
    // a new value of a field with a list to pick from (a giver): in the list for the other fields at once
    if (ok && field.list && field.value.trim() && !Array.prototype.some.call(field.list.options, function (o) { return o.value.toLowerCase() === field.value.trim().toLowerCase(); })) {
      var option = document.createElement('option');
      option.value = field.value.trim();
      field.list.appendChild(option);
    }
    field.classList.remove('saving');
    field.classList.add(ok ? 'saved' : 'unsaved');
    field.title = ok ? '' : said;
    if (state) state.textContent = said;
  }
  field.classList.remove('saved', 'unsaved');
  field.classList.add('saving');
  fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Autosave': '1' }, credentials: 'same-origin' })
    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
    .then(function (d) { done(d.ok, d.said || ''); })
    .catch(function () { done(false, 'Not saved: the site could not be reached. Try again.'); });
});
