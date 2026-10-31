using SCTools.Server.Accounts;

namespace SCTools.Server;

/// <summary>
/// Who looks at a page, for what the page may show him (Frank, 30 Oct 2026): everybody, logged in or not, sees every tool that is shown on the Tools page,
/// but what the players do is only for the players it is about. A visitor (not logged in) sees the tools without anybody's data, apart from the public
/// leaderboards (the Hauling Planner's all-time one and the Gauntlet Tracker's); a player sees his own and his party's; the admin sees everybody's, and with
/// the top bar's View pick (PointOfView) exactly what that player sees.
/// </summary>
/// <param name="Name">the player the page is for (null: a visitor, or the admin's own view)</param>
/// <param name="Everything">the admin's own view: everybody's data</param>
public sealed record Viewer(string? Name, bool Everything)
{
    /// <summary>Not logged in: the tools, the public leaderboards, nobody's data.</summary>
    public bool Visitor => Name == null && !Everything;

    /// <summary>A key for what this viewer sees, the same for every viewer who sees the same ("all", "visitor", "p:zzoega").</summary>
    public string Key => Everything ? "all" : Name == null ? "visitor" : "p:" + Name.ToLowerInvariant();
}

/// <summary>Works out the viewer of a request and the players he may see: himself and his party (Audience.PartyOf).</summary>
public sealed class Audience
{
    readonly LiveTracker _live;

    public Audience(LiveTracker live) => _live = live;

    /// <summary>The viewer of this request: the admin's own view sees everything; the admin looking as a player (the top bar's View) is that player.</summary>
    public static Viewer Of(HttpContext context)
    {
        if (context.User.IsInRole(Roles.Admin) && PointOfView.Of(context) == null) return new Viewer(null, true);
        return new Viewer(PointOfView.Name(context) is { Length: > 0 } name ? name : null, false);
    }

    /// <summary>
    /// The players this player may see: himself, the in-game party he is in now (from everybody's log), and the members of his group in the Hauling Planner's
    /// session (the party he hauled with, also when one of them is not playing now). Names in any capitals.
    /// </summary>
    public HashSet<string> PartyOf(string name, DateTime nowUtc)
    {
        var book = _live.Parties(nowUtc);
        var hauled = _live.Read(t => t.State.Current != null && t.State.Data.Sessions.TryGetValue(t.State.Current.Key, out var s)
            ? s.Groups.Select(g => g.ToList()).ToList() : new List<List<string>>());
        return book.Together(name, hauled);   // the party tracker's party now, and the groups of the session
    }

    /// <summary>The players this viewer may see (null: everybody; empty: nobody, a visitor).</summary>
    public HashSet<string>? Allowed(Viewer viewer, DateTime nowUtc) =>
        viewer.Everything ? null : viewer.Name == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : PartyOf(viewer.Name, nowUtc);
}
