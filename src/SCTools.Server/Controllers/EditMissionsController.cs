using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// Edit Mission Info (admin only): the Mission Library as the admin changes it, the same list with the same choices and order as the read-only one
/// (/missions, MissionsController), but with a field in every cell, where he types in what the log does not say: the aUEC, MG scrip and Council scrip a
/// kind of mission pays, its SCU, another name, the faction (the Giver field as it was called: it wins over the Star Citizen Wiki's faction). Every field is
/// saved as soon as it changes (autosave.js: in the background, so the page stays
/// where it is and is not made again).
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

    /// <summary>The same choices as the read-only library's (see MissionsController.Index).</summary>
    [HttpGet("")]
    public IActionResult Index(string? q, string? faction, string? giver, string? rewards, string? mine, string? sort, string? dir, string? show, int page = 1)
    {
        ViewData["Title"] = "Edit Mission Info";
        var viewer = MissionsController.ViewerOf(HttpContext);
        return View("~/Views/Missions/Index.cshtml",
            MissionsController.PageOf(_library, LibraryFilter.From(q, faction ?? giver, rewards ?? show, mine, sort, dir, viewer != null), page, viewer, Path, editable: true));
    }

    /// <summary>What the open page asks every few seconds (missions.js), as on the read-only library.</summary>
    [HttpGet("version")]
    public IActionResult Version()
    {
        Response.Headers.CacheControl = "no-store";
        return Json(new { version = _library.Version });
    }

    [HttpPost("set")]
    public IActionResult Set(string id, string field, string? value, string? back)
    {
        var said = _library.Set(id ?? "", field ?? "", value, User.Identity?.Name ?? "", DateTime.UtcNow);
        if (Request.Headers.ContainsKey(AutosaveHeader))   // saved in the background (autosave.js): what to say, the page stays as it is
            return Json(new { ok = said != null, said = said ?? NotSaved, version = _library.Version });
        if (said == null) TempData["Message"] = NotSaved;
        var anchor = "#" + MissionLibrary.Anchor(id ?? "");
        return LocalRedirect(back is { Length: > 0 } && back.StartsWith(Path, StringComparison.Ordinal) ? back + anchor : Path + anchor);
    }
}
