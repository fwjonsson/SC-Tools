namespace SCTools.GameData;

/// <summary>
/// One leg of a route between two places: a quantum jump (From and To in one system, Gm long), or a gateway crossing (Gate: From is the gateway on one side,
/// To the gateway on the other, no Gm; it takes StarRoutes.GateSeconds).
/// </summary>
public sealed record RouteLeg(string From, string To, double Gm, bool Gate);

/// <summary>
/// The way from any place of the map to any other, for the Route Planner (stage 2 of docs/planner-any-hauling.md: interstellar legs). A place is named by its id
/// as the planner has it: one of the Pyro stations' ids (PyroDistances: "Ruin", "69936" ...), or a place key of the map ("stanton/baijini-point", Place.Key).
/// Within one system a route is one jump, straight between the places; between two of the Pyro stations it is the distance table's leg, as the game reads it,
/// so the Pyro legs do not change by a metre. Between two systems it is the jump to the gateway of the start's system that leads to the other, the crossing,
/// and the jump from the arrival gateway to the end (every pair of systems has its pair of gateways: Pyro's Stanton Gateway and Stanton's Pyro Gateway, and so
/// on, so no way goes through a third system). The crossing takes GateSeconds, counted in the flight time; it has no Gm.
/// </summary>
public static class StarRoutes
{
    /// <summary>
    /// What a gateway crossing takes, in seconds: 3.4 minutes, the middle of the 80 crossings in zzoega's logs from one side's area tag to the other's (most
    /// between 3.2 and 4.1), the calibration included (Frank, 30 Oct 2026).
    /// </summary>
    public const double GateSeconds = 204;

    /// <summary>The places of the map to read the ids by: the game data's, until the server gives the admin's (MapPlaces, with the places he added or renamed).</summary>
    public static Func<Atlas> AtlasSource = () => Atlas.Default;

    /// <summary>The place a planner id is: a Pyro station by its id, else a place of the map by its key; null when neither.</summary>
    public static Place? PlaceOf(string? id)
    {
        if (id == null) return null;
        var atlas = CurrentAtlas();
        return atlas.OfPyroStation(id) ?? atlas.Find(id);
    }

    /// <summary>The places to read by: the source's, else the game data's (a source that is gone, as a test server's after the test, answers nothing).</summary>
    public static Atlas CurrentAtlas()
    {
        try { return AtlasSource(); }
        catch (Exception) { return Atlas.Default; }
    }

    /// <summary>True when the id names a place the routes know (a Pyro station, or a place of the map).</summary>
    public static bool Known(string? id) => PlaceOf(id) != null;

    /// <summary>The planner's id of a place: the Pyro station's id when it is one of the planner's Pyro stations, else its key.</summary>
    public static string IdOf(Place place) => StarMap.PyroStationIdOf(place) ?? place.Key;

    /// <summary>The place key of a planner id ("pyro/ruin-station" for "Ruin"), or the id itself when it is no place the map knows.</summary>
    public static string KeyOf(string id) => PlaceOf(id)?.Key ?? id;

    /// <summary>The system of a planner id ("pyro", "stanton"), or null when it is no place the map knows.</summary>
    public static string? SystemOf(string id) => PlaceOf(id)?.System;

    /// <summary>
    /// The name of a place for the tables: a Pyro station's as the planner always wrote it (PyroDistances: "Rod's Fuel 'N Supplies"), else the map's name
    /// ("Baijini Point"), else the id as it is.
    /// </summary>
    public static string NameOf(string id)
    {
        if (PyroDistances.Points.Any(p => p.Id == id)) return PyroDistances.NameOf(id);
        return PlaceOf(id)?.Name ?? id;
    }

    /// <summary>True when the way from one place to the other crosses a gateway (they are in different systems).</summary>
    public static bool Crosses(string a, string b)
    {
        var sa = SystemOf(a);
        var sb = SystemOf(b);
        return sa != null && sb != null && sa != sb;
    }

    /// <summary>The gateways between two systems: the one in the first system that leads to the second, and the one in the second that leads back; null when there are none.</summary>
    public static (string From, string To)? GatesBetween(string systemA, string systemB)
    {
        var atlas = CurrentAtlas();
        var a = atlas.Find(systemA + "/" + systemB + "-gateway");
        var b = atlas.Find(systemB + "/" + systemA + "-gateway");
        return a != null && b != null ? (IdOf(a), IdOf(b)) : null;
    }

    /// <summary>
    /// The legs from one place to another: nothing when they are the same place, one jump within a system, or the jump to the gateway, the crossing and the jump
    /// from the other gateway between two systems. A place the map does not know, or two systems without a gateway between them: one leg of 0 Gm (the planner
    /// leaves such places out before it plans; this keeps a table from failing on one).
    /// </summary>
    public static IReadOnlyList<RouteLeg> Legs(string a, string b)
    {
        if (a == b) return Array.Empty<RouteLeg>();
        var pa = PlaceOf(a);
        var pb = PlaceOf(b);
        if (pa == null || pb == null) return new[] { new RouteLeg(a, b, 0, false) };
        if (pa.System == pb.System) return new[] { new RouteLeg(a, b, StarMap.JumpGm(pa, pb), false) };
        var gates = GatesBetween(pa.System, pb.System);
        if (gates == null) return new[] { new RouteLeg(a, b, 0, false) };
        var (outGate, inGate) = gates.Value;
        var legs = new List<RouteLeg>();
        if (a != outGate) legs.Add(new RouteLeg(a, outGate, StarMap.JumpGm(pa, PlaceOf(outGate)!), false));
        legs.Add(new RouteLeg(outGate, inGate, 0, true));
        if (inGate != b) legs.Add(new RouteLeg(inGate, b, StarMap.JumpGm(PlaceOf(inGate)!, pb), false));
        return legs;
    }

    /// <summary>The Gm flown from one place to the other (the crossings count nothing).</summary>
    public static double Gm(string a, string b) => Legs(a, b).Sum(l => l.Gm);

    /// <summary>The flight time from one place to the other with this drive: every jump's time (QtDrive.Seconds) and GateSeconds for every crossing.</summary>
    public static double Seconds(QtDrive drive, string a, string b) => Legs(a, b).Sum(l => l.Gate ? GateSeconds : drive.Seconds(l.Gm));

    /// <summary>The Gm of a route through these places in order (the same place twice in a row is no leg).</summary>
    public static double PathGm(IEnumerable<string> places)
    {
        string? last = null;
        double total = 0;
        foreach (var id in places)
        {
            if (last != null) total += Gm(last, id);
            last = id;
        }
        return total;
    }

    /// <summary>The flight time of a route through these places in order with this drive, the crossings included.</summary>
    public static double PathSeconds(QtDrive drive, IEnumerable<string> places)
    {
        string? last = null;
        double total = 0;
        foreach (var id in places)
        {
            if (last != null) total += Seconds(drive, last, id);
            last = id;
        }
        return total;
    }
}
