using SCTools.Missions;

namespace SCTools.ContractTracking;

/// <summary>
/// One contract as the contracts tracker knows it: its mission (two copies of the same contract taken separately are two missions), its kind in the Mission
/// Library and its name, everybody who had it (Holders: who took it, who accepted the share, and how it ended for each), what it pays (the Mission Library's
/// typed reward, else the Star Citizen Wiki's; the admin's payout override on the Debts tab wins), and the Hauling Planner's payout of it when the planner
/// paid it out (kept with its session, so it wins). The split of the money is ContractSplit's.
/// </summary>
public sealed record TrackedContract(string MissionId, string? Kind, string? Title, IReadOnlyList<ContractHolder> Holders, ContractReward Reward,
    PayoutRecord? Planner = null)
{
    /// <summary>The holder who is this player (any capitals), or null.</summary>
    public ContractHolder? HolderOf(string player) => Holders.FirstOrDefault(h => SameName(h.Player, player));

    /// <summary>
    /// The player who took it: the holder whose log says he accepted it; when no log says so and one player alone had it, he did (his log started after
    /// the accept). Null when nobody is known to have taken it.
    /// </summary>
    public string? Taker => Holders.FirstOrDefault(h => h.Took == true)?.Player
        ?? (Holders.Count == 1 && Holders[0].Took == null ? Holders[0].Player : null);

    /// <summary>Did this player take it (it was not shared with him)? Every holder whose log says he accepted it did.</summary>
    public bool TookIt(string player) =>
        HolderOf(player) is { } h && (h.Took == true || (h.Took == null && Taker is { } t && SameName(t, player)));

    /// <summary>
    /// Does this player get its MG and Council scrip? The game's rule (Frank, 30 Oct 2026): the one who took it, when he still had it when it paid out; when
    /// he abandoned it (or it failed for him), the one who accepted the share first of the ones who still had it. Nobody else, and nobody when it was
    /// not completed.
    /// </summary>
    public bool GetsScrip(string player)
    {
        if (HolderOf(player) is not { Completed: true } me) return false;
        if (TookIt(player)) return true;
        // the takers (whose log says they accepted it first): when one of them completed it, the scrip is his; when they all ended it without
        // completing it, it goes on to the share accepted first
        var takers = Holders.Where(h => h.Took == true).ToList();
        if (takers.Count == 0 || takers.Any(h => h.Completed || h.Outcome == null)) return false;
        var next = Holders.Where(h => h.Completed && h.AcceptedUtc != null).Select(h => h.AcceptedUtc).Min();
        return next != null && me.AcceptedUtc == next;
    }

    /// <summary>How it ended: completed when anybody completed it (it paid out), else how it ended for the first who ended it; null while it goes on.</summary>
    public MissionOutcome? Outcome =>
        Holders.Any(h => h.Completed) ? MissionOutcome.Complete : Holders.Where(h => h.Outcome != null).OrderBy(h => h.EndedUtc ?? DateTime.MaxValue).FirstOrDefault()?.Outcome;

    /// <summary>When it ended: when it paid out (the first completion), else when it ended for the last who had it.</summary>
    public DateTime? EndedUtc =>
        Holders.Any(h => h.Completed) ? Holders.Where(h => h.Completed).Min(h => h.EndedUtc) : Holders.Max(h => h.EndedUtc);

    /// <summary>Who is paid its aUEC (ContractSplit.Payees).</summary>
    public IReadOnlyList<string> Payees => ContractSplit.Payees(this);

    /// <summary>This player's part of what it pays (ContractSplit.PartOf), or null when he gets none of it.</summary>
    public ContractReward? PartOf(string player) => ContractSplit.PartOf(this, player);
}
