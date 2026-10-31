namespace SCTools.Locations;

/// <summary>
/// What the Hauling Planner's own reading of the logs says about where a member of its party is (Whereabouts.OwnLocationOf): the station of his last
/// pickup, delivery or hand-in, and when that was (LastMoveOf; the minimum time when nothing yet).
/// </summary>
public sealed record PlannerClue(string Station, DateTime At);
