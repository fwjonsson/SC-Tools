using System.Security.Claims;
using SCTools.Server.Accounts;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// A tool of the site: its card on the landing page (with its colour and its icon, an SVG drawn with currentColor), and its page (Path null: not made yet,
/// the card says Coming).
/// </summary>
/// <param name="Text">what the tool does and how it is used: the landing page shows it when the mouse is over the tool's link (Frank, 30 Oct 2026)</param>
public sealed record SiteTool(string Id, string Name, string Text, string? Path, bool PublicByDefault, string Color, string Icon);

/// <summary>The tools of the site, in the order of their cards.</summary>
public static class SiteTools
{
    public static readonly SiteTool Hauling = new("hauling", "Hauling Planner",
        "Plans the crew's hauling together: the open contracts and stops, a route for every hauler, the cargo runs as they happen, the leaderboards and who owes whom. Keep the SC Tools Tracker running while you play: the planner follows your pickups and hand-ins from the game log, and a session starts and ends by itself.", "/hauling", true,
        "#4682ff", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><path d=\"M3 7l9-4 9 4-9 4-9-4z\"/><path d=\"M3 7v10l9 4 9-4V7\"/><path d=\"M12 11v10\"/></svg>");

    public static readonly SiteTool Session = new("session", "Session History",   // "Session" until 31 Oct 2026, at /session (Frank: the Companion has a Session tab)
        "Your own playtime and earnings: today, this week, this month and all time, game by game, with crashes and what your completed missions paid at the Mission Library's rewards.", "/history", false,
        "#8b949e", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"13\" r=\"8\"/><path d=\"M12 9v4l3 2M9 2h6\"/></svg>");

    public static readonly SiteTool Missions = new("missions", "Mission Library",
        "Every mission the crew has taken, one row per mission: the reward, MG and Council scrip, cargo in SCU, the faction and what the Star Citizen Wiki says. Search and sort it; the admin fills in what the log does not say, and the other tools count with those rewards.", "/missions", false,
        "#3fb8a4", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><rect x=\"5\" y=\"3\" width=\"14\" height=\"18\" rx=\"2\"/><path d=\"M9 8h6M9 12h6M9 16h4\"/></svg>");

    public static readonly SiteTool Gauntlet = new("gauntlet", "Gauntlet Tracker",
        "Tracks Combat Gauntlet runs from the game log: earnings, grind sessions, times by scenario with a top 10, what each scenario pays per hour, and a leaderboard of everybody. Nothing to press: run the tracker and play.", "/gauntlet", false,
        "#d9a441", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"12\" r=\"8\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 1v5M12 18v5M1 12h5M18 12h5\"/></svg>");

    public static readonly SiteTool Party = new("party", "Party",
        "Who is playing now and with whom (the game's own party), the ship each one flies, when everybody else was last seen, and a page per player.", "/party", false,
        "#b77fdb", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><circle cx=\"9\" cy=\"8\" r=\"3.2\"/><circle cx=\"17\" cy=\"9.5\" r=\"2.6\"/><path d=\"M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6M15 14.5c3 0 5.5 2 5.5 5\"/></svg>");

    public static readonly SiteTool Companion = new("companion", "Companion",
        "Your own page to keep open while you play: the map with you and your party, your contracts going on and the ones you finished this game session with which party member has which, and your session's playtime and earnings. It updates every few seconds.", "/companion", false,
        "#d6609a", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M15.5 8.5l-2 5-5 2 2-5 5-2z\"/><circle cx=\"12\" cy=\"12\" r=\"0.8\" fill=\"currentColor\"/></svg>");

    public static readonly IReadOnlyList<SiteTool> All = new[] { Hauling, Missions, Gauntlet, Session, Party, Companion };   // the ones that work first; the Map is an admin's page since 31 Oct 2026 (MapController)

    /// <summary>
    /// The tools with a card on the landing page and a link in the top bar: every tool but the Party, which is a button beside the viewer's name in the bar
    /// (TopBar.PartyButton) with its pages behind it (Frank, 29 Oct 2026). The Tools page still says who sees it.
    /// </summary>
    public static readonly IReadOnlyList<SiteTool> Cards = All.Where(t => t != Party).ToList();

    /// <summary>
    /// The tools in the top bar, in the order Frank set (30 Oct 2026): the ones used while playing first, the Companion left of the Hauling Planner (#380).
    /// The landing page's links come in the same order (Frank, the same day).
    /// </summary>
    public static readonly IReadOnlyList<SiteTool> Bar = new[] { Companion, Hauling, Gauntlet, Session, Missions };

    public static SiteTool? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Which tools everybody sees; the others only the admin (their cards on the landing page and their pages). Set on the Tools page (/admin/tools) and kept
/// in the database; a tool that was never set is as SiteTools says (the hauling tracker shown, the tools still being made only for the admin).
/// </summary>
public sealed class ToolAccess
{
    readonly DataStore _store;
    Dictionary<string, bool>? _public;

    public ToolAccess(DataStore store) => _store = store;

    Dictionary<string, bool> Public => _public ??= _store.ToolSettings();

    /// <summary>Everybody sees this tool.</summary>
    public bool IsPublic(SiteTool tool) => Public.TryGetValue(tool.Id, out var p) ? p : tool.PublicByDefault;

    /// <summary>This viewer sees the tool: everybody sees the public ones, the admin every one.</summary>
    public bool Sees(ClaimsPrincipal user, SiteTool tool) => IsPublic(tool) || user.IsInRole(Roles.Admin);

    /// <summary>Shows the tool to everybody, or only to the admin.</summary>
    public void SetPublic(SiteTool tool, bool value)
    {
        _store.SetToolPublic(tool.Id, value);
        _public = _store.ToolSettings();
    }
}
