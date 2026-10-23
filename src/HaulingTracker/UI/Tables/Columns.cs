namespace HaulingTracker.UI.Tables;

// The names of the columns of the tables. The code finds a cell by what is in it (row.Cells[StopCol.Value]), never by where its column is,
// so a column can be moved, added or hidden without anything else changing.

/// <summary>Columns that several tables have.</summary>
static class GridCol
{
    /// <summary>The "#" column at the far left: the row number (NumberRows fills it), or the number of a run. It keeps its small width.</summary>
    public const string Number = "num";
}

/// <summary>The Stops board of the Session tab: one row per pickup location with batches still to collect (and stations with only drop-offs).</summary>
static class StopCol
{
    public const string Location = "location";
    public const string FromRuin = "fromRuin";     // Gm from Ruin Station
    public const string Batches = "batches";       // missions still waiting there / missions with cargo there
    public const string Worker = "worker";         // Assigned Worker (hidden: the runs get their workers from the logs and the Trip Planner)
    public const string Value = "value";
    public const string Notes = "notes";
    public const string DropOff = "dropOff";       // cargo to bring out from Ruin to that station
}

/// <summary>The Finished Stops board of the Session tab.</summary>
static class FinishedCol
{
    public const string Location = "location";
    public const string FromRuin = "fromRuin";
    public const string Delivered = "delivered";
    public const string Worker = "worker";
    public const string Value = "value";
    public const string HandedIn = "handedIn";
}

/// <summary>Cargo Runs: the board on the Session tab, its short copy under the map of the Trip Planner and the window with every run. They all have these cells.</summary>
static class RunCol
{
    public const string Worker = "worker";
    public const string Number = GridCol.Number;   // the number of the run
    public const string Places = "places";         // Pickup Location & Time (the short copy: Locations)
    public const string Batches = "batches";
    public const string Value = "value";
    public const string Started = "started";
    public const string Completed = "completed";
    public const string Time = "time";             // how long the run took (counts up while it is on the way)
    public const string Flight = "flight";         // the estimated flying time of the run
    public const string Lost = "lost";             // Lost Cargo: None, Little, All
    public const string PerHour = "perHour";       // aUEC/h
}

/// <summary>The Contracts table on the Session tab (the payout of every kind of contract and how many there are).</summary>
static class RewardCol
{
    public const string Contract = "contract";
    public const string Reward = "reward";
    public const string Count = "count";
    public const string Total = "total";
}

/// <summary>The leaderboards: the session one on the Session tab and the all-time one on the History tab (only that one has Sessions).</summary>
static class StatCol
{
    public const string Worker = "worker";
    public const string Sessions = "sessions";
    public const string Runs = "runs";
    public const string Perfect = "perfect";
    public const string Delivered = "delivered";
    public const string AvgBatches = "avgBatches";
    public const string AvgTime = "avgTime";
    public const string Distance = "distance";
    public const string GmPerHour = "gmPerHour";
    public const string RunValue = "runValue";
    public const string Earned = "earned";
    public const string Lost = "lost";
    public const string Share = "share";
    public const string ValuePerHour = "valuePerHour";
}

/// <summary>The Party table on the Session tab.</summary>
static class PartyCol
{
    public const string Member = "member";
    public const string Active = "active";
    public const string Batches = "batches";       // Batch Capacity of his ship
    public const string Ship = "ship";
    public const string Drive = "drive";           // QT Drive
    public const string Color = "color";           // click it to pick his colour
}

/// <summary>Balances on the Debts tab.</summary>
static class BalanceCol
{
    public const string Member = "member";
    public const string Balance = "balance";
    public const string Meaning = "meaning";
}

/// <summary>Earnings on the Debts tab.</summary>
static class EarnCol
{
    public const string Worker = "worker";
    public const string Session = "session";
    public const string All = "all";
    public const string Net = "net";
}

/// <summary>Money Transfers on the Debts tab.</summary>
static class TransferCol
{
    public const string Time = "time";
    public const string From = "from";
    public const string To = "to";
    public const string Amount = "amount";
    public const string Payment = "payment";       // ticked: the transfer pays off a debt
}

/// <summary>Mission Payouts on the Debts tab.</summary>
static class PayoutCol
{
    public const string Time = "time";
    public const string Mission = "mission";
    public const string Payout = "payout";         // the only cell you type in: what the mission really paid
    public const string Members = "members";       // Shared Between
    public const string Each = "each";
}

/// <summary>Saved Sessions on the History tab.</summary>
static class HistoryCol
{
    public const string Date = "date";
    public const string Time = "time";
    public const string Missions = "missions";
    public const string Revenue = "revenue";
    public const string Carried = "carried";
}

/// <summary>The Contracts tab: the contract, then one column for every party member, then Gone.</summary>
static class ContractCol
{
    public const string Contract = "contract";
    public const string Cargo = "cargo";
    public const string Stops = "stops";
    public const string Gone = "gone";

    const string MemberPrefix = "m:";

    /// <summary>The column of a party member (his box: has the mission or not).</summary>
    public static string Member(string name) => MemberPrefix + name;

    /// <summary>The party member of a column, or null when it is not a member column.</summary>
    public static string? MemberOf(string column) => column.StartsWith(MemberPrefix, StringComparison.Ordinal) ? column[MemberPrefix.Length..] : null;
}

/// <summary>The Live Sources dialog.</summary>
static class SourceCol
{
    public const string Enabled = "enabled";
    public const string Source = "source";
    public const string Reads = "reads";           // the Game.log it reads now, or why it cannot
}

/// <summary>
/// The two tables of every worker in the Trip Planner. Current Route: #, Stops, Deliver/Collect, Cargo, Value, Distance, In flight. Queued routes: #, Route, Stops,
/// Batches, Value, Distance, In flight. Their rows are added as whole lines of cells in this order (LineCells, QueuedCells and the totals give them so, for the web page too).
/// </summary>
static class PlanCol
{
    public const string Number = GridCol.Number;
    public const string Stop = "stop";             // Current Route: the station
    public const string Action = "action";         // Current Route: what is done there (Deliver/Collect)
    public const string Cargo = "cargo";           // Current Route: the batches and loads
    public const string Route = "route";           // Queued routes: the stations of the trip
    public const string Stops = "stops";           // Queued routes: how many stops
    public const string Batches = "batches";       // Queued routes
    public const string Value = "value";
    public const string Distance = "distance";
    public const string Flight = "flight";         // In flight
}
