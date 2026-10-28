using System.Globalization;
using SCTools.Server.Accounts;
using SCTools.Server.Data;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The tracker keys (admin only): a key is made for a player and shown once (the database keeps only its hash), and a key can be taken back, so that tracker
/// can no longer upload or log in.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route("/admin/keys")]
public sealed class KeysController : Controller
{
    readonly DataStore _store;
    readonly TrackerKeys _keys;
    readonly LiveTracker _live;
    readonly UserManager<IdentityUser> _users;
    readonly TrackerVersions _versions;

    public KeysController(DataStore store, TrackerKeys keys, LiveTracker live, UserManager<IdentityUser> users, TrackerVersions versions)
    {
        _store = store;
        _keys = keys;
        _live = live;
        _users = users;
        _versions = versions;
    }

    async Task<KeysModel> Page(NewKeyForm? form = null)
    {
        var now = DateTime.UtcNow;
        var newest = await _versions.NewestAsync(now);
        return new KeysModel
        {
            Keys = _store.TrackerKeys()
                .Select(k =>
                {
                    var (tracker, isNew) = TrackerVersions.Describe(_versions.Of(k.Player), newest, now);
                    return new KeyLine(k.Id, k.Player, k.CreatedUtc.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture), tracker, isNew);
                })
                .ToList(),
            Party = _live.Read(t => t.State.Data.Party.ToList()),
            New = form ?? new NewKeyForm(),
        };
    }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await Page());

    [HttpPost("")]
    public async Task<IActionResult> Make([Bind(Prefix = "New")] NewKeyForm form)
    {
        if (!ModelState.IsValid) return View("Index", await Page(form));
        var key = TrackerKeys.NewKey();
        _store.AddTrackerKey(form.Player, key, DateTime.UtcNow);
        _keys.Reload(_store.TrackerKeys());
        return View("Made", new KeyMadeModel { Player = form.Player.Trim(), Key = key, ServerUrl = Request.Scheme + "://" + Request.Host });
    }

    [HttpPost("{id:int}/remove")]
    public async Task<IActionResult> Remove(int id)
    {
        var row = _store.RemoveTrackerKey(id);
        var rows = _store.TrackerKeys();
        _keys.Reload(rows);
        if (row == null)
        {
            TempData["Message"] = "That key was taken back already.";
            return LocalRedirect("/admin/keys");
        }
        // his last key: his browser is logged out too (within a minute)
        if (!rows.Any(r => PlayerNames.SameName(r.Player, row.Player)) && await _users.FindByNameAsync(row.Player) is { } user
            && !await _users.IsInRoleAsync(user, Roles.Admin))
        {
            await _users.UpdateSecurityStampAsync(user);
        }
        TempData["Message"] = "The key of " + row.Player + " (made " + row.CreatedUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture) + ") is taken back.";
        return LocalRedirect("/admin/keys");
    }
}
