using SCTools.Server.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The SCU field of the Hauling Planner's Other hauling board (/hauling, the hauling jobs of every system; see HaulJobs): a logged in hauler types in the
/// SCU of a mission the game did not give, and app.js sends it here in the background. That mission has it (only that one), and the Mission Library gets it
/// as its suggestion for the kind. The game's SCU, when it comes, wins; the library's (typed in there, else the suggestion), else the Star Citizen Wiki's,
/// is what a mission has otherwise. /hauling/jobs was a page of its own once: it is the Hauling Planner now.
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[AutoValidateAntiforgeryToken]
[Route("/hauling/jobs")]
public sealed class HaulJobsController : Controller
{
    readonly MissionLibrary _library;
    readonly ToolAccess _tools;

    public HaulJobsController(MissionLibrary library, ToolAccess tools)
    {
        _library = library;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <summary>The page it was once: the Hauling Planner, where the jobs and their SCU fields are.</summary>
    [HttpGet("")]
    [AllowAnonymous]
    public IActionResult Index() => LocalRedirect("/hauling");

    /// <summary>The SCU a hauler typed in for a mission (empty: none; the game's, the library's or the Wiki's again).</summary>
    [HttpPost("scu")]
    public IActionResult Scu(string id, string? title, string? value)
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return Hidden();
        var said = _library.SetMissionScu(id ?? "", title, value, User.Identity?.Name ?? "", DateTime.UtcNow);
        if (Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader))
            return Json(new { ok = said != null, said = said ?? NotSaved, version = _library.Version });
        if (said == null) TempData["Message"] = NotSaved;
        return LocalRedirect("/hauling");
    }

    const string NotSaved = "Not saved: the SCU is a whole number above 0 (no letters).";
}
