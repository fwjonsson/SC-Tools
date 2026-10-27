using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>The live page for everybody: the page itself, its data (data.json, new every two seconds) and its icon. Watching needs no login.</summary>
public sealed class PageController : Controller
{
    readonly LiveTracker _live;

    public PageController(LiveTracker live) => _live = live;

    [HttpGet("/")]
    [HttpGet("/index.html")]
    public IActionResult Page() => Content(WebAssets.Page, "text/html; charset=utf-8");

    [HttpGet("/data.json")]
    public IActionResult Data()
    {
        Response.Headers.CacheControl = "no-store";
        return Content(_live.Json, "application/json; charset=utf-8");
    }

    [HttpGet("/favicon.ico")]
    [ResponseCache(Duration = 86400)]
    public IActionResult Icon() => WebAssets.Icon is { } icon ? File(icon, "image/x-icon") : NotFound();
}
