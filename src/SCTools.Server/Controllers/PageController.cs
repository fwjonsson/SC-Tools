using System.Net;
using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace SCTools.Server.Controllers;

/// <summary>
/// The pages for everybody: the landing page of the tools (/), the live page of the hauling tracker (/hauling), its data (data.json, made anew when something
/// changed, polled with its ETag) and the icon. Watching needs no login, unless the admin hid the hauling tracker on the Tools page: then it is only for him.
/// </summary>
public sealed class PageController : Controller
{
    readonly LiveTracker _live;
    readonly ToolAccess _tools;
    readonly GauntletService _gauntlet;
    readonly MissionLibrary _missions;
    readonly Playtime _playtime;
    readonly IFileVersionProvider _files;

    public PageController(LiveTracker live, ToolAccess tools, GauntletService gauntlet, MissionLibrary missions, Playtime playtime, IFileVersionProvider files)
    {
        _files = files;
        _live = live;
        _tools = tools;
        _gauntlet = gauntlet;
        _missions = missions;
        _playtime = playtime;
    }

    /// <summary>A hidden tool's page: the login for somebody not logged in, "not for you" for a player.</summary>
    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <summary>The landing page: a card for every tool this viewer sees, with what it shows now.</summary>
    [HttpGet("/")]
    public IActionResult Home()
    {
        var cards = SiteTools.Cards.Where(t => _tools.Sees(User, t)).Select(t => new HomeCard(t, !_tools.IsPublic(t), State(t))).ToList();
        return View("~/Views/Home/Index.cshtml", new HomeModel(cards));
    }

    /// <summary>What a tool's card says now.</summary>
    string State(SiteTool tool)
    {
        if (tool.Path == null) return "Coming";
        if (tool == SiteTools.Hauling)
            return _live.Read(t => t.State.Current == null ? "No session yet" : BoardTexts.DescribeSession(t.State.Current));
        if (tool == SiteTools.Gauntlet)
        {
            if (User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(User.Identity.Name)) return "Log in to see your runs";
            var week = DateTime.UtcNow.AddDays(-7);
            var runs = _gauntlet.Runs(User.Identity.Name).Count(r => r.Outcome == GauntletOutcome.Complete && r.EndedUtc >= week);
            return runs == 0 ? "No runs this week" : runs == 1 ? "1 run this week" : runs + " runs this week";
        }
        if (tool == SiteTools.Session)
        {
            if (User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(User.Identity.Name)) return "Log in to see your play";
            var now = DateTime.UtcNow;
            var games = _playtime.Games(User.Identity.Name, now);
            if (games.FirstOrDefault(g => g.Running) is { } running)
                return "Playing for " + (running.Length.TotalHours >= 1 ? (int)running.Length.TotalHours + " h " : "") + running.Length.Minutes + " min";
            var week = SCTools.Play.PlayStats.Periods(now, TimeZoneInfo.Local)[1];
            var played = SCTools.Play.PlayStats.For(week, games, Array.Empty<SCTools.Play.EarnedMission>(), 0, 0).Played;
            return played.TotalMinutes < 1 ? "Nothing played this week" : played.TotalHours.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " h played this week";
        }
        if (tool == SiteTools.Missions)
        {
            var all = _missions.Entries();
            var todo = all.Count(e => e.NeedsInfo);
            return all.Count == 0 ? "No missions yet" : all.Count + (all.Count == 1 ? " mission" : " missions") + (todo > 0 ? ", " + todo + " without rewards" : ", all filled in");
        }
        return "";
    }

    /// <summary>The live page of the hauling tracker, with the bar of the site at the top (SC Tools, the tools, who is logged in).</summary>
    [HttpGet("/hauling")]
    [HttpGet("/index.html")]
    public IActionResult Page() => _tools.Sees(User, SiteTools.Hauling) ? Content(WithLinks(WebAssets.Page), "text/html; charset=utf-8") : Hidden();

    string WithLinks(string page)
    {
        // the bar of the site at the top, as on every page (SC Tools back to the cards, the tools, who is logged in), with its look. The look's address has
        // its version in it (as asp-append-version on the other pages): Cloudflare and the browsers keep a .css for hours, and an old one with the new bar
        // is a mess (the menu button and a big download arrow on a computer)
        var css = WebUtility.HtmlEncode(_files.AddFileVersionToPath(Request.PathBase, "/topbar.css"));
        page = page.Replace("</head>", "<link rel=\"stylesheet\" href=\"" + css + "\">\n</head>")
            .Replace("<body>", "<body class=\"tool\">\n" + TopBar.Html(HttpContext, _tools, SiteTools.Hauling));
        // who is looking, so the page can show his party first and his own runs on the Trip Planner (a player logged in by his tracker, or the admin)
        if (User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(User.Identity.Name))
            page = page.Replace("<head>", "<head>\n<meta name=\"me\" content=\"" + WebUtility.HtmlEncode(User.Identity.Name) + "\">"
                + (User.IsInRole(Roles.Admin) ? "\n<meta name=\"admin\" content=\"1\">" : ""));   // the admin picks whose session the tabs show
        return page;
    }

    static readonly string[] HaulingTabs = { "session", "contracts", "planner", "debts", "history" };

    /// <summary>A tab of the live page at its own address (/hauling/planner): the same page, which shows that tab.</summary>
    [HttpGet("/hauling/{tab}")]
    public IActionResult Tab(string tab) => HaulingTabs.Contains(tab) ? Page() : NotFound();

    /// <summary>
    /// The live page's data, made anew only when something changed (LiveTracker.Refresh). The page sends back the ETag it got (If-None-Match): the same one
    /// means the same data, and the answer is 304 with no body, so a poll that finds nothing new costs next to nothing on both sides.
    /// </summary>
    [HttpGet("/data.json")]
    public IActionResult Data()
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return StatusCode(StatusCodes.Status403Forbidden);
        var snapshot = _live.Snapshot;
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ETag = snapshot.ETag;
        if (Request.Headers.IfNoneMatch.Any(h => h != null && h.Split(',').Any(v => v.Trim() == snapshot.ETag)))
            return StatusCode(StatusCodes.Status304NotModified);
        return File(snapshot.Bytes, "application/json; charset=utf-8");
    }

    [HttpGet("/favicon.ico")]
    [ResponseCache(Duration = 86400)]
    public IActionResult Icon() => WebAssets.Icon is { } icon ? File(icon, "image/x-icon") : NotFound();
}
