namespace SCTools.Gauntlet;

/// <summary>How a gauntlet mission ended: only Complete earns anything.</summary>
public enum GauntletOutcome { Complete, Abandon, Fail }

/// <summary>
/// One gauntlet mission of one player, from the log. Accepted is the first "Contract Accepted" of the mission (a missions shared to the player has none).
/// </summary>
public sealed record GauntletRun(string MissionId, int Scenario, DateTime? AcceptedUtc, DateTime EndedUtc, GauntletOutcome Outcome)
{
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
