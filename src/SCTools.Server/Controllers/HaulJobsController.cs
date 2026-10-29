using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// Hauling jobs (/hauling/jobs): the hauling missions of every system the party has going (not the Hauling Planner's own contracts; see HaulJobs), where a
/// logged in hauler types in the SCU of a mission the game did not give: that mission has it (only that one), and the Mission Library gets it as its
/// suggestion for the kind. The game's SCU, when it comes, wins; the library's (typed in there, else the suggestion) is what a mission has otherwise.
/// Every field is saved as soon as it changes (autosave.js, in the background).
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[AutoValidateAntiforgeryToken]
[Route("/hauling/jobs")]
public sealed class HaulJobsController : Controller
{
    readonly LiveTracker _live;
    readonly MissionLibrary _library;
    readonly ToolAccess _tools;

    public HaulJobsController(LiveTracker live, MissionLibrary library, ToolAccess tools)
    {
        _live = live;
        _library = library;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return Hidden();
        ViewData["Title"] = "Hauling jobs";
        var jobs = _live.Read(t => HaulJobs.Open(t.State).Select(j =>
        {
            var kind = t.State.Jobs.KindOf(j.Mission);
            return new HaulJobLine(j.Mission.Id, j.Title, string.Join(", ", j.Mission.Holders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase)),
                HaulJobs.StopsText(j.Pickups), HaulJobs.StopsText(j.Drops), j.ScuFrom, j.ScuFrom == ScuFrom.Game ? (long)Math.Round(j.Scu) : null,
                t.State.Jobs.HaulerScu(j.Mission), kind?.Scu, kind?.Suggested, kind?.SuggestedBy);
        }).ToList());
        return View(new HaulJobsPage(jobs));
    }

    /// <summary>The SCU a hauler typed in for a mission (empty: none; the game's or the library's again).</summary>
    [HttpPost("scu")]
    public IActionResult Scu(string id, string? title, string? value)
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return Hidden();
        var said = _library.SetMissionScu(id ?? "", title, value, User.Identity?.Name ?? "", DateTime.UtcNow);
        if (Request.Headers.ContainsKey(MissionsController.AutosaveHeader))
            return Json(new { ok = said != null, said = said ?? NotSaved, version = _library.Version });
        if (said == null) TempData["Message"] = NotSaved;
        return LocalRedirect("/hauling/jobs");
    }

    const string NotSaved = "Not saved: the SCU is a whole number above 0 (no letters).";
}
