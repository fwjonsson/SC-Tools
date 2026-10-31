using SCTools.Server.Accounts;
using SCTools.Server.Editing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Start Time of the Hauling Planner's session, a field in its Session table for the admin (hauling.js; Frank, 31 Oct 2026: his Session page, where it
/// was, went): 19:30, or empty for the start the logs give. Sent in the background (X-Autosave: JSON back), else a redirect to the tab.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route("/hauling/session")]
public sealed class SessionStartController : Controller
{
    readonly LiveTracker _live;

    public SessionStartController(LiveTracker live) => _live = live;

    [HttpPost("start")]
    public IActionResult Start(string? startTime)
    {
        var said = "";
        _live.Edit(t => said = Edits.SetStartTime(t, startTime), DateTime.UtcNow);
        var ok = !said.StartsWith("Type", StringComparison.Ordinal) && !said.StartsWith("There is no", StringComparison.Ordinal);
        if (Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader)) return Json(new { ok, said });
        TempData["Message"] = said;
        return LocalRedirect("/hauling/session");
    }
}
