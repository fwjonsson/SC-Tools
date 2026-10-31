using System.Globalization;
using System.Text.RegularExpressions;
using SCTools.Server.Editing;
using SCTools.Server.Accounts;
using SCTools.Server.Data;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The admin's Players page (Frank, 31 Oct 2026: in place of his Party and Tracker keys pages): every registered player (a tracker key made for him) and every
/// member of the Hauling Planner's party, with when he registered, when he last played, his tracker, whether the planner counts him active (Auto: the logs
/// decide; In or Out: the admin, until the logs change their mind) and his keys. His name opens his My page as he sees it, where the admin changes his
/// fleet and the rest for him (/me?player=). A key is made for a player and shown once (the database keeps only its hash), and a key can be taken back, so
/// that tracker can no longer upload or log in.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route("/admin/players")]
public sealed partial class PlayersController : AdminControllerBase
{
    const string Back = "/admin/players";

    readonly DataStore _store;
    readonly TrackerKeys _keys;
    readonly UserManager<IdentityUser> _users;
    readonly TrackerVersions _versions;

    public PlayersController(DataStore store, TrackerKeys keys, LiveTracker live, UserManager<IdentityUser> users, TrackerVersions versions) : base(live)
    {
        _store = store;
        _keys = keys;
        _users = users;
        _versions = versions;
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,40}$")]
    private static partial Regex GameName();

    async Task<PlayersModel> Page(NewKeyForm? form = null)
    {
        var now = DateTime.UtcNow;
        var newest = await _versions.NewestAsync(now);
        static string When(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture);
        var keys = _store.TrackerKeys();
        var party = Live.Read(t => AdminPartyModel.Of(t, now));
        // when each one last played: the last line of his newest game in the logs on the server
        var last = Live.Uploads.Games().ToDictionary(g => g.Key, g => g.Value.Select(r => r.Run.Last).DefaultIfEmpty().Max(), StringComparer.OrdinalIgnoreCase);
        var names = keys.Select(k => k.Player).Concat(party.Members.Select(m => m.Name)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        return new PlayersModel
        {
            Players = names.Select(n =>
            {
                var his = keys.Where(k => PlayerNames.SameName(k.Player, n)).OrderBy(k => k.CreatedUtc).ToList();
                var (tracker, isNew) = TrackerVersions.Describe(_versions.Of(n), newest, now);
                var played = last.FirstOrDefault(kv => PlayerNames.SameName(kv.Key, n)).Value;
                var member = party.Members.FirstOrDefault(m => PlayerNames.SameName(m.Name, n));
                return new PlayerLine(n, his.Count > 0 ? When(his[0].CreatedUtc) : null, played == default ? null : When(played), his.Count > 0 ? tracker : null,
                    his.Count > 0 ? isNew : null, member, party.Gone.Any(g => PlayerNames.SameName(g, n)),
                    his.Select(k => new KeyLine(k.Id, k.Player, When(k.CreatedUtc), tracker, isNew)).ToList());
            }).ToList(),
            Party = party,
            New = form ?? new NewKeyForm(),
        };
    }

    /// <summary>
    /// Whether the Hauling Planner counts a member of its party active: auto (the logs decide), in or out (the admin decides until the logs show something new
    /// about him: In until he leaves, Out until he plays). Saved with its time, so mission payouts are split between who was there when they paid out.
    /// </summary>
    [HttpPost("active")]
    public IActionResult Active(string name, string? value)
    {
        bool? active = value switch { "auto" => null, "in" => true, _ => false };
        return Change(t => Edits.SetActive(t, name ?? "", active, DateTime.UtcNow), Back);
    }

    /// <summary>Into the Hauling Planner's party: for somebody the logs have not shown in it yet (that sets him In until the logs show him leaving).</summary>
    [HttpPost("party/add")]
    public IActionResult AddMember(string? name)
    {
        name = name?.Trim() ?? "";
        if (!GameName().IsMatch(name)) return Say("Type the name in the game (letters, digits and - _ . only).", Back);
        return Change(t => Edits.AddMember(t, name, DateTime.UtcNow), Back);
    }

    /// <summary>Out of the Hauling Planner's party: his earlier work and debts stay, he just stops getting a share of new payouts.</summary>
    [HttpPost("party/remove")]
    public IActionResult RemoveMember(string name) => Change(t => Edits.RemoveMember(t, name), Back);

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await Page());

    /// <summary>Admin in the top bar: his first tab (the Session page that was here went on 31 Oct 2026; its start time is a row of every tool's session).</summary>
    [HttpGet("/admin")]
    public IActionResult Home() => LocalRedirect(Back);

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
            return LocalRedirect(Back);
        }
        // his last key: his browser is logged out too (within a minute)
        if (!rows.Any(r => PlayerNames.SameName(r.Player, row.Player)) && await _users.FindByNameAsync(row.Player) is { } user
            && !await _users.IsInRoleAsync(user, Roles.Admin))
        {
            await _users.UpdateSecurityStampAsync(user);
        }
        TempData["Message"] = "The key of " + row.Player + " (made " + row.CreatedUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture) + ") is taken back.";
        return LocalRedirect(Back);
    }
}
