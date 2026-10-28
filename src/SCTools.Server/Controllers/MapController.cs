using SCTools.Server.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Map tool: the Pyro system with everybody playing where his quantum jumps put him (moving along his route while he is in quantum, with his speed) and
/// the objectives of the missions going on (MapService). The page asks for map.json every few seconds and moves the players in between. For a logged in
/// player or the admin, and only for the admin until it is shown (the Tools page).
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[Route("/map")]
public sealed class MapController : Controller
{
    readonly MapService _map;
    readonly ToolAccess _tools;

    public MapController(MapService map, ToolAccess tools)
    {
        _map = map;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!_tools.Sees(User, SiteTools.Map)) return Hidden();
        ViewData["Title"] = "Map";
        return View();
    }

    [HttpGet("map.json")]
    public IActionResult Data()
    {
        if (!_tools.Sees(User, SiteTools.Map)) return Hidden();
        Response.Headers.CacheControl = "no-store";
        return Json(_map.Snapshot(DateTime.UtcNow));
    }
}
