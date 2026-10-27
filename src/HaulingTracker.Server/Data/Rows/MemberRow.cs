namespace HaulingTracker.Server.Data;

/// <summary>
/// A party member and what is set for him: his place in the party list, whether he is shown as inactive, his ship, quantum drive, colour and how many
/// batches his ship holds, and his Current location when it was set by hand. A name that only has settings (someone who left the party list) has no place.
/// </summary>
public sealed class MemberRow
{
    public string Name { get; set; } = "";

    /// <summary>His place in the party list (0 = first); null = not in the list.</summary>
    public int? PartyPosition { get; set; }

    /// <summary>Unticked as active in the party list (not shown in the party of the Session tab).</summary>
    public bool Inactive { get; set; }

    /// <summary>His ship (the Ships name, like "C2 Hercules Starlifter").</summary>
    public string? Ship { get; set; }

    /// <summary>The quantum drive in his ship (like "Eos S1").</summary>
    public string? QuantumDrive { get; set; }

    /// <summary>His colour (ARGB), picked in the Party table; null = the built-in one.</summary>
    public int? Color { get; set; }

    /// <summary>The batches his ship holds (0 = not flying); null = not set.</summary>
    public int? ShipCapacity { get; set; }

    /// <summary>His Current location when set by hand (a station id), and since when (UTC); null = the logs know where he is.</summary>
    public string? LocationId { get; set; }

    public DateTime? LocationSince { get; set; }
}
