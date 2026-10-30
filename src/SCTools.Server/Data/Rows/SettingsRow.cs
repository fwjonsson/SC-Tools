namespace SCTools.Server.Data;

/// <summary>
/// The one row of settings: the times the payouts and the plan go by, and what only the program on a PC uses (kept so the data file comes back out of the
/// database the same as it went in). The database has its data once this row is there.
/// </summary>
public sealed class SettingsRow
{
    public const int OnlyId = 1;

    public int Id { get; set; } = OnlyId;

    /// <summary>Payouts of missions that ended before this time are split the old way (the whole session party).</summary>
    public DateTime? PartyTrackingSince { get; set; }

    /// <summary>From this time on the runs get their workers from the logs and the Trip Planner.</summary>
    public DateTime? PlanCreditSince { get; set; }

    /// <summary>From this time on the logs decide who of the party is active (the in-game party and the trackers).</summary>
    public DateTime? AutoPartySince { get; set; }

    /// <summary>The mission rewards typed last, by contract line ("2 To Ruin"): a new session starts with them.</summary>
    public Dictionary<string, decimal> DefaultRewards { get; set; } = new();

    // the program on a PC
    public string? GameLogPath { get; set; }
    public bool DarkMode { get; set; }
    public string? Channel { get; set; }
    public bool WebView { get; set; }
    public int WebViewPort { get; set; }
    public List<string> WatchPaths { get; set; } = new();
    public List<string> DisabledWatchPaths { get; set; } = new();
    public List<List<string>> CombinedAccounts { get; set; } = new();   // one player's accounts added up in the money tables (30 Oct 2026)
}
