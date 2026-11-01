namespace SCTools.Server;

/// <summary>
/// The missions' sites the players show the map with /showlocation (Frank, 1 Nov 2026). The game data has no place for most of them (an asteroid mining base in
/// the Keeger Belt), so a mission's beacon there could only be placed by its fuel estimate when one of the game data's hidden spots happened to be at that
/// distance. A player at a mission's site (the location tracker has him at its objective, with no jump since) who uses /showlocation there saves the site:
/// a hidden spot by what the objective says ("Go to Asteroid Mining Base": "Asteroid Mining Base"), at his coordinates (MapPlaces.AddSpot). From then on
/// a fix within 50 km of it is there, and a beacon at its distance from a known start is placed there (BeaconRanging), whoever flies it.
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
    /// Takes in a player's fix (after LogUploads.AddFix): when the location tracker has him at a mission's objective there, the site is saved (or the spot
    /// within 50 km of it is the site). The site's key, or null when he is not at a mission.
    /// </summary>
    public string? Learn(string player, PositionFix fix, DateTime nowUtc)
    {
        var where = _live.Locations(nowUtc).Where(player);
        if (where is not { AtMission: { } mission, System: { } system } || where.Fix != fix) return null;
        var (x, y) = fix.Gm;
        return _places.AddSpot(system, SiteName(mission.Text), x, y, nowUtc).Key;
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
