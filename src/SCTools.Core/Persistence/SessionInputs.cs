using System.Text.Json;

namespace SCTools.Persistence;

/// <summary>What you typed yourself for one session.</summary>
public sealed class SessionInputs
{
    public string? Start { get; set; }     // null = use the time from the log
    public string? End { get; set; }       // null = use the time from the log
    public List<RewardRow> Rewards { get; set; } = new();   // old format, no longer used
    public decimal Reward2 { get; set; }   // payout of a 2 stop contract
    public decimal Reward3 { get; set; }   // payout of a 3 stop contract
    public decimal Reward4 { get; set; }   // payout of a 4 stop contract (Master rank)
    public decimal WasteReward2 { get; set; }   // payout of a from-Ruin (waste) contract with 2 drop-offs
    public decimal WasteReward3 { get; set; }   // ... with 3 drop-offs
    public decimal WasteReward4 { get; set; }   // ... with 4 drop-offs
    public decimal DirectReward { get; set; }   // payout of a direct from-Ruin contract (one drop-off: "DIRECT Stellar Large Haul")
    public List<string> LostRuns { get; set; } = new();   // old format: runs ticked as "lost cargo" (counts as All)
    public Dictionary<string, string> LostCargo { get; set; } = new();   // run key -> Little or All
    public List<ManualEnd> ManualEnds { get; set; } = new();
    public Dictionary<string, RowInputs> Rows { get; set; } = new();   // key = pickup location id
    public Dictionary<string, string> PickupClaims { get; set; } = new();   // "mission|objective" -> the worker the Trip Planner credited a pickup to (only where no log names who did it)
    public bool NextStopsLocked { get; set; }   // the "Next Stops: Locked" button of the Trip Planner: only while it is on is the next stop of everybody kept for him (off = the plan is free)
    public Dictionary<string, List<LockedStop>> LockedTrips { get; set; } = new();   // worker -> his Current trip: the Trip Planner leaves it as it is (set by Assign Workers to Stops or by assigning stops by hand)
    public List<WorkerEra> PastWorkers { get; set; } = new();   // assigned workers of earlier sets of missions (frozen when the table was reset)
    public DateTime? WorkersResetAt { get; set; }               // when the last set of missions began: the assigned workers were cleared then
    public Dictionary<string, decimal> Carried { get; set; } = new();   // worker -> value of the locations he carried (saved for the Debts tab)
    public Dictionary<string, decimal> RunValues { get; set; } = new();   // worker -> value of his finished runs (Carried = the part whose missions are completed)
    public Dictionary<string, decimal> FailedValues { get; set; } = new();   // worker -> part of the run value that is known to be lost (failed missions, runs marked as failed)
    public List<string> MissionIds { get; set; } = new();
    public List<string>? Party { get; set; }   // who was in the party this session (null = everyone who carried something), set with "Session party…"
    public DateTime? StartUtc { get; set; }   // saved with the session so the History tab works without the logs
    public DateTime? EndUtc { get; set; }
    public int MissionCount { get; set; }
    public decimal Revenue { get; set; }
    public Dictionary<string, WorkerStat> Stats { get; set; } = new();   // worker -> runs and missions of the session (for the History leaderboard)
    public Dictionary<string, decimal> Received { get; set; } = new();   // worker -> his share of the mission payouts (split between the party present when each mission paid out)
    public List<string> Members { get; set; } = new();   // the party of the session as used for the split (saved with Carried)   // missions of the session, to avoid counting a session twice
}
