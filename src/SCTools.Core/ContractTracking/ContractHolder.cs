using SCTools.Missions;

namespace SCTools.ContractTracking;

/// <summary>
/// A player who had a contract: Took is true when he took it, false when it was shared with him and he accepted the share, null when his log does not say
/// which (a gauntlet run whose "Contract Accepted" line was not read). Outcome and EndedUtc: how and when it ended for him (null: it did not end yet).
/// </summary>
public sealed record ContractHolder(string Player, bool? Took, MissionOutcome? Outcome, DateTime? EndedUtc)
{
    /// <summary>He still had it when it paid out: his log says it was completed.</summary>
    public bool Completed => Outcome == MissionOutcome.Complete;
}
