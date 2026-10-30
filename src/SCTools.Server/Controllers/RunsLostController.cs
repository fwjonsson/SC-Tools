using SCTools.Server.Accounts;
using SCTools.Server.Editing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Lost Cargo pick of a cargo run on the Hauling Planner's Session tab (hauling.js lostPicks): the admin picks None, Little or All right in the table
/// instead of on his Session page (Frank, 29 Oct 2026), sent in the background (X-Autosave: JSON back), else a redirect to the tab. The same edit as the
/// admin page's (Edits.SetLost): All ends a run still on the way.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route("/hauling/runs")]
public sealed class RunsLostController : Controller
{
    readonly LiveTracker _live;

    public RunsLostController(LiveTracker live)
    {
        _live = live;
    }

    /// <param name="key">the run (Trip.Key)</param>
    /// <param name="level">None, Little or All</param>
    [HttpPost("lost")]
    public IActionResult Lost(string key, string level)
    {
        var said = "";
        _live.Edit(t => said = Edits.SetLost(t, key ?? "", level ?? ""), DateTime.UtcNow);
        var ok = said.Contains("lost cargo", StringComparison.Ordinal);
        if (Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader)) return Json(new { ok, said });
        TempData["Message"] = said;
        return LocalRedirect("/hauling/session");
    }
}
