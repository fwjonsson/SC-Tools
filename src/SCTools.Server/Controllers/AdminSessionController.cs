using SCTools.Server.Editing;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The admin's Session page: Start / Stop Session and Next Stops (the buttons of the program), the mission rewards, the start time, the names and notes of
/// the stops, the lost cargo of the runs, and the SCU of the from-Ruin drop-offs the game did not tell.
/// </summary>
[Route("/admin")]
public sealed class AdminSessionController : AdminControllerBase
{
    const string Back = "/admin";

    public AdminSessionController(LiveTracker live) : base(live)
    {
    }

    [HttpGet("")]
    public IActionResult Index() => View(Live.Read(AdminSessionModel.Of));

    [HttpPost("session")]
    public IActionResult ToggleSession() => Change(t => Edits.ToggleSession(t, DateTime.UtcNow), Back);

    [HttpPost("locks")]
    public IActionResult ToggleLocks() => Change(Edits.ToggleLocks, Back);

    [HttpPost("rewards")]
    public IActionResult Rewards(List<string?> rewards)
    {
        var values = rewards.Select(Amounts.Parse).ToList();
        if (values.Any(v => v < 0)) return Say("A reward cannot be below 0.", Back);
        return Change(t => Edits.SetRewards(t, values), Back);
    }

    [HttpPost("start-time")]
    public IActionResult StartTime(string? startTime) => Change(t => Edits.SetStartTime(t, startTime), Back);

    [HttpPost("stop")]
    public IActionResult Stop(string zone, string? name, string? notes) => Change(t =>
    {
        var said = new List<string>();
        if ((name ?? "").Trim() != t.Places.LocationName(zone)) said.Add(Edits.RenameStop(t, zone, name ?? ""));
        var now = t.State.Inputs != null && t.State.Inputs.Rows.TryGetValue(zone, out var row) ? row.Notes : "";
        if ((notes ?? "").Trim() != now) said.Add(Edits.SetNotes(t, zone, notes ?? ""));
        return said.Count == 0 ? "Nothing changed." : string.Join(" ", said);
    }, Back);

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
