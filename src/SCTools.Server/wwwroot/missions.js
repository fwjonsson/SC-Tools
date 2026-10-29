// The Mission Library (and the admin's Edit Mission Info) keeps itself up to date while it is open: every few seconds it asks the server for the library's
// version (data-version-url: /missions/version or /admin/missions/version), and when that is not the one the page shows (a mission came in or went on, a
// field was saved) it gets the page again and puts the new list in place, with the same choices and order. Not while you are typing in it (the window
// has the focus and a field of the list has it): then it waits until you leave the list.
(function () {
  'use strict';
  const box = document.getElementById('library');
  if (!box || !window.fetch || !window.DOMParser) return;
  const versionUrl = box.dataset.versionUrl || '/missions/version';
  let version = box.dataset.version, behind = false;

  function typing() {
    const a = document.activeElement;
    return document.hasFocus() && a && box.contains(a) && (a.tagName === 'INPUT' || a.tagName === 'SELECT' || a.tagName === 'TEXTAREA');
  }
  async function reload() {
    if (typing()) { behind = true; return; }
    try {
      const res = await fetch(location.href, { cache: 'no-store', credentials: 'same-origin' });
      if (!res.ok) return;
      const fresh = new DOMParser().parseFromString(await res.text(), 'text/html').getElementById('library');
      if (!fresh) return;
      if (typing()) { behind = true; return; }   // he started typing while it came
      box.innerHTML = fresh.innerHTML;
      version = box.dataset.version = fresh.dataset.version;
      behind = false;
    } catch (e) { /* the next time */ }
  }
  async function check() {
    if (document.hidden) return;
    try {
      const res = await fetch(versionUrl, { cache: 'no-store', credentials: 'same-origin' });
      if (!res.ok) return;
      const d = await res.json();
      if (behind || String(d.version) !== String(version)) await reload();
    } catch (e) { /* the next time */ }
  }
  setInterval(check, 4000);
  document.addEventListener('visibilitychange', function () { if (!document.hidden) check(); });
  box.addEventListener('focusout', function () { setTimeout(function () { if (behind && !typing()) reload(); }, 400); });
})();
