// A form marked data-autosave (the fields of the Party page) is sent as soon as one of its fields changes, so there is no Save button.
document.addEventListener('change', function (e) {
  var form = e.target.form;
  if (form && form.hasAttribute('data-autosave')) form.requestSubmit();
});
