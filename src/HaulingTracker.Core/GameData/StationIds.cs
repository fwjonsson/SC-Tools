namespace HaulingTracker.GameData;

/// <summary>
/// Distances between the Pyro stations in gigameters (Gm). They are calculated from the real positions of the stations
/// (Star Citizen Wiki API); the distances from Ruin are the ones read in the game. The top-down map positions are only for drawing the map. Planets do not move in the game,
/// so the numbers stay valid. Ruin Station and Orbituary orbit a planet closely, so the planet centre is used for them.
/// </summary>
/// <summary>
/// Recognises a station by where it is. The location id in the log (zoneHostId) is different on every server shard, but the position
/// that comes with the objective marker is the same everywhere, so the stations are found by their position.
/// </summary>
public static class StationIds
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
