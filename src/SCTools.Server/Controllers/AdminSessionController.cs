using SCTools.Server.Editing;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The admin's Session page: the session (it starts and stops by itself; Start / Stop Session only when SCTools:FollowSessions is off), Next Stops, the start
/// time, the lost cargo of the runs (Register lost cargo), and the SCU of the from-Ruin drop-offs the game did not tell.
/// The rewards are the Mission Library's (Edit Mission Info), not here any more (Frank, 29 Oct 2026).
/// </summary>
[Route("/admin")]
public sealed class AdminSessionController : AdminControllerBase
{
    const string Back = "/admin";

    public AdminSessionController(LiveTracker live) : base(live)
    {
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var m = Live.Read(AdminSessionModel.Of);
        m.Auto = Live.FollowSessions;
        return View(m);
    }

    /// <summary>Start / Stop Session, only when the logs do not start and stop the sessions (SCTools:FollowSessions off).</summary>
    [HttpPost("session")]
    public IActionResult ToggleSession() =>
        Live.FollowSessions ? Say("The sessions start and stop by themselves.", Back) : Change(t => Edits.ToggleSession(t, DateTime.UtcNow), Back);

    [HttpPost("locks")]
    public IActionResult ToggleLocks() => Change(Edits.ToggleLocks, Back);

    [HttpPost("start-time")]
    public IActionResult StartTime(string? startTime) => Change(t => Edits.SetStartTime(t, startTime), Back);

    [HttpPost("run")]
    public IActionResult Run(string key, string level) => Change(t => Edits.SetLost(t, key, level), Back);

    [HttpPost("scu")]
    public IActionResult Scu(string key, string? scu)
    {
        var n = Amounts.Parse(scu) ?? 0;
        if (n != Math.Floor(n)) return Say("Type the SCU as a whole number.", Back);
        return Change(t => Edits.SetScu(t, key, (int)n), Back);
    }
}
