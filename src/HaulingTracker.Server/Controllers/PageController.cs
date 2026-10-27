using System.Net;
using HaulingTracker.Server.Accounts;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>The live page for everybody: the page itself, its data (data.json, new every two seconds) and its icon. Watching needs no login.</summary>
public sealed class PageController : Controller
{
    readonly LiveTracker _live;

    public PageController(LiveTracker live) => _live = live;

    /// <summary>The page, with the links of the login in its top right corner (Log in; My page for a player; the admin pages for the admin).</summary>
    [HttpGet("/")]
    [HttpGet("/index.html")]
    public IActionResult Page() => Content(WithLinks(WebAssets.Page), "text/html; charset=utf-8");

    string WithLinks(string page)
    {
        var links = new List<string>();
        if (User.Identity?.IsAuthenticated != true) links.Add("<a href=\"/login\">Log in</a>");
        else
        {
            links.Add("<span>" + WebUtility.HtmlEncode(User.Identity.Name ?? "") + "</span>");
            if (User.IsInRole(Roles.Player)) links.Add("<a href=\"/me\">My page</a>");
            if (User.IsInRole(Roles.Admin)) links.Add("<a href=\"/admin/keys\">Admin</a>");
        }
        const string Heading = "<h1>Hauling Tracker</h1>";
        var nav = "<nav style=\"position:absolute;top:16px;right:20px;display:flex;gap:16px;font-weight:600;opacity:.85\">"
            + string.Join("", links).Replace("<a ", "<a style=\"color:inherit\" ") + "</nav>";
        return page.Replace(Heading, Heading + nav);
    }

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
