namespace HaulingTracker;

/// <summary>
/// Distances between the Pyro stations in gigameters (Gm). They are calculated from the real positions of the stations
/// (Star Citizen Wiki API); the distances from Ruin are the ones read in the game. The top-down map positions are only for drawing the map. Planets do not move in the game,
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

    // Real positions in the Pyro system in Gm (x, y, z), from the Star Citizen Wiki API (locations/positions). The distances from Ruin
    // calculated from them match the ones read in the game within 0.1 Gm, so every other leg is calculated from them too.
    static readonly Dictionary<string, (double X, double Y, double Z)> Real = new()
    {
        ["Ruin"] = (-52.3368, -43.9179, -0.0001),
        ["73791"] = (3.7326, -42.7669, 0.0185),
        ["70269"] = (-59.1456, 34.1455, 0.0127),
        ["70120"] = (11.4453, 13.6223, 0.0028),
        ["69936"] = (46.4944, -8.1909, 0.0177),
        ["72785"] = (23.3426, -64.2260, -0.0108),
        ["73041"] = (-8.9004, -15.4178, 0.0002),
        ["72645"] = (14.6643, 40.3386, -0.0095),
        ["70053"] = (-7.7566, -14.0089, 0.0014),
        ["72952"] = (1.8334, 10.4345, 0.0026),
        ["74320"] = (32.9940, -38.1517, 0.0000),
        ["74325"] = (-37.6092, 37.4357, 0.0000),
        ["70273"] = (48.2968, 48.2920, 0.0027),
    };

    public static string NameOf(string id) => Stations.First(s => s.Id == id).Name;

    /// <summary>Every station with its position on the top-down map (pixels of the 3840x2160 map), Ruin included.</summary>
    public static IEnumerable<(string Id, string Name, double X, double Y)> Points => Stations.Select(s => (s.Id, s.Name, s.X, s.Y));

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
        if (Real.TryGetValue(a, out var ra) && Real.TryGetValue(b, out var rb))
            return Math.Sqrt((ra.X - rb.X) * (ra.X - rb.X) + (ra.Y - rb.Y) * (ra.Y - rb.Y) + (ra.Z - rb.Z) * (ra.Z - rb.Z));
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
