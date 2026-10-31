namespace SCTools.Tests.Support;

/// <summary>Where the location tracker has a player, put in by hand for the tests of the Hauling Planner alone (the server works it out from the logs).</summary>
static class Tracked
{
    /// <summary>
    /// Puts this worker where the location tracker would have him (as the planner's ids: the place he is at since when, the one he is flying to since when),
    /// in the tracker's book (made when there is none), in place of what it had on him.
    /// </summary>
    public static void Place(Tracker t, string name, PlannerPlace place)
    {
        var book = t.State.Locations ??= new LocationBook { Now = DateTime.UtcNow };
        book.Players.RemoveAll(p => PlayerNames.SameName(p.Name, name));
        var at = StarRoutes.PlaceOf(place.At);
        var to = StarRoutes.PlaceOf(place.Heading);
        var picked = to != null ? new QtEvent { Time = place.HeadingSince ?? DateTime.MinValue, Kind = QtKind.Selected } : null;
        book.Players.Add(new LocatedPlayer(name, new HaulingLog(), new PlayerLocation(at?.System ?? to?.System, at, at != null ? place.Since : null, picked, null, to)));
    }
}
