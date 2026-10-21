namespace HaulingTracker;

/// <summary>
/// Distances between the Pyro stations in gigameters (Gm). They are calculated from the real positions of the stations
/// (Star Citizen Wiki API); the distances from Ruin are the ones read in the game. The top-down map positions are only for drawing the map. Planets do not move in the game,
/// so the numbers stay valid. Ruin Station and Orbituary orbit a planet closely, so the planet centre is used for them.
/// </summary>
/// <summary>
/// Recognises a station by where it is. The location id in the log (zoneHostId) is different on every server shard, but the position
/// that comes with the objective marker is the same everywhere, so the stations are found by their position.
/// </summary>
static class StationIds
{
    // id = the last 5 digits of the location id (same ids as PyroDistances), x, y, z as written in the log
    static readonly (string Id, double X, double Y, double Z)[] Known =
    {
        ("73791", -9820981.55, 7015084.13, 18513515.24),    // Rat's Nest
        ("70269", 23515490, -15603579, 12705743),           // Megumi Refueling
        ("70120", 2281205, -14897444, 2752554),             // Patch City
        ("69936", -19125581, 10432691, 17665446),           // Gaslight
        ("72785", -25440591, -23932605, -10765064),         // Dudley & Daughters
        ("73041", 379850, -718724, 226044),                 // Orbituary
        ("72645", -21100600, -9275477, -9518769),           // Rod's Fuel 'N Supplies
        ("70053", 10905194, 4250545, 1357701),              // Starlight Service Station
        ("72952", -11023679, -25541091, 2570931),           // Checkmate
        ("70273", -14499257.01, -19323565.06, 2735552.17),  // Endgame
    };

    // The two gateways (Nyx and Stanton) have exactly the same position in the log, so the position cannot tell them apart.
    // On every server the ids of the stations come in the same order, and the Nyx Gateway has the lower id of the two.
    const double GateX = -19162.107349, GateY = -37842.049299, GateZ = 6727.802807;
    const string NyxId = "74320", StantonId = "74325";
    static readonly List<string> Gates = new();

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ByZone = new();

    /// <summary>Remembers which station a location id is, from the position in the log. A position that is not in the table is ignored.</summary>
    public static void Register(string zone, double x, double y, double z)
    {
        var gx = GateX - x;
        var gy = GateY - y;
        var gz = GateZ - z;
        if (gx * gx + gy * gy + gz * gz <= 5000.0 * 5000.0)
        {
            lock (Gates)
            {
                if (!Gates.Contains(zone)) Gates.Add(zone);
            }
            return;
        }
        foreach (var k in Known)
        {
            var dx = k.X - x;
            var dy = k.Y - y;
            var dz = k.Z - z;
            if (dx * dx + dy * dy + dz * dz > 5000.0 * 5000.0) continue;
            ByZone[zone] = k.Id;
            return;
        }
    }

    /// <summary>True for a location id that sits at the position of the gateways (it is one of the two, which one is not known from the position).</summary>
    public static bool IsGate(string zone)
    {
        lock (Gates) return Gates.Contains(zone);
    }

    /// <summary>The game itself named this gateway (a quantum route that started right after something was picked up there): it is the Nyx or the Stanton Gateway.</summary>
    public static void NameGate(string zone, bool nyx) => ByZone[zone] = nyx ? NyxId : StantonId;

    /// <summary>The station id (last 5 digits as in the table) for a location id the position of which has been seen, or null.</summary>
    public static string? Of(string zone)
    {
        if (ByZone.TryGetValue(zone, out var id)) return id;
        lock (Gates)
        {
            if (!Gates.Contains(zone) || !long.TryParse(zone, out var me)) return null;
            // the other gateway of the same server was named by the game: this one is the other of the two
            foreach (var g in Gates)
            {
                if (g != zone && long.TryParse(g, out var gn) && Math.Abs(gn - me) < 1000 && ByZone.TryGetValue(g, out var named))
                    return named == NyxId ? StantonId : named == StantonId ? NyxId : null;
            }
            // the gateways of the same server (ids close together), lowest first: Nyx Gateway, then Stanton Gateway.
            // With only one of them seen so far it cannot be told which it is, so it stays unknown (the last digits may still know it)
            var group = Gates.Where(g => long.TryParse(g, out var n) && Math.Abs(n - me) < 1000).Select(g => long.Parse(g)).OrderBy(n => n).ToList();
            var rank = group.IndexOf(me);
            if (group.Count < 2) return null;
            return rank == 0 ? NyxId : rank == 1 ? StantonId : null;
        }
    }
}

static class PyroDistances
{
    public const string Ruin = "Ruin";
    const double PxPerGm = 14.982;   // map pixels per Gm, fitted to the in-game readings (all within 0.2%)

    // id = the last 5 digits of the location id in the log (same ids as MainForm.KnownNames)
    static readonly (string Id, string Name, double X, double Y, double FromRuin)[] Stations =
    {
        (Ruin,    "Ruin Station",             1195.5,  355.5,   0),
        ("73791", "Rat's Nest",               1286.5, 1191.5,  56.00),
        ("70269", "Megumi",         2352.5,  152.5,  78.36),
        ("70120", "Patch City",               2138.5, 1229.5,  85.90),
        ("69936", "Gaslight",                 1859.5, 1782.5, 105.09),
        ("72785", "Dudley",        993.5, 1513.5,  78.35),
        ("73041", "Orbituary",                1679.5,  964.5,  51.95),
        ("72645", "Rod's Fuel",   2540.5, 1243.5, 107.64),
        ("70053", "Starlight", 1701.5,  978.5,  53.60),
        ("72952", "Checkmate",                2076.5, 1092.5,  76.73),
        ("74320", "Nyx GW",              1394.5, 1621.5,  85.52),
        ("74325", "Stanton GW",          2431.5,  470.5,  82.67),
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

    // The backdrop of the map: the sun, the orbits and the planets, in the pixels of the same top-down map (measured on it).
    public static readonly (double X, double Y) Sun = (1919.5, 1079.5);
    public const double SunRadius = 25;
    public static readonly double[] OrbitRadii = { 123.4, 158.7, 266.0, 642.4, 1026.5 };   // Pyro I, Monox, Bloom, Pyro V, Terminus
    public static readonly (double X, double Y)[] Planets =
    {
        (1801.5, 1115.5),   // Pyro I
        (2010.5, 1209.5),   // Monox
        (1679.5, 964.5),    // Bloom
        (1864.5, 1719.5),   // Pyro V
        (1194.0, 355.0),    // Terminus
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

    // The destinations of the quantum map as the logs write them ("rs_ext_pyro6_leo") and the station each one is. Seen in the logs by flying there: Ruin, Orbituary, Starlight, Dudley,
    // Patch City, Checkmate, Rod's Fuel, Rat's Nest and both gateways. Megumi (the other Lagrange point of Terminus), Gaslight and Endgame are named by the same pattern
    // (planet, Lagrange point) from their positions on the map; if one is not right the destination just shows up as unknown.
    static readonly Dictionary<string, string> QuantumTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rs_ext_pyro6_leo"] = Ruin,
        ["rs_ext_pyro3_leo"] = "73041",        // Orbituary
        ["rs_ext_pyro3_l1"] = "70053",         // Starlight
        ["rs_ext_pyro6_l4"] = "72785",         // Dudley
        ["rs_ext_pyro3_l3"] = "70120",         // Patch City
        ["rs_ext_pyro2_l4"] = "72952",         // Checkmate
        ["rs_ext_pyro5_l4"] = "72645",         // Rod's Fuel
        ["rs_ext_pyro5_l5"] = "73791",         // Rat's Nest
        ["rs_ext_pyro6_l5"] = "70269",         // Megumi
        ["rs_ext_pyro5_l2"] = "69936",         // Gaslight
        ["rs_ext_pyro6_l3"] = "70273",         // Endgame
        ["rs_ext_pyro-nyx_jp"] = "74320",      // Nyx Gateway
        ["rs_ext_pyro-stan_jp1"] = "74325",    // Stanton Gateway
    };

    /// <summary>The station (Ruin included) for the destination of a quantum travel in the log, or null when it is not known.</summary>
    public static string? IdOfQuantumTarget(string target) => QuantumTargets.TryGetValue(target, out var id) ? id : null;

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
