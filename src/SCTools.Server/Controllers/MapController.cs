using System.Text.Json;
using System.Text.Json.Nodes;
using SCTools.GameData;
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

    /// <summary>
    /// The map's data for this viewer: the places, the players he may see with their objectives (DataViews.MapFor; the admin everybody), and who he is
    /// (me: the player the admin looks as, else who is logged in, null for a visitor; as the Companion's feed has it), for the parts of the map that are
    /// his own, such as measuring a distance with his own ship (#408; Frank, 31 Oct 2026).
    /// </summary>
    [HttpGet("map.json")]
    public IActionResult Data()
    {
        if (!_tools.Sees(User, SiteTools.Map)) return Hidden();
        Response.Headers.CacheControl = "no-store";
        var viewer = Audience.Of(HttpContext);
        var now = DateTime.UtcNow;
        var map = _map.Snapshot(now);
        var node = viewer.Everything ? JsonSerializer.SerializeToNode(map, JsonSerializerOptions.Web) : DataViews.MapFor(map, _audience.Allowed(viewer, now));
        if (node is JsonObject d)
        {
            var me = PointOfView.Name(HttpContext);
            d["me"] = me;
            if (me != null) d["mine"] = JsonSerializer.SerializeToNode(_map.Mine(me), JsonSerializerOptions.Web);   // his ship and drive, for measuring a distance (#408)
        }
        return Json(node);
    }

    /// <summary>
    /// What every station of the map has, by the place's key: its services and its shops as UEX lists them (StationServices), for the card the map shows when
    /// the mouse is over a station (#409; Frank, 31 Oct 2026). Game data, the same for everybody and every page with a map, so no login is needed and a
    /// browser may keep it for an hour.
    /// </summary>
    [HttpGet("services.json")]
    public IActionResult Services()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Json(StationServices.ByKey.ToDictionary(kv => kv.Key, kv => new { name = kv.Value.Name, services = kv.Value.Services, shops = kv.Value.Shops }));
    }
}
