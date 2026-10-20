namespace HaulingTracker;

/// <summary>A quantum drive: its top speed in Mm/s (megametres per second) and the spool time in seconds. Size 1 to 4, grade 1 (A) to 4 (D).</summary>
public sealed record QtDrive(string Name, string Maker, int Size, int Grade, double SpeedMm, double Spool)
{
    public string Key => Name + " S" + Size;
    public string Label => Key + " (" + SpeedMm.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ")";
}

/// <summary>
/// The quantum drives that ships come with, from the Star Citizen Wiki API (the speed and spool time of the drive each ship ships with).
/// A flight is estimated as spool time plus distance / top speed. The acceleration and braking phases are not in this table yet.
/// </summary>
public static class QuantumDrives
{
    /// <summary>Used for a worker without a drive set, so the estimate still has something to work with.</summary>
    public const double AssumedSpeedMm = 200;
    public const double AssumedSpool = 5;

    public static readonly QtDrive[] All =
    {
        new("Atlas", "Roberts Space Industries", 1, 1, 259.1, 4.2),
        new("Beacon", "Wei-Tek", 1, 3, 161.4, 4.4),
        new("Colossus", "Juno Starwerk", 1, 2, 241.1, 4.8),
        new("Drift", "RAMP Corporation", 1, 3, 167.4, 4.9),
        new("Eos", "Roberts Space Industries", 1, 3, 199.3, 5.1),
        new("Expedition", "Tarsus", 1, 3, 189.3, 4.6),
        new("Flood", "ArcCorp", 1, 4, 188.3, 5.3),
        new("FoxFire", "Ace Astrogation", 1, 2, 263.0, 4.8),
        new("Goliath", "Juno Starwerk", 1, 3, 219.2, 5.3),
        new("Hyperion", "Roberts Space Industries", 1, 2, 219.2, 4.6),
        new("LightFire", "Ace Astrogation", 1, 3, 239.1, 5.3),
        new("Rush", "ArcCorp", 1, 3, 209.2, 4.9),
        new("Siren", "Wei-Tek", 1, 2, 177.6, 4.0),
        new("Spectre", "RAMP Corporation", 1, 1, 217.6, 4.0),
        new("VK-00", "Wei-Tek", 1, 1, 209.8, 3.6),
        new("Zephyr", "RAMP Corporation", 1, 2, 184.1, 4.4),
        new("Bolon", "Juno Starwerk", 2, 3, 275.9, 8.0),
        new("Bolt", "RAMP Corporation", 2, 2, 231.8, 6.6),
        new("Crossfield", "Wei-Tek", 2, 3, 203.2, 6.6),
        new("Hemera", "Roberts Space Industries", 2, 1, 326.1, 6.2),
        new("Huracan", "Juno Starwerk", 2, 2, 303.5, 7.2),
        new("Khaos", "Roberts Space Industries", 2, 3, 250.9, 7.6),
        new("Nova", "RAMP Corporation", 2, 3, 210.7, 7.3),
        new("Odyssey", "Tarsus", 2, 3, 238.3, 6.9),
        new("SparkFire", "Ace Astrogation", 2, 3, 301.0, 8.0),
        new("SunFire", "Ace Astrogation", 2, 2, 331.1, 7.2),
        new("Torrent", "ArcCorp", 2, 3, 263.4, 7.3),
        new("VK-00", "Wei-Tek", 2, 1, 209.8, 3.6),
        new("XL-1", "Wei-Tek", 2, 1, 264.2, 5.4),
        new("Yeager", "Wei-Tek", 2, 2, 223.5, 6.0),
        new("Balandin", "Wei-Tek", 3, 2, 431.3, 7.0),
        new("Erebos", "Roberts Space Industries", 3, 1, 629.3, 7.3),
        new("Kama", "Juno Starwerk", 3, 3, 532.5, 9.3),
        new("Pontes", "Wei-Tek", 3, 3, 392.1, 7.7),
        new("Allegro", "Unknown", 4, 1, 1262.9, 9.9),
        new("Frontline", "Unknown", 4, 1, 974.2, 9.0),
    };

    public static QtDrive? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Label, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Seconds for one jump of this many Gm.</summary>
    public static double LegSeconds(double gm, double speedMm, double spool) => spool + gm * 1000.0 / speedMm;
}
