using System.Security.Claims;
using HaulingTracker.Server.Accounts;
using HaulingTracker.Server.Data;

namespace HaulingTracker.Server;

/// <summary>A tool of the site: its card on the landing page, and its page (Path null: not made yet, the card says Coming).</summary>
public sealed record SiteTool(string Id, string Name, string Text, string? Path, bool PublicByDefault);

/// <summary>The tools of the site, in the order of their cards.</summary>
public static class SiteTools
{
    public static readonly SiteTool Hauling = new("hauling", "Hauling Tracker",
        "Hauling sessions live: the stops, the cargo runs, the Trip Planner with everybody's routes, the leaderboards and who owes whom.", "/hauling", true);

    public static readonly SiteTool Session = new("session", "Session",
        "Any kind of play: the contracts you took, finished and failed, time played, deaths, crashes, and what it earned per hour.", null, false);

    public static readonly SiteTool Missions = new("missions", "Mission Library",
        "Every mission you take, with what the log does not say (reward, mission giver), filled in from the Star Citizen Wiki and by you.", null, false);

    public static readonly SiteTool Gauntlet = new("gauntlet", "Gauntlet Tracker",
        "Gilly's Combat Gauntlet, tracked from the log: the Gauntlet Tracker program, coming onto the site.", null, false);

    public static readonly IReadOnlyList<SiteTool> All = new[] { Hauling, Session, Missions, Gauntlet };

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
