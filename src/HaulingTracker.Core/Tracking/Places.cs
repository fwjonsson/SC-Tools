using System.Globalization;

namespace HaulingTracker.Tracking;

/// <summary>The places in the log: the stations the program knows (found by their position in the log), the names you typed, and their station ids.</summary>
public sealed class Places
{
    readonly TrackerState _s;

    public Places(TrackerState state) => _s = state;

    /// <summary>The last 5 digits of a location id.</summary>
    public static string Tail(string zone) => zone.Length > 5 ? zone.Substring(zone.Length - 5) : zone;

    /// <summary>The id of the station in the tables: found by the position the log gave (the same on every server), else the last digits of the location id.</summary>
    public static string StationKey(string zone) => StationIds.Of(zone) ?? Tail(zone);

    // Names found so far, keyed by the last digits of the location id. Your own names in the grid win.
    static readonly Dictionary<string, string> KnownNames = new()
    {
        ["73791"] = "Rat's Nest",
        ["70269"] = "Megumi",
        ["70120"] = "Patch City",
        ["69936"] = "Gaslight",
        ["72785"] = "Dudley",
        ["73041"] = "Orbituary",
        ["72645"] = "Rod's Fuel",
        ["70053"] = "Starlight",
        ["72952"] = "Checkmate",
        ["74320"] = "Nyx GW",
        ["74325"] = "Stanton GW",
        ["70273"] = "Endgame",
    };

    // the names the program used to give these stations, as they may be saved in the data file
    static readonly Dictionary<string, string> LongToShort = new()
    {
        ["Megumi Refueling"] = "Megumi",
        ["Dudley & Daughters"] = "Dudley",
        ["Rod's Fuel 'N Supplies"] = "Rod's Fuel",
        ["Starlight Service Station"] = "Starlight",
        ["Nyx Gateway"] = "Nyx GW",
        ["Stanton Gateway"] = "Stanton GW",
    };

    /// <summary>The name the program knows for a location (from its position), or Unknown ...id.</summary>
    public static string AutoName(string zone) =>
        KnownNames.TryGetValue(StationKey(zone), out var known) ? known : "Unknown ..." + Tail(zone);

    /// <summary>The name of a location: the one you gave it, else the one the program knows.</summary>
    public string LocationName(string zone)
    {
        var auto = AutoName(zone);
        if (_s.Data.LocationNames.TryGetValue(zone, out var n) && !string.IsNullOrWhiteSpace(n))
        {
            // a saved name that is only the name of another station, while this one is known now (it was saved by clicking into the cell), is not a name you chose
            if (!auto.StartsWith("Unknown ...") && n != auto && (KnownNames.ContainsValue(n) || LongToShort.ContainsKey(n))) return auto;
            if (LongToShort.TryGetValue(n, out var shortName)) return shortName;   // a long name saved by an earlier version: the short one now
            return n;
        }
        return auto;
    }

    /// <summary>
    /// Gives a location your own name. Nothing, an Unknown name or the name the program knows itself takes your name away again: then the result is true (the
    /// location has the name the program knows).
    /// </summary>
    public bool Rename(string zone, string text)
    {
        if (text.Length == 0 || text.StartsWith("Unknown ...") || text == AutoName(zone))
        {
            _s.Data.LocationNames.Remove(zone);
            return true;
        }
        _s.Data.LocationNames[zone] = text;
        return false;
    }

    /// <summary>The station of a pickup location: by its location id, or by the name you gave it. Null when the distance table does not know it.</summary>
    public string? StationOf(string zone) => PyroDistances.IdOfTail(StationKey(zone)) ?? PyroDistances.IdOfName(LocationName(zone));

    /// <summary>Gm from Ruin Station to this pickup location, empty when the distance table does not know it.</summary>
    public string DistanceText(string zone)
    {
        var id = StationOf(zone);
        return id == null ? "" : PyroDistances.Between(PyroDistances.Ruin, id).ToString("N1", CultureInfo.InvariantCulture) + " Gm";
    }
}
