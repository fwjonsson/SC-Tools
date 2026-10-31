namespace SCTools.Locations;

/// <summary>
/// What the location tracker knows (LocationTracker.Work): where everybody playing is, since when, and whether he is on his way somewhere. Every tool asks
/// this one answer, worked out once per refresh (LiveTracker.Locations): the Map tool, the Hauling Planner's map and its Current location, the Companion, My
/// page and the Party pages.
/// </summary>
public sealed class LocationBook
{
    /// <summary>The time it was worked out for.</summary>
    public DateTime Now { get; init; }

    /// <summary>Everybody playing (a game running, by the trackers' logs), in the order of the logs.</summary>
    public List<LocatedPlayer> Players { get; } = new();

    /// <summary>The deaths of the last while (LocationTracker.DeathAge) of everybody playing, with where each happened: the map's skulls (#410).</summary>
    public List<PlayerDeath> Deaths { get; } = new();

    /// <summary>This player, when he is playing; else null.</summary>
    public LocatedPlayer? Of(string name) => Players.FirstOrDefault(p => SameName(p.Name, name));

    /// <summary>Where this player is, when he is playing; else null.</summary>
    public PlayerLocation? Where(string name) => Of(name)?.Where;

    /// <summary>Where this worker is for the Hauling Planner (PlayerLocation.ForPlanner), when he is playing; else null.</summary>
    public PlannerPlace? ForPlanner(string name) => Where(name)?.ForPlanner;

    /// <summary>
    /// What the Hauling Planner takes from the book, everybody's in one text: the same text is the same for the plan, so a new book that says nothing new to
    /// it does not make the plan anew (LiveTracker.Refresh).
    /// </summary>
    public string PlannerKey => string.Join("|", Players.Select(p => p.Name.ToLowerInvariant() + "=" + p.Where.ForPlanner));
}
