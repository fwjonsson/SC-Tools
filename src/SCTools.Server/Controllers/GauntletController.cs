using SCTools.Server.Accounts;
using SCTools.Server.Data;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Gauntlet Tracker: Gilly's Combat Gauntlet from the log the player's tracker uploads. Everybody sees his own runs (the admin too) and the leaderboard
/// of everybody; the rewards are the Mission Library's and what a MG scrip is worth the admin's. The grind sessions start and stop by themselves
/// (GauntletService.GrindSessions). No hotkeys.
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
    readonly ScripValues _scrip;

    public GauntletController(GauntletService gauntlet, ToolAccess tools, LogUploads uploads, ScripValues scrip)
    {
        _gauntlet = gauntlet;
        _uploads = uploads;
        _tools = tools;
        _scrip = scrip;
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
        var rewards = _gauntlet.Rewards();   // the Mission Library's
        var value = (double)_scrip.Mg;   // the admin's Scrip page
        var runs = _gauntlet.Runs(who);

        var gameStart = _uploads.GameStart(who);
        var sessions = _gauntlet.GrindSessions(who, now);   // newest first; they start and stop by themselves
        GrindLine Line(SCTools.Sessions.AutoSessions.Window s) => new(GauntletStats.Session(runs, s.Start, s.Stop ?? now, rewards, value), s.Stop == null);
        var scenarios = GauntletStats.Scenarios(runs, rewards, value);
        var running = sessions.FirstOrDefault(s => s.Stop == null);

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
            PastGrinds = sessions.Where(s => s.Stop != null).Take(10).Select(Line).ToList(),
            Scenarios = scenarios,
            Ranking = GauntletStats.Ranking(scenarios),
            Leaderboard = tab == "leaderboard" ? GauntletLeaderboard.Make(_gauntlet.AllRuns(), rewards, value) : Array.Empty<GauntletLeader>(),
            Selected = Math.Clamp(scenario, 1, GauntletRewards.Scenarios),
            RunCount = runs.Count,
        };
        ViewData["Title"] = "Gauntlet Tracker";
        return View(model);
    }
}

/// <summary>
/// The admin's Gauntlet page: the rewards of the eight scenarios the Gauntlet Tracker counts with, the same for everybody. They are in the Mission Library
/// (a link to each), where they are changed; what a MG scrip is worth is on his Scrip page.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[Route("/admin/gauntlet")]
public sealed class GauntletAdminController : Controller
{
    readonly GauntletService _gauntlet;

    public GauntletAdminController(GauntletService gauntlet) => _gauntlet = gauntlet;

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Gauntlet";
        return View("~/Views/Gauntlet/Admin.cshtml", new GauntletAdminPage(_gauntlet.Rewards()));
    }
}
