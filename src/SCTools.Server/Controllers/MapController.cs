using SCTools.Server.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Map tool: the Pyro system with everybody playing where his quantum jumps put him (moving along his route while he is in quantum, with his speed) and
/// the objectives of the missions going on (MapService). The page asks for map.json every few seconds and moves the players in between. For everybody
/// once it is shown (the Tools page; only the admin before): a visitor sees the places, a player the players of his party, the admin everybody (Audience).
/// </summary>
[Route("/map")]
public sealed class MapController : Controller
{
    readonly MapService _map;
    readonly ToolAccess _tools;
    readonly Audience _audience;

    public MapController(MapService map, ToolAccess tools, Audience audience)
    {
        _map = map;
        _tools = tools;
        _audience = audience;
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
        var viewer = Audience.Of(HttpContext);
        var now = DateTime.UtcNow;
        var map = _map.Snapshot(now);
        return viewer.Everything ? Json(map) : Json(DataViews.MapFor(map, _audience.Allowed(viewer, now)));
    }
}
