namespace SCTools.Locations;

/// <summary>
/// Where a player is now, with his drive (LocationTracker.Now): the state, his system, and what goes with it. At: the place, its name (or what the mission's
/// objective says), the mission, since when, landed or taking off (Pad), the destination he picked while still at the station (Leaving), and whether the
/// Hauling Planner put him there. Jump: from where to where, when it started (the map's moving marker counts from it), how far (Gm) and how long his drive takes (seconds).
/// Heading: where to (a place, or a mission's objective) and since when he picked it. Mission: what the objective says, the mission, since when. Unknown:
/// off the grid (Off) since when, with the last place known (LastPing).
/// </summary>
public sealed record PlayerNow(PlayerState State, string? System)
{
    public Place? At { get; init; }
    public string? AtName { get; init; }
    public string? Mission { get; init; }
    public DateTime? Since { get; init; }
    public string? Leaving { get; init; }
    public bool ByPlanner { get; init; }
    public PadState? Pad { get; init; }
    public Place? From { get; init; }
    public Place? To { get; init; }
    public string? ToName { get; init; }
    public DateTime Start { get; init; }
    public double Gm { get; init; }
    public double Seconds { get; init; }
    public bool Off { get; init; }
    public string? LastPing { get; init; }
}
