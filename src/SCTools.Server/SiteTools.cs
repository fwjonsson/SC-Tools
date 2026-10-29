using System.Security.Claims;
using SCTools.Server.Accounts;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// A tool of the site: its card on the landing page (with its colour and its icon, an SVG drawn with currentColor), and its page (Path null: not made yet,
/// the card says Coming).
/// </summary>
public sealed record SiteTool(string Id, string Name, string Text, string? Path, bool PublicByDefault, string Color, string Icon);

/// <summary>The tools of the site, in the order of their cards.</summary>
public static class SiteTools
{
    public static readonly SiteTool Hauling = new("hauling", "Hauling Planner",
        "The party's hauling, live: the stops and contracts to and from Ruin, the Other hauling board of every system, the Trip Planner with everybody's routes, the cargo runs, the leaderboards and who owes whom. A session starts and ends by itself.", "/hauling", true,
        "#4682ff", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><path d=\"M3 7l9-4 9 4-9 4-9-4z\"/><path d=\"M3 7v10l9 4 9-4V7\"/><path d=\"M12 11v10\"/></svg>");

    public static readonly SiteTool Session = new("session", "Session",
        "Your own play: time played today, this week, this month and in all, your games and crashes, and what your completed missions earned (aUEC, MG and Council scrip) at the Mission Library's rewards, per hour too.", "/session", false,
        "#8b949e", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"13\" r=\"8\"/><path d=\"M12 9v4l3 2M9 2h6\"/></svg>");

    public static readonly SiteTool Missions = new("missions", "Mission Library",
        "Every mission we take, one row per mission, with what the log does not say: the reward, MG and Council scrip, cargo in SCU and who gives it, from the Star Citizen Wiki and what we fill in. Search it, sort it, and see your own plays.", "/missions", false,
        "#3fb8a4", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><rect x=\"5\" y=\"3\" width=\"14\" height=\"18\" rx=\"2\"/><path d=\"M9 8h6M9 12h6M9 16h4\"/></svg>");

    public static readonly SiteTool Gauntlet = new("gauntlet", "Gauntlet Tracker",
        "Gilly's Combat Gauntlet from your log: your runs and grind sessions, times by scenario with a top 10, what each scenario earns per hour, and a leaderboard of everybody.", "/gauntlet", false,
        "#d9a441", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"12\" r=\"8\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 1v5M12 18v5M1 12h5M18 12h5\"/></svg>");

    public static readonly SiteTool Party = new("party", "Party",
        "Who is playing now and with whom (the game's own party), the ship each one flies, when everybody else was last seen, and a page per player.", "/party", false,
        "#b77fdb", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><circle cx=\"9\" cy=\"8\" r=\"3.2\"/><circle cx=\"17\" cy=\"9.5\" r=\"2.6\"/><path d=\"M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6M15 14.5c3 0 5.5 2 5.5 5\"/></svg>");

    public static readonly SiteTool Map = new("map", "Map",
        "Pyro, Stanton and Nyx from above: where everybody is by his quantum jumps, moving along his route at his drive's speed, and the objectives of the missions going on.", "/map", false,
        "#e0734a", "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"12\" r=\"2.5\"/><circle cx=\"12\" cy=\"12\" r=\"6\"/><circle cx=\"12\" cy=\"12\" r=\"9.5\"/><circle cx=\"18\" cy=\"8.4\" r=\"1.4\" fill=\"currentColor\"/></svg>");

    public static readonly IReadOnlyList<SiteTool> All = new[] { Hauling, Missions, Gauntlet, Session, Party, Map };   // the ones that work first

    /// <summary>
    /// The tools with a card on the landing page and a link in the top bar: every tool but the Party, which is a button beside the viewer's name in the bar
    /// (TopBar.PartyButton) with its pages behind it (Frank, 29 Oct 2026). The Tools page still says who sees it.
    /// </summary>
    public static readonly IReadOnlyList<SiteTool> Cards = All.Where(t => t != Party).ToList();

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
