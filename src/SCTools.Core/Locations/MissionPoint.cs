namespace SCTools.Locations;

/// <summary>
/// An objective of a mission a player can quantum jump to: its beacon (MissionBeacon: the destination's id, and its marker's entity id), the mission's title,
/// what the objective says, the contract's system (or null), the place its text names (or null), and from when until the objective or the mission was over
/// (null: going on).
/// </summary>
public sealed record MissionPoint(string NavPoint, string Marker, string Title, string Text, string? System, Place? Place, DateTime From, DateTime? Until)
{
    /// <summary>Is this destination of the log this objective (its beacon, or its marker)?</summary>
    public bool Is(string destination) =>
        destination.Equals(NavPoint, StringComparison.OrdinalIgnoreCase)
        || (Marker.Length > 0 && destination.Equals("MissionObjectiveMarker_" + Marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>Was the objective there to jump to at this time?</summary>
    public bool On(DateTime t) => From <= t && (Until == null || Until >= t - LocationTracker.DoneGrace);
}
