// A form marked data-autosave (the fields of the Party page, the scenario of the Gauntlet's Top 10) is sent as soon as one of its fields changes, so there
// is no Save or Show button.
document.addEventListener('change', function (e) {
  var form = e.target.form;
  if (form && form.hasAttribute('data-autosave')) form.requestSubmit();
});
