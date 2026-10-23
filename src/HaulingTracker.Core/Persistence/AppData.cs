using System.Text.Json;

namespace HaulingTracker.Persistence;

public sealed class AppData
{
    public string? GameLogPath { get; set; }
    public List<string> Party { get; set; } = new() { "zzoega", "zzatvo", "bildog123" };
    public List<PartyChange> PartyChanges { get; set; } = new();   // when each member joined or left (Active / inactive)
    public DateTime? PartyTrackingSince { get; set; }   // payouts of missions that ended before this time are split the old way (the whole session party)
    public List<string> InactiveParty { get; set; } = new();   // party members that are not shown in the party list of the Session tab
    public DateTime? PlanCreditSince { get; set; }   // from this time on the runs get their workers from the logs and the Trip Planner, not from Assigned Worker (earlier pickups keep the old rule)
    public bool DarkMode { get; set; }   // the dark look
    public string? Channel { get; set; }   // Auto, LIVE, HOTFIX...
    public List<ManualSession> ManualSessions { get; set; } = new();   // started and stopped with the Start Session / Stop Session buttons
    public bool WebView { get; set; }      // the read-only live web view is switched on
    public int WebViewPort { get; set; } = 8765;   // the port of the live web view (on this computer only)
    public Dictionary<string, string> Ship { get; set; } = new();   // worker -> his ship (Ships name, like "C2 Hercules Starlifter"), for the SCU his ship holds
    public Dictionary<string, string> QuantumDrive { get; set; } = new();   // worker -> the quantum drive in his ship (QuantumDrives key, like "Eos S1"), for the travel time estimates
    public Dictionary<string, int> WorkerColors { get; set; } = new();   // worker -> colour (ARGB) on the Trip Planner Overview, picked in the Party table
    public Dictionary<string, int> ShipCapacity { get; set; } = new();   // worker -> batches his ship holds (the Trip Planner), 0 = not flying
    public List<string> WatchPaths { get; set; } = new();   // Game.log files to watch live, also on other computers
    public List<string> DisabledWatchPaths { get; set; } = new();   // sources that are saved but switched off (unticked in Sources)
    public Dictionary<string, string> LocationNames { get; set; } = new();   // location id -> your name
    public Dictionary<string, SessionInputs> Sessions { get; set; } = new();
    public List<SavedTransfer> Transfers { get; set; } = new();
    public List<PayoutRecord> Payouts { get; set; } = new();   // the completed missions and who shared their payout
    public Dictionary<string, Dictionary<string, bool>> MissionHolders { get; set; } = new();   // mission id -> member -> has the mission (ticked on the Contracts tab, wins over what the logs say)
    public Dictionary<string, decimal> PayoutOverrides { get; set; } = new();   // mission id -> what it really paid (typed in the Mission Payouts list), instead of the reward of its contract
    public Dictionary<string, DateTime> DroppedMissions { get; set; } = new();   // mission id -> when it was marked as gone on the Contracts tab (treated like an abandoned mission)
    public Dictionary<string, StartPoint> StartPoints { get; set; } = new();   // worker -> his Current location, when set by hand (otherwise the logs know)
    public Dictionary<string, int> TypedScu { get; set; } = new();   // "mission id|objective id" -> the SCU of that drop-off of a from-Ruin contract, typed by you when the game did not say (its own number wins once it does)
    public Dictionary<string, decimal> PairAdjust { get; set; } = new();   // "a|b" (a before b) -> amount b owes a, typed by you
}
