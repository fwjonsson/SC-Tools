namespace SCTools.Server.Data;

/// <summary>
/// A ship a player owns (his fleet, on My page): its name, the quantum drive in it, and the qualities of that drive when it is crafted: the drive speed's and
/// the fuel efficiency's (the game's crafting takes a material for each).
/// </summary>
public sealed class FleetShipRow
{
    public int Id { get; set; }
    public string Player { get; set; } = "";

    /// <summary>The ship's name, as he typed or picked it ("C2 Hercules Starlifter").</summary>
    public string Ship { get; set; } = "";

    /// <summary>The quantum drive in it (a QuantumDrives key, like "TS-2 S3"), or null.</summary>
    public string? Drive { get; set; }

    /// <summary>The quality of the drive when it is crafted (0 to 1000: 1000 is +20 % drive speed, 0 is -20 %), or null when it is not.</summary>
    public int? Quality { get; set; }

    /// <summary>
    /// The fuel efficiency quality of the drive when it is crafted (0 to 1000: 1000 burns 20 % less fuel per distance, 0 burns 20 % more), or null for as
    /// bought (the same as 500).
    /// </summary>
    public int? FuelQuality { get; set; }

    public DateTime AddedUtc { get; set; }
}

/// <summary>The ship a player picked as the one he flies (null in the table: Automatic, the ship of his latest quantum jump).</summary>
public sealed class ActiveShipRow
{
    public string Player { get; set; } = "";
    public int FleetShipId { get; set; }
}
