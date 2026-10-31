using System.Globalization;

namespace SCTools.GameData;

/// <summary>Every quantum drive in the game data, for the drive each worker has in his ship (the Party table) and the flying time estimates.</summary>
public static class QuantumDrives
{
    /// <summary>Used for a worker without a drive set, so the estimate still has something to work with.</summary>
    public static QtDrive Assumed => Find("Odyssey S2")!;

    public static readonly QtDrive[] All =
    {
        new("Atlas", "Roberts Space Industries", 1, "A", 259054500, 9990736, 26053770, 1500, 7.546e-9),
        new("Beacon", "Wei-Tek", 1, "C", 161410900, 9393000, 22045500, 1500, 1.862e-8),
        new("Burst", "ArcCorp", 1, "B", 230160000, 9393000, 23270250, 1500, 1.1368e-8),
        new("Colossus", "Juno Starwerk", 1, "B", 241120000, 11271600, 26944500, 1500, 5.488e-9),
        new("Drift", "RAMP Corporation", 1, "C", 167389100, 7172836, 17814550, 1500, 1.666e-8),
        new("Eos", "Roberts Space Industries", 1, "C", 199272700, 7685182, 20041360, 1500, 9.114e-9),
        new("Expedition", "Tarsus", 1, "C", 189309100, 8112136, 21154770, 1500, 9.8e-9),
        new("Flood", "ArcCorp", 1, "D", 188312700, 7685182, 19039300, 1500, 1.0094000000000001e-8),
        new("FoxFire", "Ace Astrogation", 1, "B", 263040000, 10848920, 28291720, 1500, 5.8799999999999995e-9),
        new("Goliath", "Juno Starwerk", 1, "C", 219200000, 10246910, 24495000, 1500, 5.8e-9),
        new("Hyperion", "Roberts Space Industries", 1, "B", 219200000, 8453700, 22045500, 1500, 8.232e-9),
        new("LightFire", "Ace Astrogation", 1, "C", 239127300, 9862650, 25719750, 1500, 4.9e-9),
        new("Rush", "ArcCorp", 1, "C", 209236400, 8539091, 21154770, 1500, 1.0682000000000001e-8),
        new("Siren", "Wei-Tek", 1, "B", 177552000, 10332300, 24250050, 1500, 2.058e-8),
        new("Spectre", "RAMP Corporation", 1, "A", 217605800, 9324687, 23158910, 1500, 1.862e-8),
        new("VK-00", "Wei-Tek", 1, "A", 209834200, 12210900, 28659150, 1500, 2.156e-8),
        new("Voyage", "Tarsus", 1, "B", 208240000, 8923350, 23270250, 1500, 9.114e-9),
        new("Vulcan", "Juno Starwerk", 1, "D", 197280000, 9222218, 22045500, 1500, 6.076e-9),
        new("Wayfare", "Tarsus", 1, "D", 170378200, 7300923, 19039300, 1500, 1.0094000000000001e-8),
        new("Zephyr", "RAMP Corporation", 1, "B", 184128000, 7890120, 19596000, 1500, 1.7639999999999998e-8),
        new("Aither", "Roberts Space Industries", 2, "B", 275942800, 5945657, 8994857, 84000000, 9.156e-9),
        new("Bolon", "Juno Starwerk", 2, "C", 275942800, 7206857, 9994285, 84000000, 6.54e-9),
        new("Bolt", "RAMP Corporation", 2, "B", 231792000, 5549280, 7995428, 84000000, 1.962e-8),
        new("Cascade", "ArcCorp", 2, "D", 237060000, 5405143, 7768285, 84000000, 1.1226999999999999e-8),
        new("Crossfield", "Wei-Tek", 2, "C", 203194300, 6606285, 8994857, 84000000, 2.0709999999999998e-8),
        new("Flash", "ArcCorp", 2, "B", 289740000, 6606285, 9494571, 84000000, 1.2644e-8),
        new("Hemera", "Roberts Space Industries", 2, "A", 326114300, 7026685, 10630280, 84000000, 8.393e-9),
        new("Huracan", "Juno Starwerk", 2, "B", 303537200, 7927542, 10993710, 84000000, 6.1040000000000004e-9),
        new("Khaos", "Roberts Space Industries", 2, "C", 250857100, 5405143, 8177143, 84000000, 1.0137e-8),
        new("Nova", "RAMP Corporation", 2, "C", 210720000, 5044800, 7268571, 84000000, 1.853e-8),
        new("Odyssey", "Tarsus", 2, "C", 238314300, 5705428, 8631428, 84000000, 1.09e-8),
        new("Quest", "Tarsus", 2, "D", 214482900, 5134885, 7768285, 84000000, 1.1226999999999999e-8),
        new("Sojourn", "Tarsus", 2, "B", 262145700, 6275971, 9494571, 84000000, 1.0354999999999999e-8),
        new("SparkFire", "Ace Astrogation", 2, "C", 301028600, 6936600, 10494000, 84000000, 5.45e-9),
        new("Spicule", "RAMP Corporation", 2, "A", 273936000, 6558240, 9449143, 84000000, 2.0709999999999998e-8),
        new("SunFire", "Ace Astrogation", 2, "B", 331131400, 7630260, 11543400, 84000000, 6.54e-9),
        new("Torrent", "ArcCorp", 2, "C", 263400000, 6005714, 8631428, 84000000, 1.1880999999999999e-8),
        new("XL-1", "Wei-Tek", 2, "A", 264152600, 8588171, 11693310, 84000000, 2.398e-8),
        new("Yaluk", "Juno Starwerk", 2, "D", 248348600, 6486171, 8994857, 84000000, 6.758e-9),
        new("Yeager", "Wei-Tek", 2, "B", 223513700, 7266914, 9894343, 84000000, 2.2890000000000002e-8),
        new("Agni", "Juno Starwerk", 3, "B", 585715600, 4685175, 5969838, 190000000, 3.6064e-8),
        new("Balandin", "Wei-Tek", 3, "B", 431299700, 4294744, 5372854, 190000000, 1.3524e-7),
        new("Drifter", "Tarsus", 3, "D", 413873400, 3034716, 4218356, 190000000, 6.6332e-8),
        new("Echo", "ArcCorp", 3, "D", 457439100, 3194438, 4218356, 190000000, 4.9588e-8),
        new("Erebos", "Roberts Space Industries", 3, "A", 629281300, 4152769, 5772488, 190000000, 4.9588e-8),
        new("Fissure", "ArcCorp", 3, "C", 508265600, 3549375, 4687063, 190000000, 7.019599999999999e-8),
        new("Impulse", "ArcCorp", 3, "B", 559092200, 3904313, 5155769, 190000000, 7.4704e-8),
        new("Kama", "Juno Starwerk", 3, "C", 532468700, 4259250, 5427125, 190000000, 3.864e-8),
        new("Metis", "Roberts Space Industries", 3, "C", 484062500, 3194438, 4440375, 190000000, 5.9892e-8),
        new("Pontes", "Wei-Tek", 3, "C", 392090600, 3904313, 4884413, 190000000, 1.2236e-7),
        new("Ranger", "Tarsus", 3, "B", 505845300, 3709097, 5155769, 190000000, 6.118e-8),
        new("TS-2", "Wei-Tek", 3, "A", 509717800, 5075606, 6349736, 190000000, 1.4168e-7),
        new("Tyche", "Roberts Space Industries", 3, "B", 532468700, 3513881, 4884413, 190000000, 5.4096e-8),
        new("Vesta", "Juno Starwerk", 3, "D", 479221900, 3833325, 4884413, 190000000, 3.9928e-8),
        new("Wanderer", "Tarsus", 3, "C", 459859400, 3371906, 4687063, 190000000, 6.44e-8),
        new("Allegro", "Unknown", 4, "A", 1262910000, 2404215, 21945000, 200000000, 6.13e-8),
        new("Mauler", "Unknown", 4, "A", 1142633000, 2610290, 21840500, 200000000),
    };

    /// <summary>The best quality of a crafted drive.</summary>
    public const int MaxQuality = 1000;

    /// <summary>
    /// How much faster a crafted drive of this quality is, in percent: 1000 is +20 %, 500 the same as bought, 0 is -20 %, in a straight line between.
    /// </summary>
    public static double CraftedSpeedPercent(int quality) => (Math.Clamp(quality, 0, MaxQuality) - MaxQuality / 2.0) / (MaxQuality / 2.0) * 20;

    /// <summary>
    /// How much more fuel per distance a drive of this crafted fuel efficiency quality burns, in percent: 1000 is -20 %, 500 the same as bought, 0 is +20 %, in
    /// a straight line between (the game's Crafting Lab, a TS-2: 0.1417 per m as bought, 0.11 at 1000, 0.17 at 0; Frank, 31 Oct 2026).
    /// </summary>
    public static double CraftedFuelPercent(int quality) => -CraftedSpeedPercent(quality);

    /// <summary>"+12 %" (nothing for 0).</summary>
    public static string PercentText(double percent) =>
        Math.Abs(percent) < 0.05 ? "" : (percent > 0 ? "+" : "-") + Math.Abs(percent).ToString("0.#", CultureInfo.InvariantCulture) + " %";

    public static QtDrive? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Label, key, StringComparison.OrdinalIgnoreCase));
}
