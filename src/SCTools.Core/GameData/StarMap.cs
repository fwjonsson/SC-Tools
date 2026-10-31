using System.Text.RegularExpressions;

namespace SCTools.GameData;

/// <summary>What a place of a star system is: how the map draws it.</summary>
public enum PlaceKind { Star, Planet, Moon, City, Gateway, Station }

/// <summary>
/// A place of a star system and where it is seen from above: X and Y in Gm, the system's own axes, with its star at 0, 0 (the same frame as the system's own
/// objective markers in the log, in metres there). Parent: the planet or moon it is at (a station in orbit, a city), or null. Short: the label on the map,
/// the full name for every place of the game data (Frank, 30 Oct 2026: "Dudley & Daughters" and "Rod's Fuel 'N Supplies" written out on the maps, as the
/// Hauling Planner has them again since the day before; before, the stations had short labels, "Dudley", "Rod's Fuel", "HUR-L1", "PSS Alpha"); the admin
/// can still shorten one on Map places. Hidden: a place of the game data the game hides in its starmap (HiddenSpots): the map knows it but does not draw it.
/// </summary>
public sealed record Place(string System, string Id, string Name, string Short, PlaceKind Kind, string? Parent, double X, double Y, bool Hidden = false)
{
    /// <summary>"pyro/ruin-station": unique over the systems (a "Nyx Gateway" is in Pyro and in Stanton).</summary>
    public string Key => System + "/" + Id;
}

/// <summary>A star system of the map.</summary>
public sealed record StarSystem(string Id, string Name);

/// <summary>
/// The star systems from above, for the Map tool: the stars, planets, moons, cities, gateways and stations of Pyro, Stanton and Nyx, with their positions from the
/// Star Citizen Wiki API (api.star-citizen.wiki, /api/locations/positions, downloaded with ScApiTool on 20 Oct 2026; Pyro's are the same as PyroDistances').
/// Planets do not move in the game, so the positions stay valid. Also which place a quantum destination of the log is, and a name the game says.
/// </summary>
public static class StarMap
{
    public static readonly StarSystem[] Systems = { new("pyro", "Pyro"), new("stanton", "Stanton"), new("nyx", "Nyx") };

    public static readonly Place[] Places =
    {
        new("pyro", "pyro", "Pyro", "Pyro", PlaceKind.Star, null, 0.0000, 0.0000),
        new("pyro", "bloom", "Bloom", "Bloom", PlaceKind.Planet, null, -8.9008, -15.4171),
        new("pyro", "monox", "Monox", "Monox", PlaceKind.Planet, null, 9.1984, 5.3107),
        new("pyro", "pyro-i", "Pyro I", "Pyro I", PlaceKind.Planet, null, 1.7200, -8.0920),
        new("pyro", "pyro-iv", "Pyro IV", "Pyro IV", PlaceKind.Planet, "pyro-v", 42.6145, -7.2730),
        new("pyro", "pyro-v", "Pyro V", "Pyro V", PlaceKind.Planet, null, 42.2853, -7.4546),
        new("pyro", "terminus", "Terminus", "Terminus", PlaceKind.Planet, null, -52.3365, -43.9182),
        new("pyro", "adir", "Adir", "Adir", PlaceKind.Moon, "pyro-v", 42.2638, -7.3557),
        new("pyro", "fairo", "Fairo", "Fairo", PlaceKind.Moon, "pyro-v", 42.4031, -7.4985),
        new("pyro", "fuego", "Fuego", "Fuego", PlaceKind.Moon, "pyro-v", 42.3645, -7.3284),
        new("pyro", "ignis", "Ignis", "Ignis", PlaceKind.Moon, "pyro-v", 42.2969, -7.5086),
        new("pyro", "vatra", "Vatra", "Vatra", PlaceKind.Moon, "pyro-v", 42.2167, -7.4262),
        new("pyro", "vuur", "Vuur", "Vuur", PlaceKind.Moon, "pyro-v", 42.1644, -7.5825),
        new("pyro", "nyx-gateway", "Nyx Gateway", "Nyx Gateway", PlaceKind.Gateway, null, 32.9940, -38.1517),
        new("pyro", "stanton-gateway", "Stanton Gateway", "Stanton Gateway", PlaceKind.Gateway, null, -37.6092, 37.4357),
        new("pyro", "checkmate", "Checkmate", "Checkmate", PlaceKind.Station, null, 1.8334, 10.4345),
        new("pyro", "dudley-daughters", "Dudley & Daughters", "Dudley & Daughters", PlaceKind.Station, null, 23.3426, -64.2260),
        new("pyro", "endgame", "Endgame", "Endgame", PlaceKind.Station, null, 48.2968, 48.2920),
        new("pyro", "gaslight", "Gaslight", "Gaslight", PlaceKind.Station, null, 46.4944, -8.1909),
        new("pyro", "megumi-refueling", "Megumi Refueling", "Megumi Refueling", PlaceKind.Station, null, -59.1456, 34.1455),
        new("pyro", "orbituary", "Orbituary", "Orbituary", PlaceKind.Station, "bloom", -8.9004, -15.4178),
        new("pyro", "patch-city", "Patch City", "Patch City", PlaceKind.Station, null, 11.4453, 13.6223),
        new("pyro", "rats-nest", "Rat's Nest", "Rat's Nest", PlaceKind.Station, null, 3.7326, -42.7669),
        new("pyro", "rods-fuel-n-supplies", "Rod's Fuel 'N Supplies", "Rod's Fuel 'N Supplies", PlaceKind.Station, null, 14.6643, 40.3386),
        new("pyro", "ruin-station", "Ruin Station", "Ruin Station", PlaceKind.Station, "terminus", -52.3368, -43.9179),
        new("pyro", "starlight-service-station", "Starlight Service Station", "Starlight Service Station", PlaceKind.Station, null, -7.7566, -14.0089),
        new("stanton", "stanton", "Stanton", "Stanton", PlaceKind.Star, null, 0.0000, 0.0000),
        new("stanton", "arccorp", "ArcCorp", "ArcCorp", PlaceKind.Planet, null, 18.5877, -22.1519),
        new("stanton", "crusader", "Crusader", "Crusader", PlaceKind.Planet, null, -18.9622, -2.6650),
        new("stanton", "hurston", "Hurston", "Hurston", PlaceKind.Planet, null, 12.8505, 0.0000),
        new("stanton", "microtech", "microTech", "microTech", PlaceKind.Planet, null, 22.4621, 37.1857),
        new("stanton", "aberdeen", "Aberdeen", "Aberdeen", PlaceKind.Moon, "hurston", 12.9058, 0.0410),
        new("stanton", "arial", "Arial", "Arial", PlaceKind.Moon, "hurston", 12.8927, -0.0315),
        new("stanton", "calliope", "Calliope", "Calliope", PlaceKind.Moon, "microtech", 22.5258, 37.2026),
        new("stanton", "cellin", "Cellin", "Cellin", PlaceKind.Moon, "crusader", -18.9876, -2.7090),
        new("stanton", "clio", "Clio", "Clio", PlaceKind.Moon, "microtech", 22.4474, 37.2805),
        new("stanton", "daymar", "Daymar", "Daymar", PlaceKind.Moon, "crusader", -18.9305, -2.6102),
        new("stanton", "euterpe", "Euterpe", "Euterpe", PlaceKind.Moon, "microtech", 22.4361, 37.2904),
        new("stanton", "ita", "Ita", "Ita", PlaceKind.Moon, "hurston", 12.8302, 0.1149),
        new("stanton", "lyria", "Lyria", "Lyria", PlaceKind.Moon, "arccorp", 18.7036, -22.1217),
        new("stanton", "magda", "Magda", "Magda", PlaceKind.Moon, "hurston", 12.7927, -0.0745),
        new("stanton", "wala", "Wala", "Wala", PlaceKind.Moon, "arccorp", 18.3796, -22.0005),
        new("stanton", "yela", "Yela", "Yela", PlaceKind.Moon, "crusader", -19.0229, -2.6140),
        new("stanton", "area18", "Area18", "Area18", PlaceKind.City, "arccorp", 18.5869, -22.1520),
        new("stanton", "lorville", "Lorville", "Lorville", PlaceKind.City, "hurston", 12.8501, -0.0008),
        new("stanton", "new-babbage", "New Babbage", "New Babbage", PlaceKind.City, "microtech", 22.4626, 37.1862),
        new("stanton", "orison", "Orison", "Orison", PlaceKind.City, "crusader", -18.9569, -2.6658),
        new("stanton", "nyx-gateway", "Nyx Gateway", "Nyx Gateway", PlaceKind.Gateway, null, -62.2843, 23.4676),
        new("stanton", "pyro-gateway", "Pyro Gateway", "Pyro Gateway", PlaceKind.Gateway, null, 3.3105, -27.9794),
        new("stanton", "terra-gateway", "Terra Gateway", "Terra Gateway", PlaceKind.Gateway, null, 51.1182, -5.2700),
        new("stanton", "arc-l1-wide-forest-station", "ARC-L1 Wide Forest Station", "ARC-L1 Wide Forest Station", PlaceKind.Station, null, 16.7292, -19.9420),
        new("stanton", "arc-l2-lively-pathway-station", "ARC-L2 Lively Pathway Station", "ARC-L2 Lively Pathway Station", PlaceKind.Station, null, 20.4510, -24.3681),
        new("stanton", "arc-l3-modern-express-station", "ARC-L3 Modern Express Station", "ARC-L3 Modern Express Station", PlaceKind.Station, null, -25.0455, 14.4633),
        new("stanton", "arc-l4-faint-glen-station", "ARC-L4 Faint Glen Station", "ARC-L4 Faint Glen Station", PlaceKind.Station, null, 28.4761, 5.0189),
        new("stanton", "arc-l5-yellow-core-station", "ARC-L5 Yellow Core Station", "ARC-L5 Yellow Core Station", PlaceKind.Station, null, -9.8958, -27.1752),
        new("stanton", "baijini-point", "Baijini Point", "Baijini Point", PlaceKind.Station, "arccorp", 18.5869, -22.1522),
        new("stanton", "covalex-shipping-hub-gundo", "Covalex Shipping Hub Gundo", "Covalex Shipping Hub Gundo", PlaceKind.Station, "daymar", -18.9308, -2.6097),
        new("stanton", "cru-l1-ambitious-dream-station", "CRU-L1 Ambitious Dream Station", "CRU-L1 Ambitious Dream Station", PlaceKind.Station, null, -17.0688, -2.3995),
        new("stanton", "cru-l4-shallow-fields-station", "CRU-L4 Shallow Fields Station", "CRU-L4 Shallow Fields Station", PlaceKind.Station, null, -7.1738, -17.7501),
        new("stanton", "cru-l5-beautiful-glen-station", "CRU-L5 Beautiful Glen Station", "CRU-L5 Beautiful Glen Station", PlaceKind.Station, null, -11.7850, 15.0912),
        new("stanton", "everus-harbor", "Everus Harbor", "Everus Harbor", PlaceKind.Station, "hurston", 12.8499, -0.0009),
        new("stanton", "grim-hex", "Grim HEX", "Grim HEX", PlaceKind.Station, "yela", -19.0235, -2.6136),
        new("stanton", "hur-l1-green-glade-station", "HUR-L1 Green Glade Station", "HUR-L1 Green Glade Station", PlaceKind.Station, null, 11.5614, -0.0003),
        new("stanton", "hur-l2-faithful-dream-station", "HUR-L2 Faithful Dream Station", "HUR-L2 Faithful Dream Station", PlaceKind.Station, null, 14.1396, -0.0020),
        new("stanton", "hur-l3-thundering-express-station", "HUR-L3 Thundering Express Station", "HUR-L3 Thundering Express Station", PlaceKind.Station, null, -12.8447, -0.0016),
        new("stanton", "hur-l4-melodic-fields-station", "HUR-L4 Melodic Fields Station", "HUR-L4 Melodic Fields Station", PlaceKind.Station, null, 6.4276, 11.1248),
        new("stanton", "hur-l5-high-course-station", "HUR-L5 High Course Station", "HUR-L5 High Course Station", PlaceKind.Station, null, 6.4257, -11.1284),
        new("stanton", "mic-l1-shallow-frontier-station", "MIC-L1 Shallow Frontier Station", "MIC-L1 Shallow Frontier Station", PlaceKind.Station, null, 20.2158, 33.4671),
        new("stanton", "mic-l2-long-forest-station", "MIC-L2 Long Forest Station", "MIC-L2 Long Forest Station", PlaceKind.Station, null, 24.7138, 40.9129),
        new("stanton", "mic-l3-endless-odyssey-station", "MIC-L3 Endless Odyssey Station", "MIC-L3 Endless Odyssey Station", PlaceKind.Station, null, -22.4522, -37.1799),
        new("stanton", "mic-l4-red-crossroads-station", "MIC-L4 Red Crossroads Station", "MIC-L4 Red Crossroads Station", PlaceKind.Station, null, -20.9687, 38.0561),
        new("stanton", "mic-l5-modern-icarus-station", "MIC-L5 Modern Icarus Station", "MIC-L5 Modern Icarus Station", PlaceKind.Station, null, 43.4486, -0.8577),
        new("stanton", "port-tressler", "Port Tressler", "Port Tressler", PlaceKind.Station, "microtech", 22.4626, 37.1863),
        new("stanton", "security-post-kareah", "Security Post Kareah", "Security Post Kareah", PlaceKind.Station, "cellin", -18.9876, -2.7094),
        new("stanton", "seraphim-station", "Seraphim Station", "Seraphim Station", PlaceKind.Station, "crusader", -18.9563, -2.6663),
        new("stanton", "wikelo-emporium-dasi-station", "Wikelo Emporium Dasi Station", "Wikelo Emporium Dasi Station", PlaceKind.Station, null, 14.3334, 2.0012),
        new("stanton", "wikelo-emporium-kinga-station", "Wikelo Emporium Kinga Station", "Wikelo Emporium Kinga Station", PlaceKind.Station, null, 23.7614, 39.2254),
        new("stanton", "wikelo-emporium-selo-station", "Wikelo Emporium Selo Station", "Wikelo Emporium Selo Station", PlaceKind.Station, null, -20.1566, -1.2631),
        new("nyx", "nyx", "Nyx", "Nyx", PlaceKind.Star, null, 0.0000, 0.0000),
        new("nyx", "nyx-i", "Nyx I", "Nyx I", PlaceKind.Planet, null, 6.4952, -3.7500),
        new("nyx", "nyx-ii", "Nyx II", "Nyx II", PlaceKind.Planet, null, -3.5912, 9.8668),
        new("nyx", "nyx-iii", "Nyx III", "Nyx III", PlaceKind.Planet, null, -29.1097, -24.4259),
        new("nyx", "pyro-gateway", "Pyro Gateway", "Pyro Gateway", PlaceKind.Gateway, null, -12.0000, 20.7846),
        new("nyx", "stanton-gateway", "Stanton Gateway", "Stanton Gateway", PlaceKind.Gateway, null, -13.4999, -23.3827),
        new("nyx", "levski", "Levski", "Levski", PlaceKind.Station, null, -9.6417, -11.4907),
        new("nyx", "peoples-service-station-alpha", "People's Service Station Alpha", "People's Service Station Alpha", PlaceKind.Station, null, -45.8043, -14.3513),
        new("nyx", "peoples-service-station-delta", "People's Service Station Delta", "People's Service Station Delta", PlaceKind.Station, null, -6.4325, -47.5670),
        new("nyx", "peoples-service-station-lambda", "People's Service Station Lambda", "People's Service Station Lambda", PlaceKind.Station, null, 43.3278, 20.6568),
        new("nyx", "peoples-service-station-theta", "People's Service Station Theta", "People's Service Station Theta", PlaceKind.Station, null, 40.4727, -25.8063),
        new("nyx", "qv-breaker-station-brk-127", "QV Breaker Station BRK-127", "QV Breaker Station BRK-127", PlaceKind.Station, null, 47.2704, -8.3350),
        new("nyx", "qv-breaker-station-brk-204", "QV Breaker Station BRK-204", "QV Breaker Station BRK-204", PlaceKind.Station, null, -45.1050, 16.4169),
        new("nyx", "qv-breaker-station-brk-320", "QV Breaker Station BRK-320", "QV Breaker Station BRK-320", PlaceKind.Station, null, -27.5312, 39.3186),
        new("nyx", "qv-breaker-station-brk-425", "QV Breaker Station BRK-425", "QV Breaker Station BRK-425", PlaceKind.Station, null, 27.5310, -39.3184),
        new("nyx", "qv-breaker-station-brk-546", "QV Breaker Station BRK-546", "QV Breaker Station BRK-546", PlaceKind.Station, null, 16.4169, 45.1052),
        new("nyx", "transit-point-glaciem-alpha", "Transit Point Glaciem Alpha", "Transit Point Glaciem Alpha", PlaceKind.Station, null, -13.5924, 6.3382),
        new("nyx", "transit-point-glaciem-bravo", "Transit Point Glaciem Bravo", "Transit Point Glaciem Bravo", PlaceKind.Station, null, 9.6402, -11.4888),
        new("nyx", "transit-point-glaciem-charlie", "Transit Point Glaciem Charlie", "Transit Point Glaciem Charlie", PlaceKind.Station, null, 9.6402, 11.4888),
    };

    static readonly Dictionary<string, Place> ByKey = Places.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>A place of the game data by its key ("pyro/ruin-station"), or null (the admin's changes: Atlas).</summary>
    public static Place? Find(string? key) => key != null && ByKey.TryGetValue(key, out var p) ? p : null;

    /// <summary>The places of a system.</summary>
    public static IEnumerable<Place> Of(string system) => Places.Where(p => p.System == system);

    /// <summary>The flat distance between two places in Gm (the systems are nearly flat).</summary>
    public static double Distance(Place a, Place b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The planets of a system in their order (the game's "pyro3" is the third: Bloom).</summary>
    internal static readonly Dictionary<string, string[]> PlanetOrder = new()
    {
        ["pyro"] = new[] { "pyro-i", "monox", "bloom", "pyro-iv", "pyro-v", "terminus" },
        ["stanton"] = new[] { "hurston", "crusader", "arccorp", "microtech" },
        ["nyx"] = new[] { "nyx-i", "nyx-ii", "nyx-iii" },
    };

    /// <summary>Stanton's planets as the names of their Lagrange stations start ("HUR-L1").</summary>
    internal static readonly string[] StantonPrefix = { "HUR", "CRU", "ARC", "MIC" };

    /// <summary>The Hauling Planner's Pyro stations (PyroDistances ids) and their places.</summary>
    internal static readonly Dictionary<string, string> PyroStations = new()
    {
        [PyroDistances.Ruin] = "ruin-station", ["73791"] = "rats-nest", ["70269"] = "megumi-refueling", ["70120"] = "patch-city", ["69936"] = "gaslight",
        ["72785"] = "dudley-daughters", ["73041"] = "orbituary", ["72645"] = "rods-fuel-n-supplies", ["70053"] = "starlight-service-station",
        ["72952"] = "checkmate", ["74320"] = "nyx-gateway", ["74325"] = "stanton-gateway", ["70273"] = "endgame",
    };

    /// <summary>The place of one of the Hauling Planner's Pyro stations (its id in PyroDistances), or null.</summary>
    public static Place? OfPyroStation(string? id) => Atlas.Default.OfPyroStation(id);

    static readonly Dictionary<string, string> PyroIdOf = PyroStations.ToDictionary(kv => "pyro/" + kv.Value, kv => kv.Key);

    /// <summary>The id of one of the Hauling Planner's Pyro stations (PyroDistances) for a place of the map, or null for any other place (or none).</summary>
    public static string? PyroStationIdOf(Place? place) => place != null && PyroIdOf.TryGetValue(place.Key, out var id) ? id : null;

    /// <summary>
    /// How far a quantum jump between two places is (Gm): between two of the Hauling Planner's Pyro stations as it has it (the legs from Ruin as the game says
    /// them), else straight.
    /// </summary>
    public static double JumpGm(Place a, Place b) =>
        PyroIdOf.TryGetValue(a.Key, out var ia) && PyroIdOf.TryGetValue(b.Key, out var ib) ? PyroDistances.Between(ia, ib) : Distance(a, b);

    /// <summary>A name in small letters and digits only ("Rod's Fuel 'N Supplies" is "rodsfuelnsupplies"): names compare so.</summary>
    public static string Letters(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>A key for a name: small letters and digits, a dash between the words ("Rod's Fuel 'N Supplies" is "rods-fuel-n-supplies").</summary>
    public static string Slug(string name)
    {
        var words = Regex.Split(name.ToLowerInvariant().Replace("'", "").Replace("\u2019", ""), "[^a-z0-9]+").Where(w => w.Length > 0);
        return string.Join("-", words);
    }

    internal static readonly Regex Planet = new(@"^(?:rs_ext_)?(?<s>pyro|stanton|nyx)(?<n>\d)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    internal static readonly Regex RestStop = new(@"(?:RR_S(?<n>\d)_L(?<l>\d)|stanton(?<n>\d)_l(?<l>\d))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // The game's own code for a Pyro station (Rest & Relax's "RR", which the game puts on every station and gateway of a system, whoever runs it): planet and
    // Lagrange point or low orbit, "RR_P6_L5" (Megumi Refueling) in the areas of the inventory lines, "PYR3 L3" (Patch City) as a start name; the same
    // as the quantum destination "rs_ext_pyro6_l5" (Frank, 31 Oct 2026).
    internal static readonly Regex PyroStop = new(@"^(?:LOC_)?(?:RR_P|PYR)(?<n>\d)[_ ]+(?<l>L\d|LEO)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // The same for a Stanton station, by its planet's three letters: "RR_CRU_L1" (CRU-L1 Ambitious Dream Station) and "RR_HUR_LEO" (Everus Harbor, in low
    // orbit) in the areas, "CRU L1" as a start name (zzoega's logs, 31 Oct 2026).
    internal static readonly Regex StantonStop = new(@"^(?:LOC_)?(?:RR_)?(?<p>HUR|CRU|ARC|MIC)[_ ](?<l>L\d|LEO)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The station in low orbit of each Stanton planet, by its three letters ("RR_HUR_LEO" is Everus Harbor).</summary>
    internal static readonly Dictionary<string, string> StantonLeo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HUR"] = "stanton/everus-harbor", ["CRU"] = "stanton/seraphim-station", ["ARC"] = "stanton/baijini-point", ["MIC"] = "stanton/port-tressler",
    };

    // A gateway in the game's own code: "RR_JP_PyroStanton" is the one in Pyro to Stanton (Stanton Gateway in Pyro).
    internal static readonly Regex RestStopGate = new(@"^(?:LOC_)?RR_JP_(?<a>Pyro|Stanton|Nyx)(?<b>Pyro|Stanton|Nyx|Terra)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    internal static readonly Regex Gate = new(@"(?<a>pyro|stanton|nyx)-(?<b>pyro|stanton|nyx|stan|terra)_jp", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The place a quantum destination of the log is, in the game data (Atlas.OfQuantumTarget).</summary>
    public static Place? OfQuantumTarget(string target, string? system = null) => Atlas.Default.OfQuantumTarget(target, system);

    /// <summary>What a name the game says in plain words is, in the game data (Atlas.OfName).</summary>
    public static (string? System, Place? Place) OfName(string name, string? system = null, Place? at = null) => Atlas.Default.OfName(name, system, at);
}
