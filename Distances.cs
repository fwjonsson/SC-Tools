namespace HaulingTracker;

/// <summary>
/// Distances between the Pyro stations in gigameters (Gm). Positions come from a top-down map of Pyro that was checked
/// against the in-game star map; the distances from Ruin are the ones read in the game. Planets do not move in the game,
/// so the numbers stay valid. Ruin Station and Orbituary orbit a planet closely, so the planet centre is used for them.
/// </summary>
static class PyroDistances
{
    public const string Ruin = "Ruin";
    const double PxPerGm = 14.982;   // map pixels per Gm, fitted to the in-game readings (all within 0.2%)

    // id = the last 5 digits of the location id in the log (same ids as MainForm.KnownNames)
    static readonly (string Id, string Name, double X, double Y, double FromRuin)[] Stations =
    {
        (Ruin,    "Ruin Station",             1195.5,  355.5,   0),
        ("73791", "Rat's Nest",               1286.5, 1191.5,  56.00),
        ("70269", "Megumi Refueling",         2352.5,  152.5,  78.36),
        ("70120", "Patch City",               2138.5, 1229.5,  85.90),
        ("69936", "Gaslight",                 1859.5, 1782.5, 105.09),
        ("72785", "Dudley & Daughters",        993.5, 1513.5,  78.35),
        ("73041", "Orbituary",                1679.5,  964.5,  51.95),
        ("72645", "Rod's Fuel 'N Supplies",   2540.5, 1243.5, 107.64),
        ("70053", "Starlight Service Station", 1701.5,  978.5,  53.60),
        ("72952", "Checkmate",                2076.5, 1092.5,  76.73),
        ("74320", "Nyx Gateway",              1394.5, 1621.5,  85.52),
        ("74325", "Stanton Gateway",          2431.5,  470.5,  82.67),
        ("70273", "Endgame",                  2703.5, 1735.5, 136.50),
    };

    /// <summary>The station for the last 5 digits of a location id, or null when it is not in the table.</summary>
    public static string? IdOfTail(string tail) =>
        Stations.Any(s => s.Id == tail && s.Id != Ruin) ? tail : null;

    /// <summary>The station for a name you typed, ignoring case and the "Station" word, or null.</summary>
    public static string? IdOfName(string name)
    {
        var n = Normalize(name);
        if (n.Length == 0) return null;
        foreach (var s in Stations)
            if (s.Id != Ruin && Normalize(s.Name) == n) return s.Id;
        return null;
    }

    static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Replace("service station", "").Replace("station", "").Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Straight-line distance in Gm between two stations. Legs from Ruin use the distances read in the game.</summary>
    public static double Between(string a, string b)
    {
        if (a == b) return 0;
        var sa = Stations.First(s => s.Id == a);
        var sb = Stations.First(s => s.Id == b);
        if (a == Ruin) return sb.FromRuin;
        if (b == Ruin) return sa.FromRuin;
        return Math.Sqrt((sa.X - sb.X) * (sa.X - sb.X) + (sa.Y - sb.Y) * (sa.Y - sb.Y)) / PxPerGm;
    }

    /// <summary>The distance of a run: Ruin, every pickup station in the order of the pickups, and back to Ruin.</summary>
    public static double Run(IEnumerable<string> stops)
    {
        var last = Ruin;
        double total = 0;
        foreach (var id in stops)
        {
            if (id == last) continue;   // two pickups at the same station
            total += Between(last, id);
            last = id;
        }
        return total + Between(last, Ruin);
    }
}
