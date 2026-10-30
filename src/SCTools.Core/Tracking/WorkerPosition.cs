namespace SCTools.Tracking;

/// <summary>
/// Where the map has a worker, for the Hauling Planner (TrackerState.Positions, put there by the server every few seconds: PlannerPositions on the server,
/// from MapService): the station of the distance table he is at and since when, and the one he is flying to and since when he picked it. Null where the map
/// has him somewhere that is not one of the planner's stations, or nowhere. The map knows what the planner's own reading of his log does not: a crew
/// member's jumps (the arrival of the same ship in the pilot's log), the destinations and areas the admin named on Map places, and the mission objectives
/// (Frank's list of 29 Oct 2026: the map and the planner learn from each other; the other way, the planner's pickups and deliveries, the map takes itself).
/// </summary>
public sealed record WorkerPosition(string? AtStation, DateTime? Since, string? HeadingStation, DateTime? HeadingSince);
