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
