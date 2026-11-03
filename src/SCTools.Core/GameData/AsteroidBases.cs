namespace SCTools.GameData;

/// <summary>
/// Pyro's asteroid bases (Frank, 3 Nov 2026: "all RAB and RMB should be in the drop down menu, they're surrounded by missions"): the RAB bases, which a
/// quantum jump can go to (Asteroid_ValidQT), and the RMB bases (Asteroid), from the Star Citizen Wiki API's /api/locations/positions downloaded with
/// ScApiTool on 20 Oct 2026. The game shows them in its starmap, but there are 115 of them, so the map knows them as hidden spots (HiddenSpots.All): drawn
/// only where a player is, a mission's beacon placed at one by its fuel estimate (BeaconRanging), a /showlocation within 50 km of one there. Unlike the
/// other hidden spots their names are their own, so the log's name for one ("Projected Start Location is RAB-GULF") is that base (Atlas), and the admin
/// can pick one for a code the map does not know (MapPlaces.PickGroups). A mission's encounter code at a base is not the base's name
/// ("RegionB_2base_ab_pyro_final_set_encounter-001": every Perseus fuel estimate to it since August fits RAB-GULF's distance within 1 Mm), so the admin says
/// which base it is.
/// </summary>
public static class AsteroidBases
{
    public static readonly Place[] All =
    {
        // the RAB bases: quantum targets (29)
        new("pyro", "rab-alpha", "RAB-ALPHA", "RAB-ALPHA", PlaceKind.Station, null, -2.3973, 7.2673, Hidden: true),
        new("pyro", "rab-bravo", "RAB-BRAVO", "RAB-BRAVO", PlaceKind.Station, null, -16.6677, 1.9431, Hidden: true),
        new("pyro", "rab-charlie", "RAB-CHARLIE", "RAB-CHARLIE", PlaceKind.Station, null, -2.0660, -16.0912, Hidden: true),
        new("pyro", "rab-cook", "RAB-COOK", "RAB-COOK", PlaceKind.Station, null, -60.6956, -43.7044, Hidden: true),
        new("pyro", "rab-delta", "RAB-DELTA", "RAB-DELTA", PlaceKind.Station, null, 42.1140, -7.5210, Hidden: true),
        new("pyro", "rab-echo", "RAB-ECHO", "RAB-ECHO", PlaceKind.Station, null, 42.6389, -7.3328, Hidden: true),
        new("pyro", "rab-foxtrot", "RAB-FOXTROT", "RAB-FOXTROT", PlaceKind.Station, null, 28.6760, 0.8712, Hidden: true),
        new("pyro", "rab-gulf", "RAB-GULF", "RAB-GULF", PlaceKind.Station, null, -13.1700, -24.0824, Hidden: true),
        new("pyro", "rab-helio", "RAB-HELIO", "RAB-HELIO", PlaceKind.Station, null, 42.2026, -2.8056, Hidden: true),
        new("pyro", "rab-ignition", "RAB-IGNITION", "RAB-IGNITION", PlaceKind.Station, null, -12.1743, 12.9990, Hidden: true),
        new("pyro", "rab-ion", "RAB-ION", "RAB-ION", PlaceKind.Station, null, -49.2856, -48.8330, Hidden: true),
        new("pyro", "rab-jak", "RAB-JAK", "RAB-JAK", PlaceKind.Station, null, 51.6758, 0.3297, Hidden: true),
        new("pyro", "rab-kilo", "RAB-KILO", "RAB-KILO", PlaceKind.Station, null, 10.8241, 14.0561, Hidden: true),
        new("pyro", "rab-lamda", "RAB-LAMDA", "RAB-LAMDA", PlaceKind.Station, null, 42.5624, -7.2616, Hidden: true),
        new("pyro", "rab-lynx", "RAB-LYNX", "RAB-LYNX", PlaceKind.Station, null, -54.2639, -39.3289, Hidden: true),
        new("pyro", "rab-mat", "RAB-MAT", "RAB-MAT", PlaceKind.Station, null, -39.5149, -21.2875, Hidden: true),
        new("pyro", "rab-november", "RAB-NOVEMBER", "RAB-NOVEMBER", PlaceKind.Station, null, 8.2779, -6.1882, Hidden: true),
        new("pyro", "rab-over", "RAB-OVER", "RAB-OVER", PlaceKind.Station, null, 42.6032, -7.1983, Hidden: true),
        new("pyro", "rab-point", "RAB-POINT", "RAB-POINT", PlaceKind.Station, null, 38.2139, -7.1716, Hidden: true),
        new("pyro", "rab-quagmire", "RAB-QUAGMIRE", "RAB-QUAGMIRE", PlaceKind.Station, null, 54.0783, -14.4146, Hidden: true),
        new("pyro", "rab-roth", "RAB-ROTH", "RAB-ROTH", PlaceKind.Station, null, 24.9009, -12.4142, Hidden: true),
        new("pyro", "rab-sierra", "RAB-SIERRA", "RAB-SIERRA", PlaceKind.Station, null, 46.3909, -8.8770, Hidden: true),
        new("pyro", "rab-tung", "RAB-TUNG", "RAB-TUNG", PlaceKind.Station, null, -3.1698, 8.6740, Hidden: true),
        new("pyro", "rab-ultra", "RAB-ULTRA", "RAB-ULTRA", PlaceKind.Station, null, -28.2059, -33.3948, Hidden: true),
        new("pyro", "rab-victory", "RAB-VICTORY", "RAB-VICTORY", PlaceKind.Station, null, 46.1969, -4.7552, Hidden: true),
        new("pyro", "rab-whiskey", "RAB-WHISKEY", "RAB-WHISKEY", PlaceKind.Station, null, -8.3813, -17.6976, Hidden: true),
        new("pyro", "rab-xeno", "RAB-XENO", "RAB-XENO", PlaceKind.Station, null, 27.6986, -23.6946, Hidden: true),
        new("pyro", "rab-york", "RAB-YORK", "RAB-YORK", PlaceKind.Station, null, 0.9431, -13.8592, Hidden: true),
        new("pyro", "rab-zeta", "RAB-ZETA", "RAB-ZETA", PlaceKind.Station, null, 45.1983, -11.4839, Hidden: true),
        // the RMB bases (86)
        new("pyro", "rmb-alme", "RMB-ALME", "RMB-ALME", PlaceKind.Station, null, 42.3389, -7.6070, Hidden: true),
        new("pyro", "rmb-arid", "RMB-ARID", "RMB-ARID", PlaceKind.Station, null, 42.6909, -7.1338, Hidden: true),
        new("pyro", "rmb-axil", "RMB-AXIL", "RMB-AXIL", PlaceKind.Station, null, -9.4059, -16.5221, Hidden: true),
        new("pyro", "rmb-bale", "RMB-BALE", "RMB-BALE", PlaceKind.Station, null, -5.9720, -6.4113, Hidden: true),
        new("pyro", "rmb-balm", "RMB-BALM", "RMB-BALM", PlaceKind.Station, null, 3.2931, -6.7046, Hidden: true),
        new("pyro", "rmb-bask", "RMB-BASK", "RMB-BASK", PlaceKind.Station, null, -10.8123, -17.0474, Hidden: true),
        new("pyro", "rmb-bors", "RMB-BORS", "RMB-BORS", PlaceKind.Station, null, 45.8194, -5.8491, Hidden: true),
        new("pyro", "rmb-buck", "RMB-BUCK", "RMB-BUCK", PlaceKind.Station, null, -10.5332, -16.1691, Hidden: true),
        new("pyro", "rmb-cedi", "RMB-CEDI", "RMB-CEDI", PlaceKind.Station, null, -5.2435, -5.5583, Hidden: true),
        new("pyro", "rmb-cham", "RMB-CHAM", "RMB-CHAM", PlaceKind.Station, null, -56.4864, -40.9693, Hidden: true),
        new("pyro", "rmb-crue", "RMB-CRUE", "RMB-CRUE", PlaceKind.Station, null, -53.1529, -41.7592, Hidden: true),
        new("pyro", "rmb-dari", "RMB-DARI", "RMB-DARI", PlaceKind.Station, null, -13.9956, -12.4005, Hidden: true),
        new("pyro", "rmb-dirk", "RMB-DIRK", "RMB-DIRK", PlaceKind.Station, null, -32.1646, -55.7322, Hidden: true),
        new("pyro", "rmb-dory", "RMB-DORY", "RMB-DORY", PlaceKind.Station, null, 40.9771, -6.0395, Hidden: true),
        new("pyro", "rmb-dreg", "RMB-DREG", "RMB-DREG", PlaceKind.Station, null, 7.0438, -3.0549, Hidden: true),
        new("pyro", "rmb-elms", "RMB-ELMS", "RMB-ELMS", PlaceKind.Station, null, 11.2299, 4.9222, Hidden: true),
        new("pyro", "rmb-even", "RMB-EVEN", "RMB-EVEN", PlaceKind.Station, null, 11.2234, 5.9573, Hidden: true),
        new("pyro", "rmb-flag", "RMB-FLAG", "RMB-FLAG", PlaceKind.Station, null, 46.8306, -5.9763, Hidden: true),
        new("pyro", "rmb-gade", "RMB-GADE", "RMB-GADE", PlaceKind.Station, null, -14.0265, -12.5101, Hidden: true),
        new("pyro", "rmb-gray", "RMB-GRAY", "RMB-GRAY", PlaceKind.Station, null, -13.8050, -12.4956, Hidden: true),
        new("pyro", "rmb-grex", "RMB-GREX", "RMB-GREX", PlaceKind.Station, null, 8.3529, 3.6727, Hidden: true),
        new("pyro", "rmb-hade", "RMB-HADE", "RMB-HADE", PlaceKind.Station, null, 42.4721, -7.1600, Hidden: true),
        new("pyro", "rmb-hark", "RMB-HARK", "RMB-HARK", PlaceKind.Station, null, -50.8586, -41.6901, Hidden: true),
        new("pyro", "rmb-helm", "RMB-HELM", "RMB-HELM", PlaceKind.Station, null, -10.0957, -17.6155, Hidden: true),
        new("pyro", "rmb-horn", "RMB-HORN", "RMB-HORN", PlaceKind.Station, null, -4.9594, -21.5647, Hidden: true),
        new("pyro", "rmb-howe", "RMB-HOWE", "RMB-HOWE", PlaceKind.Station, null, 7.7976, -7.5270, Hidden: true),
        new("pyro", "rmb-ikan", "RMB-IKAN", "RMB-IKAN", PlaceKind.Station, null, 8.9829, -2.7058, Hidden: true),
        new("pyro", "rmb-jade", "RMB-JADE", "RMB-JADE", PlaceKind.Station, null, -59.6735, -47.5770, Hidden: true),
        new("pyro", "rmb-jong", "RMB-JONG", "RMB-JONG", PlaceKind.Station, null, -7.2961, -13.2445, Hidden: true),
        new("pyro", "rmb-jute", "RMB-JUTE", "RMB-JUTE", PlaceKind.Station, null, 38.4834, -6.3853, Hidden: true),
        new("pyro", "rmb-kain", "RMB-KAIN", "RMB-KAIN", PlaceKind.Station, null, 1.9727, -9.7528, Hidden: true),
        new("pyro", "rmb-kelt", "RMB-KELT", "RMB-KELT", PlaceKind.Station, null, -55.8528, -50.3411, Hidden: true),
        new("pyro", "rmb-kiff", "RMB-KIFF", "RMB-KIFF", PlaceKind.Station, null, -5.7604, -21.3495, Hidden: true),
        new("pyro", "rmb-knap", "RMB-KNAP", "RMB-KNAP", PlaceKind.Station, null, -6.7375, -13.9618, Hidden: true),
        new("pyro", "rmb-kyus", "RMB-KYUS", "RMB-KYUS", PlaceKind.Station, null, 40.6790, -5.3868, Hidden: true),
        new("pyro", "rmb-lanx", "RMB-LANX", "RMB-LANX", PlaceKind.Station, null, 42.8412, -7.3570, Hidden: true),
        new("pyro", "rmb-lazo", "RMB-LAZO", "RMB-LAZO", PlaceKind.Station, null, 10.8326, 6.5615, Hidden: true),
        new("pyro", "rmb-lest", "RMB-LEST", "RMB-LEST", PlaceKind.Station, null, -8.7389, -13.0324, Hidden: true),
        new("pyro", "rmb-lire", "RMB-LIRE", "RMB-LIRE", PlaceKind.Station, null, 7.4030, -6.6873, Hidden: true),
        new("pyro", "rmb-lown", "RMB-LOWN", "RMB-LOWN", PlaceKind.Station, null, -54.7888, -40.8392, Hidden: true),
        new("pyro", "rmb-lune", "RMB-LUNE", "RMB-LUNE", PlaceKind.Station, null, -13.8727, -12.4245, Hidden: true),
        new("pyro", "rmb-lyes", "RMB-LYES", "RMB-LYES", PlaceKind.Station, null, 42.2848, -7.6504, Hidden: true),
        new("pyro", "rmb-mage", "RMB-MAGE", "RMB-MAGE", PlaceKind.Station, null, -13.9043, -12.5270, Hidden: true),
        new("pyro", "rmb-mara", "RMB-MARA", "RMB-MARA", PlaceKind.Station, null, -55.1043, -43.5466, Hidden: true),
        new("pyro", "rmb-meff", "RMB-MEFF", "RMB-MEFF", PlaceKind.Station, null, -47.1948, -44.1692, Hidden: true),
        new("pyro", "rmb-mosk", "RMB-MOSK", "RMB-MOSK", PlaceKind.Station, null, -51.8091, -49.2345, Hidden: true),
        new("pyro", "rmb-mugg", "RMB-MUGG", "RMB-MUGG", PlaceKind.Station, null, -5.8286, -21.5713, Hidden: true),
        new("pyro", "rmb-murk", "RMB-MURK", "RMB-MURK", PlaceKind.Station, null, 46.9498, -8.5698, Hidden: true),
        new("pyro", "rmb-myal", "RMB-MYAL", "RMB-MYAL", PlaceKind.Station, null, 3.4183, -8.3969, Hidden: true),
        new("pyro", "rmb-nain", "RMB-NAIN", "RMB-NAIN", PlaceKind.Station, null, 9.5438, 7.1141, Hidden: true),
        new("pyro", "rmb-nary", "RMB-NARY", "RMB-NARY", PlaceKind.Station, null, 37.5547, -6.8930, Hidden: true),
        new("pyro", "rmb-nene", "RMB-NENE", "RMB-NENE", PlaceKind.Station, null, -59.6215, -45.5025, Hidden: true),
        new("pyro", "rmb-nigh", "RMB-NIGH", "RMB-NIGH", PlaceKind.Station, null, 6.8074, 5.2442, Hidden: true),
        new("pyro", "rmb-nong", "RMB-NONG", "RMB-NONG", PlaceKind.Station, null, 42.5347, -7.4330, Hidden: true),
        new("pyro", "rmb-oaks", "RMB-OAKS", "RMB-OAKS", PlaceKind.Station, null, 42.3521, -7.2058, Hidden: true),
        new("pyro", "rmb-olla", "RMB-OLLA", "RMB-OLLA", PlaceKind.Station, null, 47.1781, -7.9875, Hidden: true),
        new("pyro", "rmb-oner", "RMB-ONER", "RMB-ONER", PlaceKind.Station, null, 0.7678, -8.9049, Hidden: true),
        new("pyro", "rmb-oxid", "RMB-OXID", "RMB-OXID", PlaceKind.Station, null, -7.0226, -14.8191, Hidden: true),
        new("pyro", "rmb-pall", "RMB-PALL", "RMB-PALL", PlaceKind.Station, null, -8.9755, -17.2211, Hidden: true),
        new("pyro", "rmb-peke", "RMB-PEKE", "RMB-PEKE", PlaceKind.Station, null, -5.4421, -21.7362, Hidden: true),
        new("pyro", "rmb-pize", "RMB-PIZE", "RMB-PIZE", PlaceKind.Station, null, -51.7249, -45.6143, Hidden: true),
        new("pyro", "rmb-plex", "RMB-PLEX", "RMB-PLEX", PlaceKind.Station, null, -32.1217, -55.9494, Hidden: true),
        new("pyro", "rmb-puri", "RMB-PURI", "RMB-PURI", PlaceKind.Station, null, 1.5452, -6.3612, Hidden: true),
        new("pyro", "rmb-qins", "RMB-QINS", "RMB-QINS", PlaceKind.Station, null, -53.3112, -47.0777, Hidden: true),
        new("pyro", "rmb-raik", "RMB-RAIK", "RMB-RAIK", PlaceKind.Station, null, -53.2878, -50.2894, Hidden: true),
        new("pyro", "rmb-rawn", "RMB-RAWN", "RMB-RAWN", PlaceKind.Station, null, -57.2733, -44.3240, Hidden: true),
        new("pyro", "rmb-rigg", "RMB-RIGG", "RMB-RIGG", PlaceKind.Station, null, -6.9878, -5.2782, Hidden: true),
        new("pyro", "rmb-rine", "RMB-RINE", "RMB-RINE", PlaceKind.Station, null, 42.2549, -7.5898, Hidden: true),
        new("pyro", "rmb-rode", "RMB-RODE", "RMB-RODE", PlaceKind.Station, null, 41.5707, -5.3816, Hidden: true),
        new("pyro", "rmb-saic", "RMB-SAIC", "RMB-SAIC", PlaceKind.Station, null, -31.7108, -55.7135, Hidden: true),
        new("pyro", "rmb-seer", "RMB-SEER", "RMB-SEER", PlaceKind.Station, null, -56.8781, -42.2251, Hidden: true),
        new("pyro", "rmb-shew", "RMB-SHEW", "RMB-SHEW", PlaceKind.Station, null, 7.5215, 5.9910, Hidden: true),
        new("pyro", "rmb-sijo", "RMB-SIJO", "RMB-SIJO", PlaceKind.Station, null, 42.8823, -7.1551, Hidden: true),
        new("pyro", "rmb-sowl", "RMB-SOWL", "RMB-SOWL", PlaceKind.Station, null, -49.0049, -45.5134, Hidden: true),
        new("pyro", "rmb-spaw", "RMB-SPAW", "RMB-SPAW", PlaceKind.Station, null, 45.8497, -7.8865, Hidden: true),
        new("pyro", "rmb-surd", "RMB-SURD", "RMB-SURD", PlaceKind.Station, null, 0.0052, -7.4407, Hidden: true),
        new("pyro", "rmb-tack", "RMB-TACK", "RMB-TACK", PlaceKind.Station, null, -8.9979, -13.7265, Hidden: true),
        new("pyro", "rmb-tian", "RMB-TIAN", "RMB-TIAN", PlaceKind.Station, null, 9.0958, -6.7829, Hidden: true),
        new("pyro", "rmb-tigs", "RMB-TIGS", "RMB-TIGS", PlaceKind.Station, null, -31.8045, -56.0225, Hidden: true),
        new("pyro", "rmb-tyro", "RMB-TYRO", "RMB-TYRO", PlaceKind.Station, null, 37.7816, -6.3481, Hidden: true),
        new("pyro", "rmb-umus", "RMB-UMUS", "RMB-UMUS", PlaceKind.Station, null, -48.4429, -42.5191, Hidden: true),
        new("pyro", "rmb-verd", "RMB-VERD", "RMB-VERD", PlaceKind.Station, null, 46.4451, -6.5416, Hidden: true),
        new("pyro", "rmb-voes", "RMB-VOES", "RMB-VOES", PlaceKind.Station, null, 8.0720, -1.5363, Hidden: true),
        new("pyro", "rmb-warb", "RMB-WARB", "RMB-WARB", PlaceKind.Station, null, -14.0764, -12.4505, Hidden: true),
        new("pyro", "rmb-yead", "RMB-YEAD", "RMB-YEAD", PlaceKind.Station, null, -5.4019, -21.2969, Hidden: true),
        new("pyro", "rmb-zarf", "RMB-ZARF", "RMB-ZARF", PlaceKind.Station, null, 7.3104, 4.4382, Hidden: true),
    };

    static readonly Dictionary<string, Place> ByName = All.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The base by its name as the game writes it ("RAB-GULF", "rmb-bale"), or null.</summary>
    public static Place? Named(string? name) => name != null && ByName.TryGetValue(name.Trim(), out var p) ? p : null;

    /// <summary>Whether a place is one of the bases (by its key).</summary>
    public static bool Is(Place p) => p.System == "pyro" && ByName.TryGetValue(p.Name, out var b) && b.Key == p.Key;
}
