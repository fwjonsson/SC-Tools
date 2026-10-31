using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Mission Library, to look things up: every kind of mission the players took (from their logs, one row per name, or per contract line of the Hauling
/// Planner) with what the admin typed in about it (the reward in aUEC, MG scrip and Council scrip, its SCU, the faction; in its own rows for him)
/// and what the Star Citizen Wiki says (the faction, the category, the reward, the XP and the legality; WikiMissions). Read-only: for everybody when the
/// tool is shown (the admin's Tools page), like the Hauling Planner's live page, else only for the admin. The viewer chooses what to see (a search, a
/// faction, with or without rewards, the ones he played) and the order (a column's header), and sees how often he played
/// each one himself (nobody else's play). The list is kept until something in it changes (MissionLibrary.Entries), so the page comes at once, and it keeps
/// itself up to date (missions.js).
/// </summary>
[Route(Path)]
public sealed class MissionsController : Controller
{
    public const string Path = "/missions";

    readonly MissionLibrary _library;
    readonly ToolAccess _tools;

    public MissionsController(MissionLibrary library, ToolAccess tools)
    {
        _library = library;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <summary>How many kinds of mission a page of the library shows.</summary>
    public const int PageSize = 20;

    /// <summary>The name of the viewer logged in (a player by his tracker, or the admin), or null.</summary>
    public static string? ViewerOf(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(context.User.Identity.Name) ? context.User.Identity.Name : null;

    /// <summary>One page of the library as the viewer asked for it: what came in since the last time is in it.</summary>
    public static MissionLibraryPage PageOf(MissionLibrary library, LibraryFilter filter, int page, string? viewer, string path, bool editable)
    {
        library.Collect();   // what came in since the last refresh (nothing new: the list is not made again)
        var all = library.Entries();
        var found = filter.Apply(all, viewer).ToList();
        var pages = Math.Max(1, (found.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pages);
        var factions = all.Select(e => e.FactionShown).Where(g => !string.IsNullOrEmpty(g)).Select(g => g!).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
        return new MissionLibraryPage(found.Skip((page - 1) * PageSize).Take(PageSize).ToList(), filter, all.Count, found.Count, all.Count(e => e.NeedsInfo),
            all.Count(e => !e.NeedsInfo), viewer == null ? 0 : all.Count(e => e.PlaysOf(viewer) != null), page, pages, library.Version, factions, viewer, path, editable);
    }

    /// <param name="q">a part of the name, the faction or the category</param>
    /// <param name="faction">one faction (giver: as the address was before 29 Oct 2026)</param>
    /// <param name="rewards">all, known (with rewards) or todo (without rewards yet; show=todo as the address was)</param>
    /// <param name="mine">1: only the kinds the viewer played</param>
    /// <param name="sort">the column (LibraryFilter.Sorts; name when empty)</param>
    /// <param name="dir">asc or desc (empty: A to Z for a name, the biggest first for a number)</param>
    /// <param name="page">the page</param>
    [HttpGet("")]
    public IActionResult Index(string? q, string? faction, string? giver, string? rewards, string? mine, string? sort, string? dir, string? show, int page = 1)
    {
        if (!_tools.Sees(User, SiteTools.Missions)) return Hidden();
        ViewData["Title"] = "Mission Library";
        var viewer = ViewerOf(HttpContext);
        // the admin edits it right here, a field in every cell (Edit Mission Info, a page of its own, went on 31 Oct 2026: Frank)
        return View(PageOf(_library, LibraryFilter.From(q, faction ?? giver, rewards ?? show, mine, sort, dir, viewer != null), page, viewer, Path,
            editable: User.IsInRole(Roles.Admin)));
    }

    /// <summary>What the open page asks every few seconds: when the version is not the one it shows, it gets the list again (missions.js).</summary>
    [HttpGet("version")]
    public IActionResult Version()
    {
        if (!_tools.Sees(User, SiteTools.Missions)) return Hidden();
        Response.Headers.CacheControl = "no-store";
        return Json(new { version = _library.Version });
    }
}
