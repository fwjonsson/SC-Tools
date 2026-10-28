namespace SCTools.Server.Data;

/// <summary>A mission one player took (or had shared to him), from his logs: kept for good, so the library knows how often a kind of mission was played.</summary>
public sealed class MissionRow
{
    public string Player { get; set; } = "";
    public string MissionId { get; set; } = "";

    /// <summary>The game's ContractId: the offer it came from (the same mission at another place has another one).</summary>
    public string ContractId { get; set; } = "";

    /// <summary>Its kind: its entry in the library (MissionKinds: its name, or the Hauling Planner's contract line); null while it has no name.</summary>
    public string? Kind { get; set; }

    /// <summary>The title the game showed (with Frank's string mod, his names).</summary>
    public string? Title { get; set; }

    public bool Shared { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }

    /// <summary>Complete, Fail or Abandon (MissionOutcome), or null while it is going.</summary>
    public string? Outcome { get; set; }
}

/// <summary>
/// A kind of mission in the Mission Library (MissionKinds: one name, or one contract line of the Hauling Planner): what the log does not say, typed in by the
/// players (another name, the rewards, the giver, notes). The aUEC of a Hauling Planner kind is what the planner pays for that contract line; the Gauntlet
/// Tracker counts with the rewards of the "Combat Gauntlet - Scenario #n" kinds.
/// </summary>
public sealed class MissionKindRow
{
    public string Key { get; set; } = "";

    /// <summary>The name as the game shows it (the first one seen), or "Hauling: 2 To Ruin".</summary>
    public string Title { get; set; } = "";

    /// <summary>The name typed in (null: the Title is shown).</summary>
    public string? Name { get; set; }

    public string? Giver { get; set; }
    public long? Auec { get; set; }
    public long? MgScrip { get; set; }
    public long? CouncilScrip { get; set; }
    public string? Notes { get; set; }

    /// <summary>Who changed it last, and when (null: nobody yet).</summary>
    public string? EditedBy { get; set; }
    public DateTime? EditedUtc { get; set; }
}

/// <summary>
/// The library before it went by name: one row per ContractId (which turned out to be one per offer, not per mission). What was typed in it is carried over
/// to the kinds once (MissionLibrary), then the row goes.
/// </summary>
public sealed class LibraryRow
{
    public string ContractId { get; set; } = "";

    /// <summary>The name typed in (null: the title from the log is shown).</summary>
    public string? Name { get; set; }

    public string? Giver { get; set; }
    public long? Auec { get; set; }
    public long? MgScrip { get; set; }
    public long? CouncilScrip { get; set; }
    public string? Notes { get; set; }

    /// <summary>Who changed it last, and when.</summary>
    public string? EditedBy { get; set; }
    public DateTime? EditedUtc { get; set; }

    /// <summary>When the Wiki was asked (null: not yet), and whether it knew the mission.</summary>
    public DateTime? WikiCheckedUtc { get; set; }
    public bool WikiFound { get; set; }
    public string? WikiTitle { get; set; }
    public string? WikiGiver { get; set; }
    public long? WikiRewardMin { get; set; }
    public long? WikiRewardMax { get; set; }
    public string? WikiCurrency { get; set; }
}
