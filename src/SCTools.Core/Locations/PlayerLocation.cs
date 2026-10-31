namespace SCTools.Locations;

/// <summary>
/// Where the location tracker has a player (LocationTracker.Follow): his system, the place he is at (or null), since when; and the jump he is on, if any.
/// At or to a mission's objective (AtMission, ToMission; its place, when its text names one). Jumped: the last jump took him somewhere the map does not
/// know ("off the grid"); LastPing: the last place it knew him at before that, if any. Moves: the areas he went into since he picked the jump he is on
/// (JumpTimes; taking off into the station's own area is not one of them); PickedInArea: was he inside an area when he picked it, not out in open space
/// (a station's hangar or area, a mission's site, the area around a planet; null: the log does not say, a tracker from before 31 Oct 2026); Space: the
/// open spaces it was followed with (OpenSpace; the known ones when null). Hangar: landed or taking off now (null: neither).
/// ByPlanner: the Hauling Planner's pickups, deliveries or hand-ins put him at that station, being newer than anything his jumps say.
/// Ranged: the missions' beacons he set a route to that were placed by the route's fuel estimate (BeaconRanging), by the beacon's id: the Map tool puts
/// their objectives there.
/// </summary>
public sealed record PlayerLocation(string? System, Place? At, DateTime? Since, QtEvent? Picked, Place? From, Place? To,
    MissionPoint? AtMission = null, MissionPoint? ToMission = null, bool Jumped = false, Place? LastPing = null,
    IReadOnlyList<AreaChange>? Moves = null, bool? PickedInArea = null, bool ByPlanner = false, HangarState? Hangar = null,
    IReadOnlyDictionary<string, Place>? Ranged = null, IReadOnlySet<long>? Space = null)
{
    /// <summary>
    /// Where he is for the Hauling Planner, as its ids (a Pyro station's id, else the place's key: StarRoutes): the place he is at and since when, and the
    /// one he is flying to and since when he picked it (null where the tracker has him at no place, or on his way nowhere it knows).
    /// </summary>
    public PlannerPlace ForPlanner => new(At != null ? StarRoutes.IdOf(At) : null, At != null ? Since : null,
        To != null ? StarRoutes.IdOf(To) : null, To != null ? Picked?.Time : null);
}
