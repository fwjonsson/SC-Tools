namespace SCTools.Gauntlet;

/// <summary>How a gauntlet mission ended: only Complete earns anything.</summary>
public enum GauntletOutcome { Complete, Abandon, Fail }

/// <summary>
/// One gauntlet mission of one player, from the log. Accepted is the first "Contract Accepted" of the mission (a missions shared to the player has none).
/// Paid: his part of what it paid, from the contracts tracker (a scenario shared in the party: his share of the aUEC, and the scrip only when he took it);
/// null: the scenario's whole reward.
/// </summary>
public sealed record GauntletRun(string MissionId, int Scenario, DateTime? AcceptedUtc, DateTime EndedUtc, GauntletOutcome Outcome, GauntletReward? Paid = null)
{
    /// <summary>What this run earned him (nothing when it was not completed): his part, else the scenario's reward.</summary>
    public GauntletReward Earned(IReadOnlyDictionary<int, GauntletReward>? rewards) =>
        Outcome != GauntletOutcome.Complete ? default : Paid ?? GauntletRewards.Of(Scenario, rewards);

    /// <summary>Seconds from accepting to completing, or null (not completed, accepted by somebody else, or taking longer than counts: see GauntletRewards.MaxTimedSec).</summary>
    public double? TimingSec
    {
        get
        {
            if (Outcome != GauntletOutcome.Complete || AcceptedUtc == null) return null;
            var d = (EndedUtc - AcceptedUtc.Value).TotalSeconds;
            return d > 0 && d <= GauntletRewards.MaxTimedSec(Scenario) ? d : null;
        }
    }
}
