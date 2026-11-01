namespace SCTools.Server;

/// <summary>
/// The missions' sites the players show the map with /showlocation (Frank, 1 Nov 2026). The game data has no place for most of them (an asteroid mining base in
/// the Keeger Belt), so a mission's beacon there could only be placed by its fuel estimate when one of the game data's hidden spots happened to be at that
/// distance. A player at a mission's site (the location tracker has him at its objective, with no jump since) who uses /showlocation there saves the site:
/// a hidden spot by what the objective says ("Go to Asteroid Mining Base": "Asteroid Mining Base"), at his coordinates (MapPlaces.AddSpot). From then on
/// a fix within 50 km of it is there, and a beacon at its distance from a known start is placed there (BeaconRanging), whoever flies it.
/// At the site is what the game says (Frank's rules, 1 Nov 2026), not a time: the mission is still going (not completed, failed or abandoned), its "Go
/// to" objective is done (he got there), no "Return to" objective of it is open (the game gives one when he leaves the mission's area), and he made no
/// jump since. Anything else saves nothing: no random places on the server.
/// </summary>
public sealed class MissionSpots
{
    readonly LiveTracker _live;
    readonly MapPlaces _places;

    public MissionSpots(LiveTracker live, MapPlaces places)
    {
        _live = live;
        _places = places;
    }

    /// <summary>
    /// Takes in a player's fix (after LogUploads.AddFix): when he is at a mission's site by the rules above, the site is saved (or the spot within 50 km of
    /// it is the site). The site's key, or null when he is not at one.
    /// </summary>
    public string? Learn(string player, PositionFix fix, DateTime nowUtc)
    {
        var located = _live.Locations(nowUtc).Of(player);
        if (located?.Where is not { AtMission: { } point, System: { } system } where || where.Fix != fix) return null;   // at a mission's objective, no jump since
        lock (_live.Uploads.Sync)
        {
            if (!located.Log.Missions.TryGetValue(point.MissionId, out var m) || !AtSite(m, point, fix.Time)) return null;
        }
        var (x, y) = fix.Gm;
        return _places.AddSpot(system, SiteName(point.Text), x, y, nowUtc).Key;
    }

    /// <summary>
    /// Is he at the mission's site at this time, by what the game says: the mission still going, its "Go to" objective (the point's) done, and no "Return
    /// to" objective of it open (given and not ended by then; one given again after it ended counts as open).
    /// </summary>
    public static bool AtSite(Mission m, MissionPoint point, DateTime t)
    {
        if (m.Dropped || (m.EndState != null && (m.EndedAt == null || m.EndedAt <= t))) return false;   // completed, failed or abandoned: no telling where he is
        var reached = point.Reached is { } r && r <= t
            || m.ObjEnded.TryGetValue(point.Objective, out var ended) && ended <= t
            || m.Events.TryGetValue(point.Objective, out var events) && events.Any(e => e.State == "COMPLETED" && e.Time <= t);
        if (!reached) return false;   // the "Go to" is not done: he did not get there
        foreach (var (o, text) in m.ObjText)
        {
            if (!text.StartsWith("Return to", StringComparison.OrdinalIgnoreCase) || !m.ObjGiven.TryGetValue(o, out var given) || given > t) continue;
            if (!(m.ObjEnded.TryGetValue(o, out var back) && back >= given && back <= t)) return false;   // he is outside the mission's area
        }
        return true;
    }

    /// <summary>A site's name from what its objective says: "Go to Asteroid Mining Base" is "Asteroid Mining Base".</summary>
    public static string SiteName(string objective)
    {
        var text = objective.Trim();
        foreach (var go in new[] { "Go to the ", "Go to ", "Return to the ", "Return to ", "Travel to " })
            if (text.StartsWith(go, StringComparison.OrdinalIgnoreCase) && text.Length > go.Length) return text[go.Length..].Trim();
        return text;
    }
}
