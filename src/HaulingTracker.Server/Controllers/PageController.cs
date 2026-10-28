using System.Net;
using HaulingTracker.Server.Accounts;
using HaulingTracker.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>
/// The pages for everybody: the landing page of the tools (/), the live page of the hauling tracker (/hauling), its data (data.json, new every two seconds)
/// and the icon. Watching needs no login, unless the admin hid the hauling tracker on the Tools page: then it is only for him.
/// </summary>
public sealed class PageController : Controller
{
    readonly LiveTracker _live;
    readonly ToolAccess _tools;

    public PageController(LiveTracker live, ToolAccess tools)
    {
        _live = live;
        _tools = tools;
    }

    /// <summary>A hidden tool's page: the login for somebody not logged in, "not for you" for a player.</summary>
    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <summary>The landing page: a card for every tool.</summary>
    [HttpGet("/")]
    public IActionResult Home()
    {
        var cards = SiteTools.All.Where(t => _tools.Sees(User, t)).Select(t => new HomeCard(t, !_tools.IsPublic(t))).ToList();
        return View("~/Views/Home/Index.cshtml", _live.Read(t => new HomeModel(t.State.Current == null ? "" : BoardTexts.DescribeSession(t.State.Current), cards)));
    }

    /// <summary>The live page of the hauling tracker, with the links of the login in its top right corner (Log in; My page for a player; the admin pages for the admin).</summary>
    [HttpGet("/hauling")]
    [HttpGet("/index.html")]
    public IActionResult Page() => _tools.Sees(User, SiteTools.Hauling) ? Content(WithLinks(WebAssets.Page), "text/html; charset=utf-8") : Hidden();

    string WithLinks(string page)
    {
        var links = new List<string> { "<a href=\"/\">SC Tools</a>" };   // back to the other tools
        if (User.Identity?.IsAuthenticated != true) links.Add("<a href=\"/login\">Log in</a>");
        else
        {
            links.Add("<span>" + WebUtility.HtmlEncode(User.Identity.Name ?? "") + "</span>");
            if (User.IsInRole(Roles.Player)) links.Add("<a href=\"/me\">My page</a>");
            if (User.IsInRole(Roles.Admin)) links.Add("<a href=\"/admin\">Admin</a>");
        }
        const string Heading = "<h1>Hauling Tracker</h1>";
        var nav = "<nav style=\"position:absolute;top:16px;right:20px;display:flex;gap:16px;font-weight:600;opacity:.85\">"
            + string.Join("", links).Replace("<a ", "<a style=\"color:inherit\" ") + "</nav>";
        page = page.Replace(Heading, Heading + nav);
        // who is looking, so the page can show his party first and his own runs on the Trip Planner (a player logged in by his tracker, or the admin)
        if (User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(User.Identity.Name))
            page = page.Replace("<head>", "<head>\n<meta name=\"me\" content=\"" + WebUtility.HtmlEncode(User.Identity.Name) + "\">");
        return page;
    }

    [HttpGet("/data.json")]
    public IActionResult Data()
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return StatusCode(StatusCodes.Status403Forbidden);
        Response.Headers.CacheControl = "no-store";
        return Content(_live.Json, "application/json; charset=utf-8");
    }

    [HttpGet("/favicon.ico")]
    [ResponseCache(Duration = 86400)]
    public IActionResult Icon() => WebAssets.Icon is { } icon ? File(icon, "image/x-icon") : NotFound();
}
