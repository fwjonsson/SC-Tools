using System.Globalization;

namespace SCTools.Tracking;

/// <summary>The places in the log: the stations the program knows (found by their position in the log), the names you typed, and their station ids.</summary>
public sealed class Places
{
    readonly TrackerState _s;

    public Places(TrackerState state) => _s = state;

    /// <summary>The last 5 digits of a location id.</summary>
    public static string Tail(string zone) => zone.Length > 5 ? zone.Substring(zone.Length - 5) : zone;

    /// <summary>The id of the station in the tables: found by the position the log gave (the same on every server), else the last digits of the location id.</summary>
    public static string StationKey(string zone) => StationIds.Of(zone) ?? Tail(zone);

    // the stations' names as the game gives them (Frank, 30 Oct 2026: the full names, not the shortened ones of before and not names of his own), keyed by
    // the last digits of the location id
    static readonly Dictionary<string, string> KnownNames = new()
    {
        ["73791"] = "Rat's Nest",
        ["70269"] = "Megumi Refueling",
        ["70120"] = "Patch City",
        ["69936"] = "Gaslight",
        ["72785"] = "Dudley & Daughters",
        ["73041"] = "Orbituary",
        ["72645"] = "Rod's Fuel 'N Supplies",
        ["70053"] = "Starlight Service Station",
        ["72952"] = "Checkmate",
        ["74320"] = "Nyx Gateway",
        ["74325"] = "Stanton Gateway",
        ["70273"] = "Endgame",
    };

    /// <summary>The name the program knows for a location (from its position), or Unknown ...id.</summary>
    public static string AutoName(string zone) =>
        KnownNames.TryGetValue(StationKey(zone), out var known) ? known : "Unknown ..." + Tail(zone);

    /// <summary>
    /// The name of a location: the one the game gives it (AutoName). The names the admin typed (AppData.LocationNames, the shortened ones of before) are
    /// not used since 30 Oct 2026 (Frank: the full names back, and the Stops table of his Session page went); a zone of a hauling job of another system is
    /// named as the map has its place (TrackerState.JobZoneNames, stage 4); a station the site does not know is "Unknown ...id".
    /// </summary>
    public string LocationName(string zone) =>
        KnownNames.ContainsKey(StationKey(zone)) ? AutoName(zone) : _s.JobZoneNames.TryGetValue(zone, out var job) ? job : AutoName(zone);

    /// <summary>The station of a pickup location: by its location id, or by the name you gave it. Null when the distance table does not know it.</summary>
    public string? StationOf(string zone) => PyroDistances.IdOfTail(StationKey(zone)) ?? PyroDistances.IdOfName(LocationName(zone));

    /// <summary>Gm from Ruin Station to this pickup location, empty when the distance table does not know it.</summary>
    public string DistanceText(string zone)
    {
        var id = StationOf(zone);
        return id == null ? "" : PyroDistances.Between(PyroDistances.Ruin, id).ToString("N1", CultureInfo.InvariantCulture) + " Gm";
    }
}
