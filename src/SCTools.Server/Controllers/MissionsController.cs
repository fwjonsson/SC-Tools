using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Mission Library: every kind of mission the players took (from their logs, one row per name, or per contract line of the Hauling Planner), where a
/// logged in player types in what the log does not say: the aUEC, MG scrip and Council scrip it pays, another name, the giver, notes. Every field is saved
/// as soon as it changes (autosave.js: in the background, so the page stays where it is and is not made again). The list is kept until something in it
/// changes (MissionLibrary.Entries: it is made from every mission in the database), so the page comes at once.
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[AutoValidateAntiforgeryToken]
[Route("/missions")]
public sealed class MissionsController : Controller
{
    readonly MissionLibrary _library;
    readonly ToolAccess _tools;

    public MissionsController(MissionLibrary library, ToolAccess tools)
    {
        _library = library;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <summary>A field sent in the background by autosave.js (data-autosave="quiet") has this header: the answer is what to say, not the page again.</summary>
    public const string AutosaveHeader = "X-Autosave";

    const string NotSaved = "Not saved: a reward or SCU is a whole number (no letters).";

    /// <summary>How many kinds of mission a page of the library shows.</summary>
    public const int PageSize = 20;

    /// <param name="show">all, or todo: the ones without rewards yet</param>
    /// <param name="q">a part of the name or the giver</param>
    /// <param name="page">the page (1: the ones played last)</param>
    [HttpGet("")]
    public IActionResult Index(string? show, string? q, int page = 1)
    {
        if (!_tools.Sees(User, SiteTools.Missions)) return Hidden();
        _library.Collect();   // what came in since the last refresh (nothing new: the list is not made again)
        var all = _library.Entries();
        var list = all.AsEnumerable();
        if (show == "todo") list = list.Where(e => e.NeedsInfo);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            list = list.Where(e => e.Shown.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Row.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
                || (e.Row.Giver ?? "").Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        ViewData["Title"] = "Mission Library";
        var found = list.ToList();
        var pages = Math.Max(1, (found.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pages);
        return View(new MissionLibraryPage(found.Skip((page - 1) * PageSize).Take(PageSize).ToList(), all.Count, all.Count(e => e.NeedsInfo),
            show == "todo" ? "todo" : "all", q?.Trim() ?? "", page, pages, found.Count, _library.Version,
            all.Select(e => e.Row.Giver?.Trim()).Where(g => !string.IsNullOrEmpty(g)).Select(g => g!).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList()));
    }

    /// <summary>What the open page asks every few seconds: when the version is not the one it shows, it gets the list again (missions.js).</summary>
    [HttpGet("version")]
    public IActionResult Version()
    {
        if (!_tools.Sees(User, SiteTools.Missions)) return Hidden();
        Response.Headers.CacheControl = "no-store";
        return Json(new { version = _library.Version });
    }

    [HttpPost("set")]
    public IActionResult Set(string id, string field, string? value, string? back)
    {
        if (!_tools.Sees(User, SiteTools.Missions)) return Hidden();
        var said = _library.Set(id ?? "", field ?? "", value, User.Identity?.Name ?? "", DateTime.UtcNow);
        if (Request.Headers.ContainsKey(AutosaveHeader))   // saved in the background (autosave.js): what to say, the page stays as it is
            return Json(new { ok = said != null, said = said ?? NotSaved, version = _library.Version });
        if (said == null) TempData["Message"] = NotSaved;
        var anchor = "#" + MissionLibrary.Anchor(id ?? "");
        return LocalRedirect(back is { Length: > 0 } && back.StartsWith("/missions", StringComparison.Ordinal) ? back + anchor : "/missions" + anchor);
    }
}
