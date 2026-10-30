using SCTools.Server.Accounts;
using SCTools.Server.Data;
using SCTools.Server.Models;
using SCTools.Tracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Gauntlet Tracker: Gilly's Combat Gauntlet from the logs the trackers upload. The Session, Stats and History tabs are the viewer's in-game party's
/// (everybody in it now, from the logs: PartyService; alone, or not playing, his own runs; Frank, 29 Oct 2026), the Leaderboard everybody's; the rewards
/// are the Mission Library's and what a MG scrip is worth the admin's. The grind sessions start and stop by themselves (GauntletService.GrindSessions).
/// No hotkeys.
/// Cancelled missions never count as earnings; a run counts for the timings when it took at most 15 minutes (60 for scenario 8).
/// </summary>
[AutoValidateAntiforgeryToken]
[Route("/gauntlet")]
public sealed class GauntletController : Controller
{
    static readonly string[] Tabs = { "session", "stats", "history", "leaderboard" };

    readonly GauntletService _gauntlet;
    readonly ToolAccess _tools;
    readonly LogUploads _uploads;
    readonly ScripValues _scrip;
    readonly PartyService _party;

    readonly LiveTracker _live;

    public GauntletController(GauntletService gauntlet, ToolAccess tools, LogUploads uploads, ScripValues scrip, PartyService party, LiveTracker live)
    {
        _live = live;
        _gauntlet = gauntlet;
        _uploads = uploads;
        _tools = tools;
        _scrip = scrip;
        _party = party;
    }

    /// <summary>Whose page it is: the one logged in.</summary>
    string Me => PointOfView.Name(HttpContext) ?? "";   // the admin looks as a player through the top bar's pick (30 Oct 2026)

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    [HttpGet("{tab?}")]
    public IActionResult Index(string? tab, int scenario = 1)
    {
        if (!_tools.Sees(User, SiteTools.Gauntlet)) return Hidden();
        if (tab == "ranking") return LocalRedirect("/gauntlet/stats");   // the Earnings ranking tab was History from 29 Oct 2026, the ranking is on Stats since 30 Oct: old links
        tab = Tabs.Contains(tab) ? tab! : "session";
        _gauntlet.Collect();
        var who = Me;
        var now = DateTime.UtcNow;
        var visitor = Audience.Of(HttpContext).Visitor;   // not logged in: the Leaderboard, and a note to log in on the other tabs (Frank, 30 Oct 2026)
        var rewards = _gauntlet.Rewards();   // the Mission Library's
        var value = (double)_scrip.Mg;   // the admin's Scrip page
        // the viewer's in-game party now (the logs): the pages show the party's runs together; alone, or not playing, his own
        var party = _party.Parties(now).FirstOrDefault(g => g.Names.Any(n => string.Equals(n, who, StringComparison.OrdinalIgnoreCase)));
        // the Stats tab is his own (Frank, 30 Oct 2026: the Top 10 fastest and the whole Stats page are personal); the Session and History tabs his party's
        var members = tab != "stats" && party != null && party.Names.Count > 1 ? party.Names : new[] { who };
        if (visitor) members = Array.Empty<string>();
        var all = _gauntlet.RunsOf(members);
        var runs = all.Select(x => x.Run).ToList();
        // whose run each one is (for the top 10 of a party): a mission shared in the party is in every member's log, the one who accepted it has its start
        var by = all.GroupBy(x => x.Run.MissionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (g.FirstOrDefault(x => x.Run.AcceptedUtc != null).Player ?? g.First().Player), StringComparer.OrdinalIgnoreCase);

        // the game session: from the start of the game, for a party from the first of their games (everybody in a party is playing now)
        var gameStart = members.Select(m => _uploads.GameStart(m)).Where(s => s != null).Min();
        var sessions = _gauntlet.GrindSessions(members, runs, now);   // newest first; they start and stop by themselves (the runs read once)
        GrindLine Line(SCTools.Sessions.AutoSessions.Window s) => new(GauntletStats.Session(runs, s.Start, s.Stop ?? now, rewards, value), s.Stop == null);
        var scenarios = GauntletStats.Scenarios(runs, rewards, value);
        var running = sessions.FirstOrDefault(s => s.Stop == null);

        var model = new GauntletPage
        {
            Tab = tab,
            Visitor = visitor,
            Player = who,
            Party = members,
            RunBy = by,
            IsAdmin = User.IsInRole(Roles.Admin),
            ScripValue = value,
            Rewards = rewards,
            Lifetime = GauntletStats.Totals(runs, rewards, value),
            Game = gameStart == null ? null : GauntletStats.Session(runs, gameStart.Value, now, rewards, value),
            Grind = running == null ? null : Line(running),
            PastGrinds = sessions.Where(s => s.Stop != null).Take(10).Select(Line).ToList(),
            Scenarios = scenarios,
            Ranking = GauntletStats.Ranking(scenarios),
            Leaderboard = tab == "leaderboard" ? GauntletLeaderboard.Make(_gauntlet.AllRuns(), rewards, value, _live.Read(t => t.State.Data.CombinedAccounts)) : Array.Empty<GauntletLeader>(),
            Selected = Math.Clamp(scenario, 1, GauntletRewards.Scenarios),
            RunCount = runs.Count,
        };
        ViewData["Title"] = "Gauntlet Tracker";
        return View(model);
    }
}
