namespace SCTools.Locations;

/// <summary>
/// An objective of a mission a player can quantum jump to: its beacon (MissionBeacon: the destination's id, and its marker's entity id), the mission's title,
/// what the objective says, the contract's system (or null), the place its text names (or null), and from when until the objective or the mission was over
/// (null: going on). Reached: when the game took its marker away from him, the objective done for him (Mission.Reached; null: not yet). MissionId and
/// Objective: whose objective it is ("": not said).
/// </summary>
public sealed record MissionPoint(string NavPoint, string Marker, string Title, string Text, string? System, Place? Place, DateTime From, DateTime? Until,
    DateTime? Reached = null, string MissionId = "", string Objective = "")
{
    /// <summary>Is this destination of the log this objective (its beacon, or its marker)?</summary>
    public bool Is(string destination) =>
        destination.Equals(NavPoint, StringComparison.OrdinalIgnoreCase)
        || (Marker.Length > 0 && destination.Equals("MissionObjectiveMarker_" + Marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>Was the objective there to jump to at this time?</summary>
    public bool On(DateTime t) => From <= t && (Until == null || Until >= t - LocationTracker.DoneGrace);
}
