using System.Globalization;
using HaulingTracker.Server.Accounts;
using HaulingTracker.Server.Data;
using HaulingTracker.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

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

    public KeysController(DataStore store, TrackerKeys keys, LiveTracker live, UserManager<IdentityUser> users)
    {
        _store = store;
        _keys = keys;
        _live = live;
        _users = users;
    }

    KeysModel Page(NewKeyForm? form = null) => new()
    {
        Keys = _store.TrackerKeys()
            .Select(k => new KeyLine(k.Id, k.Player, k.CreatedUtc.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture)))
            .ToList(),
        Party = _live.Read(t => t.State.Data.Party.ToList()),
        New = form ?? new NewKeyForm(),
    };

    [HttpGet("")]
    public IActionResult Index() => View(Page());

    [HttpPost("")]
    public IActionResult Make([Bind(Prefix = "New")] NewKeyForm form)
    {
        if (!ModelState.IsValid) return View("Index", Page(form));
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
