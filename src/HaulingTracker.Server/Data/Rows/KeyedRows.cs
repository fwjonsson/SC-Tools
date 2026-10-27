namespace HaulingTracker.Server.Data;

/// <summary>A member who has (or has not) a mission, ticked on the Contracts tab (wins over what the logs say).</summary>
public sealed class MissionHolderRow
{
    public string MissionId { get; set; } = "";
    public string Member { get; set; } = "";
    public bool Has { get; set; }
}

/// <summary>What a mission really paid (typed in the Mission Payouts list), instead of the reward of its contract.</summary>
public sealed class PayoutOverrideRow
{
    public string MissionId { get; set; } = "";
    public decimal Payout { get; set; }
}

/// <summary>A mission marked as gone on the Contracts tab, and when.</summary>
public sealed class DroppedMissionRow
{
    public string MissionId { get; set; } = "";
    public DateTime Time { get; set; }
}

/// <summary>The SCU of a drop-off of a from-Ruin contract, typed when the game did not say ("mission id|objective id").</summary>
public sealed class TypedScuRow
{
    public string Objective { get; set; } = "";
    public int Scu { get; set; }
}

/// <summary>An amount typed on the Debts tab: "a|b" (a before b), what b owes a.</summary>
public sealed class PairAdjustRow
{
    public string Pair { get; set; } = "";
    public decimal Amount { get; set; }
}

/// <summary>Your own name for a location.</summary>
public sealed class LocationNameRow
{
    public string LocationId { get; set; } = "";
    public string Name { get; set; } = "";
}
