using SCTools.Play;
using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Session tool: your own play. The time played today, this week, this month and in all (from every game your tracker saw, the old logs included),
/// the games and the crashes, the missions you completed and what they earned (aUEC, MG and Council scrip as the Mission Library says they pay, the scrip
/// at the admin's value), per hour; and the clock of the game you play now. Worked out again on every visit, so a reward typed in later counts for the past.
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[Route("/session")]
public sealed class SessionController : Controller
{
    readonly Playtime _playtime;
    readonly MissionLibrary _missions;
    readonly ScripValues _scrip;
    readonly ToolAccess _tools;

    public SessionController(Playtime playtime, MissionLibrary missions, ScripValues scrip, ToolAccess tools)
    {
        _playtime = playtime;
        _missions = missions;
        _scrip = scrip;
        _tools = tools;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!_tools.Sees(User, SiteTools.Session)) return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        var me = PointOfView.Name(HttpContext) ?? "";   // the admin looks as a player through the top bar's pick (30 Oct 2026)
        var now = DateTime.UtcNow;
        _missions.Collect();
        var games = _playtime.Games(me, now);
        var earned = _missions.Earned(me);
        long mg = _scrip.Mg, council = _scrip.Council;
        var periods = PlayStats.Periods(now, TimeZoneInfo.Local).Select(p => new PeriodLine(p.Name, PlayStats.For(p, games, earned, mg, council))).ToList();
        var lines = games.Take(30).Select(g => new GameLine(g, PlayStats.For(g.StartUtc, g.EndUtc.AddSeconds(1), Array.Empty<PlayedGame>(), earned, mg, council))).ToList();
        ViewData["Title"] = "Session";
        return View(new SessionPage(me, games.FirstOrDefault(g => g.Running), games.FirstOrDefault(g => !g.Running), periods, lines, games.Count,
            earned.Count(e => !e.HasReward), mg, council));
    }
}
