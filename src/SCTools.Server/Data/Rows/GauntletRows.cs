namespace SCTools.Server.Data;

/// <summary>A gauntlet mission of a player that ended (from his log). Kept for good: the uploaded logs are only kept for a while.</summary>
public sealed class GauntletRunRow
{
    public string Player { get; set; } = "";
    public string MissionId { get; set; } = "";
    public int Scenario { get; set; }
    public DateTime? AcceptedUtc { get; set; }
    public DateTime EndedUtc { get; set; }

    /// <summary>Complete, Abandon or Fail (GauntletOutcome).</summary>
    public string Outcome { get; set; } = "";
}

/// <summary>A player's own settings of the Gauntlet Tracker: what a MG scrip is worth in aUEC, and rewards he changed.</summary>
public sealed class GauntletSettingsRow
{
    public string Player { get; set; } = "";
    public long ScripValue { get; set; }

    /// <summary>Scenario to aUEC, only for the scenarios he changed.</summary>
    public Dictionary<int, long> RewardAuec { get; set; } = new();

    /// <summary>Scenario to MG scrip, only for the scenarios he changed.</summary>
    public Dictionary<int, long> RewardScrip { get; set; } = new();
}

/// <summary>A grind session a player started with the button and (when stopped) ended.</summary>
public sealed class GauntletSessionRow
{
    public int Id { get; set; }
    public string Player { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
}
