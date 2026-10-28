using HaulingTracker.Server.Accounts;
using HaulingTracker.Server.Data;
using HaulingTracker.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>
/// The Gauntlet Tracker: Gilly's Combat Gauntlet from the log the player's tracker uploads. A player sees and changes only his own; the admin can pick a
/// player. Only buttons and forms, no hotkeys. Cancelled missions never count as earnings; a run counts for the timings when it took at most 15 minutes
/// (60 for scenario 8).
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[AutoValidateAntiforgeryToken]
[Route("/gauntlet")]
public sealed class GauntletController : Controller
{
    static readonly string[] Tabs = { "session", "stats", "ranking", "options" };

    readonly GauntletService _gauntlet;
    readonly ToolAccess _tools;
    readonly LogUploads _uploads;

    public GauntletController(GauntletService gauntlet, ToolAccess tools, LogUploads uploads)
    {
        _gauntlet = gauntlet;
        _uploads = uploads;
        _tools = tools;
    }

    /// <summary>The player this request is about: himself, or for the admin the one he picked.</summary>
    string Target(string? picked)
    {
        var me = User.Identity?.Name ?? "";
        if (!User.IsInRole(Roles.Admin)) return me;
        var players = _gauntlet.Players();
        if (!string.IsNullOrEmpty(picked)) return players.FirstOrDefault(p => p.Equals(picked, StringComparison.OrdinalIgnoreCase)) ?? picked;
        return players.FirstOrDefault(p => p.Equals(me, StringComparison.OrdinalIgnoreCase)) ?? players.FirstOrDefault() ?? me;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    string Back(string tab, string player) => $"/gauntlet/{tab}?player={Uri.EscapeDataString(player)}";

    [HttpGet("{tab?}")]
    public IActionResult Index(string? tab, string? player, int scenario = 1)
    {
        if (!_tools.Sees(User, SiteTools.Gauntlet)) return Hidden();
        tab = Tabs.Contains(tab) ? tab! : "session";
        _gauntlet.Collect();
        var who = Target(player);
        var now = DateTime.UtcNow;
        var settings = _gauntlet.Settings(who);
        var rewards = GauntletService.Rewards(settings);
        var value = (double)settings.ScripValue;
        var runs = _gauntlet.Runs(who);

        var gameStart = _uploads.GameStart(who);
        var sessions = _gauntlet.Sessions(who);
        GrindLine Line(GauntletSessionRow s) =>
            new(GauntletStats.Session(runs, s.StartedUtc, s.EndedUtc ?? now, rewards, value), s.EndedUtc == null);
        var scenarios = GauntletStats.Scenarios(runs, rewards, value);
        var running = sessions.FirstOrDefault(s => s.EndedUtc == null);

        var model = new GauntletPage
        {
            Tab = tab,
            Player = who,
            Players = _gauntlet.Players(),
            CanPickPlayer = User.IsInRole(Roles.Admin),
            ScripValue = value,
            Rewards = rewards,
            Lifetime = GauntletStats.Totals(runs, rewards, value),
            Game = gameStart == null ? null : GauntletStats.Session(runs, gameStart.Value, now, rewards, value),
            Grind = running == null ? null : Line(running),
            PastGrinds = sessions.Where(s => s.EndedUtc != null).Take(10).Select(Line).ToList(),
            Scenarios = scenarios,
            Ranking = GauntletStats.Ranking(scenarios),
            Selected = Math.Clamp(scenario, 1, GauntletRewards.Scenarios),
            RunCount = runs.Count,
        };
        ViewData["Title"] = "Gauntlet Tracker";
        return View(model);
    }

    [HttpPost("start")]
    public IActionResult Start(string? player)
    {
        if (!_tools.Sees(User, SiteTools.Gauntlet)) return Hidden();
        var who = Target(player);
        _gauntlet.Start(who, DateTime.UtcNow);
        TempData["Message"] = "The grind session started.";
        return LocalRedirect(Back("session", who));
    }

    [HttpPost("stop")]
    public IActionResult Stop(string? player)
    {
        if (!_tools.Sees(User, SiteTools.Gauntlet)) return Hidden();
        var who = Target(player);
        _gauntlet.Collect();
        _gauntlet.Stop(who, DateTime.UtcNow);
        TempData["Message"] = "The grind session ended.";
        return LocalRedirect(Back("session", who));
    }

    [HttpPost("options")]
    public IActionResult Options(GauntletOptionsForm form)
    {
        if (!_tools.Sees(User, SiteTools.Gauntlet)) return Hidden();
        var who = Target(form.Player);
        if (form.ScripValue < 0 || form.Auec.Any(a => a < 0) || form.Scrip.Any(s => s < 0))
        {
            TempData["Message"] = "Nothing was saved: a value cannot be below 0.";
            return LocalRedirect(Back("options", who));
        }
        var row = new GauntletSettingsRow { Player = who, ScripValue = form.ScripValue };
        for (var n = 1; n <= GauntletRewards.Scenarios; n++)
        {
            var d = GauntletRewards.Defaults[n];
            if (form.Auec[n - 1] != d.Auec) row.RewardAuec[n] = form.Auec[n - 1];
            if (form.Scrip[n - 1] != d.Scrip) row.RewardScrip[n] = form.Scrip[n - 1];
        }
        _gauntlet.SaveSettings(row);
        TempData["Message"] = "Saved.";
        return LocalRedirect(Back("options", who));
    }
}
