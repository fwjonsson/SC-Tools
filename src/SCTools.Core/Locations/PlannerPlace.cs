namespace SCTools.Locations;

/// <summary>
/// Where the location tracker has a worker, as the Hauling Planner's ids (a Pyro station's id, else the place's key: StarRoutes): the place he is at and
/// since when, and the one he is flying to and since when he picked it; null where it has him nowhere. The planner takes it where it is newer than its own
/// pickups and deliveries (Whereabouts): the tracker knows what the planner's own reading does not, a crew member's jumps (the arrival of the same ship in the
/// pilot's log), the destinations and areas the admin named on Map places, and the mission objectives.
/// </summary>
public sealed record PlannerPlace(string? At, DateTime? Since, string? Heading, DateTime? HeadingSince);
