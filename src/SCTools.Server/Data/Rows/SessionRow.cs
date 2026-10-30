namespace SCTools.Server.Data;

/// <summary>
/// A saved session: the times, the payouts and the figures in their own columns, the details (the stops, the runs, the locks, who carried what) as JSON
/// columns. The same as SessionInputs of the data file.
/// </summary>
public sealed class SessionRow
{
    public string Key { get; set; } = "";
    public int Position { get; set; }

    public string? Start { get; set; }
    public string? End { get; set; }
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public int MissionCount { get; set; }
    public decimal Revenue { get; set; }

    public decimal Reward2 { get; set; }
    public decimal Reward3 { get; set; }
    public decimal Reward4 { get; set; }
    public decimal WasteReward2 { get; set; }
    public decimal WasteReward3 { get; set; }
    public decimal WasteReward4 { get; set; }
    public decimal DirectReward { get; set; }

    public bool NextStopsLocked { get; set; }
    public DateTime? WorkersResetAt { get; set; }

    // the details, as JSON
    public List<RewardRow> Rewards { get; set; } = new();
    public List<string> LostRuns { get; set; } = new();
    public Dictionary<string, string> LostCargo { get; set; } = new();
    public List<ManualEnd> ManualEnds { get; set; } = new();
    public Dictionary<string, RowInputs> Rows { get; set; } = new();
    public Dictionary<string, string> PickupClaims { get; set; } = new();
    public Dictionary<string, List<LockedStop>> LockedTrips { get; set; } = new();
    public Dictionary<string, List<LockedStop>> FreeLockedTrips { get; set; } = new();   // the kept stops of the planner without a hub (tried out beside the old one, 30 Oct 2026)
    public List<WorkerEra> PastWorkers { get; set; } = new();
    public Dictionary<string, decimal> Carried { get; set; } = new();
    public Dictionary<string, decimal> RunValues { get; set; } = new();
    public Dictionary<string, decimal> FailedValues { get; set; } = new();
    public List<string> MissionIds { get; set; } = new();
    public List<string>? Party { get; set; }
    public Dictionary<string, WorkerStat> Stats { get; set; } = new();
    public Dictionary<string, decimal> Received { get; set; } = new();

    /// <summary>The parties of the session as the payouts went (SessionInputs.Groups).</summary>
    public List<List<string>> Groups { get; set; } = new();
    public List<string> Members { get; set; } = new();
}
