using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// Where the admin's fields of the Mission Library are saved (admin only): the aUEC, MG scrip and Council scrip a kind of mission pays, its SCU, another name,
/// the faction (it wins over the Star Citizen Wiki's). The fields are in the Mission Library's own rows for him (/missions, MissionsController) and in the
/// Companion's contracts; each is saved as soon as it changes (autosave.js: in the background, so the page stays where it is). Edit Mission Info, the
/// admin's page of its own at this address, went on 31 Oct 2026 (Frank: the library is where he edits it).
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route(Path)]
public sealed class EditMissionsController : Controller
{
    public const string Path = "/admin/missions";

    readonly MissionLibrary _library;

    public EditMissionsController(MissionLibrary library) => _library = library;

    /// <summary>A field sent in the background by autosave.js (data-autosave="quiet") has this header: the answer is what to say, not the page again.</summary>
    public const string AutosaveHeader = "X-Autosave";

    const string NotSaved = "Not saved: a reward or SCU is a whole number (no letters).";

    [HttpPost("set")]
    public IActionResult Set(string id, string field, string? value, string? back)
    {
        var said = _library.Set(id ?? "", field ?? "", value, User.Identity?.Name ?? "", DateTime.UtcNow);
        if (Request.Headers.ContainsKey(AutosaveHeader))   // saved in the background (autosave.js): what to say, the page stays as it is
            return Json(new { ok = said != null, said = said ?? NotSaved, version = _library.Version });
        if (said == null) TempData["Message"] = NotSaved;
        var anchor = "#" + MissionLibrary.Anchor(id ?? "");
        return LocalRedirect(back is { Length: > 0 } && back.StartsWith(MissionsController.Path, StringComparison.Ordinal) ? back + anchor : MissionsController.Path + anchor);
    }
}
