using SCTools.Server.Accounts;
using SCTools.Server.Data;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Gauntlet Tracker: Gilly's Combat Gauntlet from the log the player's tracker uploads. Everybody sees his own runs (the admin too) and the leaderboard
/// of everybody; the rewards and what a MG scrip is worth are the admin's (his Gauntlet page, GauntletAdminController). Only buttons and forms, no hotkeys.
/// Cancelled missions never count as earnings; a run counts for the timings when it took at most 15 minutes (60 for scenario 8).
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[AutoValidateAntiforgeryToken]
[Route("/gauntlet")]
public sealed class GauntletController : Controller
{
    static readonly string[] Tabs = { "session", "stats", "ranking", "leaderboard" };

    readonly GauntletService _gauntlet;
    readonly ToolAccess _tools;
    readonly LogUploads _uploads;

    public GauntletController(GauntletService gauntlet, ToolAccess tools, LogUploads uploads)
    {
        _gauntlet = gauntlet;
        _uploads = uploads;
        _tools = tools;
    }

    /// <summary>Whose page it is: the one logged in.</summary>
    string Me => User.Identity?.Name ?? "";

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    [HttpGet("{tab?}")]
    public IActionResult Index(string? tab, int scenario = 1)
    {
        if (!_tools.Sees(User, SiteTools.Gauntlet)) return Hidden();
        tab = Tabs.Contains(tab) ? tab! : "session";
        _gauntlet.Collect();
        var who = Me;
        var now = DateTime.UtcNow;
        var settings = _gauntlet.Settings();
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
            IsAdmin = User.IsInRole(Roles.Admin),
            ScripValue = value,
            Rewards = rewards,
            Lifetime = GauntletStats.Totals(runs, rewards, value),
            Game = gameStart == null ? null : GauntletStats.Session(runs, gameStart.Value, now, rewards, value),
            Grind = running == null ? null : Line(running),
            PastGrinds = sessions.Where(s => s.EndedUtc != null).Take(10).Select(Line).ToList(),
            Scenarios = scenarios,
            Ranking = GauntletStats.Ranking(scenarios),
            Leaderboard = tab == "leaderboard" ? GauntletLeaderboard.Make(_gauntlet.AllRuns(), rewards, value) : Array.Empty<GauntletLeader>(),
            Selected = Math.Clamp(scenario, 1, GauntletRewards.Scenarios),
            RunCount = runs.Count,
        };
        ViewData["Title"] = "Gauntlet Tracker";
        return View(model);
    }

    [HttpPost("start")]
    public IActionResult Start()
    {
        if (!_tools.Sees(User, SiteTools.Gauntlet)) return Hidden();
        _gauntlet.Start(Me, DateTime.UtcNow);
        TempData["Message"] = "The grind session started.";
        return LocalRedirect("/gauntlet/session");
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        if (!_tools.Sees(User, SiteTools.Gauntlet)) return Hidden();
        _gauntlet.Collect();
        _gauntlet.Stop(Me, DateTime.UtcNow);
        TempData["Message"] = "The grind session ended.";
        return LocalRedirect("/gauntlet/session");
    }
}

/// <summary>The admin's Gauntlet page: the rewards of the eight scenarios and what a MG scrip is worth in aUEC, the same for everybody.</summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route("/admin/gauntlet")]
public sealed class GauntletAdminController : Controller
{
    readonly GauntletService _gauntlet;

    public GauntletAdminController(GauntletService gauntlet) => _gauntlet = gauntlet;

    [HttpGet("")]
    public IActionResult Index()
    {
        var settings = _gauntlet.Settings();
        ViewData["Title"] = "Gauntlet";
        return View("~/Views/Gauntlet/Admin.cshtml", new GauntletAdminPage((double)settings.ScripValue, GauntletService.Rewards(settings)));
    }

    [HttpPost("")]
    public IActionResult Save(GauntletOptionsForm form)
    {
        if (form.ScripValue < 0 || form.Auec.Any(a => a < 0) || form.Scrip.Any(s => s < 0))
        {
            TempData["Message"] = "Nothing was saved: a value cannot be below 0.";
            return LocalRedirect("/admin/gauntlet");
        }
        var row = new GauntletSettingsRow { ScripValue = form.ScripValue };
        for (var n = 1; n <= GauntletRewards.Scenarios; n++)
        {
            var d = GauntletRewards.Defaults[n];
            if (form.Auec[n - 1] != d.Auec) row.RewardAuec[n] = form.Auec[n - 1];
            if (form.Scrip[n - 1] != d.Scrip) row.RewardScrip[n] = form.Scrip[n - 1];
        }
        _gauntlet.SaveSettings(row);
        TempData["Message"] = "Saved: the Gauntlet Tracker counts with these for everybody.";
        return LocalRedirect("/admin/gauntlet");
    }
}
