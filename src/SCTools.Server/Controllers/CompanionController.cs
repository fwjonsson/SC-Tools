using SCTools.Server.Accounts;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Companion (#380, Frank 30 Oct 2026): a player's own page to keep open while playing, started as a copy of the Map tool. The map with him and his party
/// (it follows him to another system while it shows the one he is in), the Players list, the objectives, his party's Active Contracts and the ones it ended
/// this game session (Completed Contracts, with who had each), and his game session's playtime and earnings (CompanionService). The admin fills in the
/// Mission Library's fields of a contract right in its row (sent to Edit Mission Info's address in the background, autosave.js). A visitor sees the map's
/// places and a note to log in; the admin's View as shows exactly what that player sees.
/// </summary>
[Route("/companion")]
public sealed class CompanionController : Controller
{
    readonly CompanionService _companion;
    readonly ToolAccess _tools;

    public CompanionController(CompanionService companion, ToolAccess tools)
    {
        _companion = companion;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <summary>Whose Companion this is: the player the admin looks as, else who is logged in (the admin's own player); null for a visitor.</summary>
    string? Me => PointOfView.Name(HttpContext);

    /// <summary>The admin may change the library from it, also while he looks as a player (what a page lets the viewer change goes by who is logged in).</summary>
    bool Editor => User.IsInRole(Roles.Admin);

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!_tools.Sees(User, SiteTools.Companion)) return Hidden();
        ViewData["Title"] = "Companion";
        ViewData["Visitor"] = Me == null;
        ViewData["Editor"] = Editor;
        return View(Editor ? _companion.Factions() : new List<string>());
    }

    [HttpGet("map.json")]
    public IActionResult Map()
    {
        if (!_tools.Sees(User, SiteTools.Companion)) return Hidden();
        Response.Headers.CacheControl = "no-store";
        return Json(_companion.Map(Me, Editor, DateTime.UtcNow));
    }

    /// <summary>The game session and the contracts ended in it, polled with its ETag (the same data again is a 304, PageController.Matches).</summary>
    [HttpGet("session.json")]
    public IActionResult Session()
    {
        if (!_tools.Sees(User, SiteTools.Companion)) return Hidden();
        var (bytes, etag) = _companion.Session(Me, Editor, DateTime.UtcNow);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ETag = etag;
        if (PageController.Matches(Request.Headers.IfNoneMatch, etag)) return StatusCode(StatusCodes.Status304NotModified);
        return File(bytes, "application/json; charset=utf-8");
    }
}
