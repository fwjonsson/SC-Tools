using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>What every session earned per worker, kept for the Debts and History tabs; the money transfers; your corrections of the balances.</summary>
public class PayoutsTests
{
    [Fact]
    public void A_correction_of_a_balance_is_kept_for_the_pair_from_both_sides()
    {
        var data = new AppData();
        var payouts = new Tracker(data, _ => { }).Payouts;
        payouts.SetAdj("Pilot_A", "Pilot_B", 100);
        Assert.Equal(100m, payouts.GetAdj("Pilot_A", "Pilot_B"));
        Assert.Equal(-100m, payouts.GetAdj("pilot_b", "PILOT_A"));
        payouts.SetAdj("Pilot_B", "Pilot_A", 0.2m);   // as good as even: nothing to keep
        Assert.Equal(0m, payouts.GetAdj("Pilot_A", "Pilot_B"));
        Assert.Empty(data.PairAdjust);
    }

    [Fact]
    public void A_balance_is_said_in_words()
    {
        Assert.Equal("Pilot_B owes Pilot_A 1,250", Payouts.Meaning("Pilot_A", "Pilot_B", 1250));
        Assert.Equal("Pilot_A owes Pilot_B 300", Payouts.Meaning("Pilot_A", "Pilot_B", -300));
        Assert.Equal("even", Payouts.Meaning("Pilot_A", "Pilot_B", 0.3m));
    }

    [Fact]
    public void The_snapshot_keeps_what_the_session_earned_and_who_received_the_payout()
    {
        var (t, saves) = PlayedSession.Load();
        var key = t.State.Current!.Key;
        t.Payouts.UpdateSnapshots();

        var kept = t.State.Data.Sessions[key];
        Assert.Equal(PlayedSession.Player, Assert.Single(Assert.Single(kept.Groups)), StringComparer.OrdinalIgnoreCase);   // one party: the one who carried and shared it
        Assert.Equal(Earnings.DefaultReward2, kept.Carried[PlayedSession.Player]);     // the finished run, its mission completed
        Assert.Equal(Earnings.DefaultReward2, kept.Received[PlayedSession.Player]);    // the payout of that mission, he was the party
        Assert.Equal(Earnings.DefaultReward2, kept.RunValues[PlayedSession.Player]);
        Assert.Equal(2, kept.MissionCount);
        Assert.Equal(2 * Earnings.DefaultReward2, kept.Revenue);
        Assert.Equal(1, kept.Stats[PlayedSession.Player].Runs);
        var payout = Assert.Single(t.State.Data.Payouts);
        Assert.Equal(PlayedSession.M1, payout.MissionId);
        Assert.Equal(new[] { PlayedSession.Player }, payout.Members);
        Assert.Equal(Earnings.DefaultReward2, payout.Each);

        var after = saves.Count;
        Assert.True(after > 0);
        t.Payouts.UpdateSnapshots();   // nothing changed: not saved again
        Assert.Equal(after, saves.Count);
    }

    [Fact]
    public void A_payout_typed_by_hand_is_what_the_mission_paid()
    {
        var (t, _) = PlayedSession.Load();
        t.Payouts.SetPayoutOverride(PlayedSession.M1, 1_000_000m);
        var (carried, received) = t.Payouts.ComputePayouts(t.State.Current!, t.State.Inputs!);
        Assert.Equal(1_000_000m, Math.Round(carried[PlayedSession.Player], 2));   // spread over the batches that were carried (to the last decimal)
        Assert.Equal(1_000_000m, Math.Round(received[PlayedSession.Player], 2));
        t.Payouts.SetPayoutOverride(PlayedSession.M1, 0);   // empty: the reward of the contract again
        Assert.Empty(t.State.Data.PayoutOverrides);
    }

    [Fact]
    public void A_money_transfer_in_the_logs_is_kept_once()
    {
        var data = PlayedSession.Data();
        var (t, _) = PlayedSession.Load(data, w => w.Minutes(2).Sent("Pilot_B", 50_000m));
        t.Payouts.UpdateSnapshots();
        t.Payouts.UpdateSnapshots();
        var kept = Assert.Single(data.Transfers);
        Assert.Equal((PlayedSession.Player, "Pilot_B", 50_000m), (kept.From, kept.To, kept.Amount));
        Assert.False(kept.Payment);   // you tick it when it pays off a debt
    }

    [Fact]
    public void The_debts_tab_lists_the_party_and_everybody_who_carried()
    {
        var data = new AppData { Party = new List<string> { "Pilot_A" } };
        data.Sessions["s"] = new SessionInputs { Carried = new() { ["Pilot_C"] = 10, ["pilot_a"] = 5 } };
        Assert.Equal(new[] { "Pilot_A", "Pilot_C" }, new Tracker(data, _ => { }).Payouts.DebtPartyNames());
    }
}
