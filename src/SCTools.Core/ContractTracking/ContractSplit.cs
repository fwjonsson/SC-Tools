namespace SCTools.ContractTracking;

/// <summary>
/// How a contract's money is split between the players (Frank's rule of 30 Oct 2026), the one place every tool gets it from:
/// <list type="bullet">
/// <item>the aUEC is split evenly among the players who have that same contract when it pays out: the one who took it and everybody who accepted the share
/// and still had it. Being in the party without accepting the share gets nothing. Two copies of the same contract taken separately are two contracts (two
/// missions), each paying its own player in full;</item>
/// <item>the MG scrip and the Council scrip go only to the player who took it; when he abandoned it, to the one who accepted the share first of the
/// ones who still had it when it paid out (TrackedContract.GetsScrip);</item>
/// <item>a contract the Hauling Planner paid out keeps the planner's split (its payout record, kept with the session: who it paid and how much each; the
/// admin's payout override on the Debts tab is in it), so a past session's debts never change afterwards.</item>
/// </list>
/// A player's combined accounts are added up by the tools that show his totals, not here.
/// </summary>
public static class ContractSplit
{
    /// <summary>Who is paid the contract's aUEC: the members of the planner's payout when it paid it out, else every holder who completed it.</summary>
    public static IReadOnlyList<string> Payees(TrackedContract c) =>
        c.Planner != null
            ? c.Planner.Members
            : c.Holders.Where(h => h.Completed).Select(h => h.Player).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// This player's part of what the contract pays (whole: what it pays, by default its Reward), or null when he gets none of it: he did not complete it
    /// and the planner did not pay him a share. The aUEC is his even share (the planner's Each for a contract it paid out: 0 when he had it but was not
    /// paid); the scrip is all his when he gets it (TrackedContract.GetsScrip: he took it, or the taker abandoned it and his share was accepted first),
    /// else none (0 when its value is known, so a known reward is not "not known"). A planner payout he had without the contract in his own log has no
    /// scrip (null).
    /// </summary>
    public static ContractReward? PartOf(TrackedContract c, string player, ContractReward? whole = null)
    {
        whole ??= c.Reward;
        var holder = c.HolderOf(player);
        var completed = holder?.Completed == true;
        long? auec;
        if (c.Planner != null)
        {
            var member = c.Planner.Members.Any(n => SameName(n, player));
            if (!member && !completed) return null;
            auec = member ? (long)Math.Round(c.Planner.Each) : 0;
        }
        else
        {
            if (!completed) return null;
            auec = whole.Auec == null ? null : Even(whole.Auec.Value, Payees(c).Count);
        }
        if (!completed) return new ContractReward(auec, null, null);
        var gets = c.GetsScrip(player);
        long? Scrip(long? v) => gets || v == null ? v : 0;
        return new ContractReward(auec, Scrip(whole.Mg), Scrip(whole.Council));
    }

    /// <summary>One even share of an amount among this many players (at least one), to the whole aUEC.</summary>
    public static long Even(long amount, int players) => (long)Math.Round((double)amount / Math.Max(1, players));

    /// <summary>
    /// The players the Hauling Planner pays a completed contract to, by the same rule: the ones of these candidates (the party, and everybody whose log has it)
    /// who have it when it pays out (has: what the logs say, or what was ticked on the Contracts tab), in their order. When nobody is known to have it: the
    /// one who took it, else the ones who carried it.
    /// </summary>
    public static List<string> PlannerPayees(IEnumerable<string> candidates, Func<string, bool> has, string? taker, IEnumerable<string> carriers)
    {
        var payees = new List<string>();
        foreach (var n in candidates)
        {
            if (!payees.Any(p => SameName(p, n)) && has(n)) payees.Add(n);
        }
        if (payees.Count > 0) return payees;
        return taker != null ? new List<string> { taker } : carriers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
