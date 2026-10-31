using System.Text.Json;
using System.Text.Json.Nodes;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The pages for everybody: the landing page of the tools (/), the Hauling Planner's page (/hauling, an ordinary page of the site: Views/Hauling, with
/// hauling.js and hauling.css in wwwroot) and its data (data.json, made anew when something changed, polled with its ETag). Watching needs no login, unless
/// the admin hid the Hauling Planner on the Tools page: then it is only for him.
/// </summary>
public sealed class PageController : Controller
{
    readonly LiveTracker _live;
    readonly ToolAccess _tools;
    readonly GauntletService _gauntlet;
    readonly MissionLibrary _missions;
    readonly Playtime _playtime;
    readonly MapService _map;
    readonly Audience _audience;
    readonly DataViews _views;

    public PageController(LiveTracker live, ToolAccess tools, GauntletService gauntlet, MissionLibrary missions, Playtime playtime, MapService map,
        Audience audience, DataViews views)
    {
        _map = map;
        _audience = audience;
        _views = views;
        _live = live;
        _tools = tools;
        _gauntlet = gauntlet;
        _missions = missions;
        _playtime = playtime;
    }

    /// <summary>A hidden tool's page: the login for somebody not logged in, "not for you" for a player.</summary>
    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <summary>The landing page: a link for every tool this viewer sees, in the order of the top bar (Frank, 30 Oct 2026), with what it shows now.</summary>
    [HttpGet("/")]
    public IActionResult Home()
    {
        var cards = SiteTools.Bar.Where(t => _tools.Sees(User, t)).Select(t => new HomeCard(t, !_tools.IsPublic(t), State(t))).ToList();
        return View("~/Views/Home/Index.cshtml", new HomeModel(cards));
    }

    /// <summary>What a tool's card says now.</summary>
    string State(SiteTool tool)
    {
        if (tool.Path == null) return "Coming";
        if (tool == SiteTools.Hauling)
        {
            // the session is the party's: a visitor is asked to log in, a player sees his party's (Frank, 30 Oct 2026)
            var viewer = Audience.Of(HttpContext);
            if (viewer.Visitor) return "Log in to see your party's session";
            var (bytes, _) = _views.For(_live.Snapshot, viewer, viewer.Everything ? null : _audience.Allowed(viewer, DateTime.UtcNow));
            var session = System.Text.Json.JsonDocument.Parse(bytes).RootElement.TryGetProperty("session", out var s) ? s.GetString() : null;
            return string.IsNullOrEmpty(session) ? "No session yet" : session;
        }
        if (tool == SiteTools.Gauntlet)
        {
            if (PointOfView.Name(HttpContext) is not { Length: > 0 } me) return "Log in to see your runs";
            var week = DateTime.UtcNow.AddDays(-7);
            var runs = _gauntlet.Runs(me).Count(r => r.Outcome == GauntletOutcome.Complete && r.EndedUtc >= week);
            return runs == 0 ? "No runs this week" : runs == 1 ? "1 run this week" : runs + " runs this week";
        }
        if (tool == SiteTools.Session)
        {
            if (PointOfView.Name(HttpContext) is not { Length: > 0 } me) return "Log in to see your play";
            var now = DateTime.UtcNow;
            var games = _playtime.Games(me, now);
            if (games.FirstOrDefault(g => g.Running) is { } running)
                return "Playing for " + (running.Length.TotalHours >= 1 ? (int)running.Length.TotalHours + " h " : "") + running.Length.Minutes + " min";
            var week = SCTools.Play.PlayStats.Periods(now, TimeZoneInfo.Local)[1];
            var played = SCTools.Play.PlayStats.For(week, games, Array.Empty<SCTools.Play.EarnedMission>(), 0, 0).Played;
            return played.TotalMinutes < 1 ? "Nothing played this week" : played.TotalHours.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " h played this week";
        }
        if (tool == SiteTools.Companion)
        {
            if (PointOfView.Name(HttpContext) is not { Length: > 0 } me) return "Log in to keep it open while you play";
            var now = DateTime.UtcNow;
            return SCTools.Play.GameSession.Latest(_playtime.Games(me, now), now)?.Running != null ? "Your game session is going on" : "Keep it open while you play";
        }
        if (tool == SiteTools.Missions)
        {
            var all = _missions.Entries();
            var todo = all.Count(e => e.NeedsInfo);
            return all.Count == 0 ? "No missions yet" : all.Count + (all.Count == 1 ? " mission" : " missions") + (todo > 0 ? ", " + todo + " without rewards" : ", all filled in");
        }
        return "";
    }

    /// <summary>The Hauling Planner's page: the site's layout (the bar at the top, the footer) around its tables and tabs.</summary>
    [HttpGet("/hauling")]
    public IActionResult Page() => _tools.Sees(User, SiteTools.Hauling) ? View("~/Views/Hauling/Index.cshtml") : Hidden();

    static readonly string[] HaulingTabs = { "session", "planner", "debts", "history", "leaderboard", "contracts" };   // contracts: an old link, the script shows the Session tab (30 Oct 2026)

    /// <summary>A tab of the page at its own address (/hauling/planner): the same page, which shows that tab.</summary>
    [HttpGet("/hauling/{tab}")]
    public IActionResult Tab(string tab) => HaulingTabs.Contains(tab) ? Page() : NotFound();

    /// <summary>
    /// The page's data, made anew only when something changed (LiveTracker.Refresh). The page sends back the ETag it got (If-None-Match): the same one
    /// means the same data, and the answer is 304 with no body, so a poll that finds nothing new costs next to nothing on both sides.
    /// </summary>
    [HttpGet("/data.json")]
    public IActionResult Data()
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return StatusCode(StatusCodes.Status403Forbidden);
        // as this viewer may see it (Audience, DataViews): the admin all of it, a player his party's session and his own debts, a visitor the maps and the
        // all-time leaderboard; each with an ETag of its own
        var viewer = Audience.Of(HttpContext);
        var (bytes, etag) = _views.For(_live.Snapshot, viewer, viewer.Everything ? null : _audience.Allowed(viewer, DateTime.UtcNow));
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ETag = etag;
        if (Matches(Request.Headers.IfNoneMatch, etag)) return StatusCode(StatusCodes.Status304NotModified);
        return File(bytes, "application/json; charset=utf-8");
    }

    /// <summary>
    /// True when the page already has this version (If-None-Match names the ETag). Cloudflare compresses the answer on its way to the browser and then
    /// calls the ETag weak (W/"..."), and the browser sends it back that way: the W/ does not matter for "is it the same data", so it is left out of the
    /// compare (it was not, and no poll was ever answered 304 on the site: Frank, 30 Oct 2026).
    /// </summary>
    public static bool Matches(IEnumerable<string?> ifNoneMatch, string etag)
    {
        foreach (var header in ifNoneMatch)
        {
            if (header == null) continue;
            foreach (var part in header.Split(','))
            {
                var tag = part.Trim();
                if (tag.StartsWith("W/", StringComparison.Ordinal)) tag = tag[2..];
                if (tag == etag || tag == "*") return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The map of the Trip Planner tab: the Map tool's data for Pyro alone (MapService.Snapshot: everybody playing where the location tracker has him, the objectives
    /// going on), polled every few seconds while the tab is shown, by whoever sees the Hauling Planner; the page draws the planner's routes on it.
    /// </summary>
    [HttpGet("/hauling/map.json")]
    public IActionResult PlannerMap()
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return StatusCode(StatusCodes.Status403Forbidden);
        Response.Headers.CacheControl = "no-store";
        // every system since stage 2 (the Route Planner draws routes through the gateways); the players of the viewer's party only, a visitor none
        var viewer = Audience.Of(HttpContext);
        var now = DateTime.UtcNow;
        var map = _map.Snapshot(now);
        var node = viewer.Everything ? JsonSerializer.SerializeToNode(map, JsonSerializerOptions.Web) : DataViews.MapFor(map, _audience.Allowed(viewer, now));
        if (node is JsonObject d) d["me"] = PointOfView.Name(HttpContext);   // who the viewer is, as the Map tool's feed says it: the two stay one answer
        return Json(node);
    }

}
