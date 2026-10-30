using SCTools.Tests.Support;

namespace SCTools.Tests.ContractTracking;

/// <summary>
/// Who the Hauling Planner pays a completed contract to (the Debts tab's Mission payouts), by Frank's rule of 30 Oct 2026: everybody who has the contract when
/// it pays out (the one who took it and everybody who accepted the share), not who is in the party. Pilot_A took M1 of PlayedSession and completed it;
/// Pilot_C plays with a tracker too.
/// </summary>
public class PlannerSplitTests
{
    /// <summary>The session with Pilot_C's log beside Pilot_A's: he accepted M1 three minutes after Pilot_A took it (shared), or never had it.</summary>
    static Tracker Load(AppData data, bool cHasIt)
    {
        var c = new GameLogWriter().Login("Pilot_C").Minutes(2);
        if (cHasIt) c.Minutes(1).Accepted(PlayedSession.M1);
        var t = new Tracker(data, _ => { });
        t.Load(new[] { PlayedSession.Writer().Read(), c.Read() });
        t.Select(t.State.Sessions[^1]);
        t.LoadStops();
        t.LoadRuns();
        return t;
    }

    static Dictionary<string, decimal> Received(Tracker t) => t.Payouts.ComputePayouts(t.State.Current!, t.State.Inputs!).Received;

    static AppData Party(params string[] names)
    {
        var data = PlayedSession.Data();
        data.Party = names.ToList();
        return data;
    }

    [Fact]
    public void A_shared_contract_is_split_between_who_has_it()
    {
        var received = Received(Load(Party(PlayedSession.Player, "Pilot_C"), cHasIt: true));
        Assert.Equal(Earnings.DefaultReward2 / 2, received[PlayedSession.Player]);
        Assert.Equal(Earnings.DefaultReward2 / 2, received["Pilot_C"]);
    }

    [Fact]
    public void A_party_member_who_never_accepted_the_share_gets_nothing()
    {
        var received = Received(Load(Party(PlayedSession.Player, "Pilot_C"), cHasIt: false));
        Assert.Equal(Earnings.DefaultReward2, received[PlayedSession.Player]);
        Assert.False(received.ContainsKey("Pilot_C"));

        // also when the one who took it is not in the party list: before the contracts tracker the party shared it then (Pilot_C got all of it)
        received = Received(Load(Party("Pilot_C"), cHasIt: false));
        Assert.Equal(Earnings.DefaultReward2, received[PlayedSession.Player]);
        Assert.False(received.ContainsKey("Pilot_C"));
    }

    [Fact]
    public void Who_still_had_it_when_it_paid_out_gets_his_share_also_when_he_was_out_of_the_party()
    {
        // Pilot_C was marked out of the party 30 minutes in, before M1 paid out, and his log still has the contract: before the contracts tracker he got nothing
        var data = Party(PlayedSession.Player, "Pilot_C");
        data.PartyChanges.Add(new PartyChange { Name = "Pilot_C", TimeUtc = GameLogWriter.Start.AddMinutes(30), Active = false });
        var received = Received(Load(data, cHasIt: true));
        Assert.Equal(Earnings.DefaultReward2 / 2, received[PlayedSession.Player]);
        Assert.Equal(Earnings.DefaultReward2 / 2, received["Pilot_C"]);
    }

    [Fact]
    public void A_payout_already_kept_with_a_session_keeps_its_members()
    {
        // the same evening, saved before the contracts tracker: Pilot_A alone was paid M1, and that stays (debts between friends do not change afterwards)
        var data = Party(PlayedSession.Player, "Pilot_C");
        data.PartyChanges.Add(new PartyChange { Name = "Pilot_C", TimeUtc = GameLogWriter.Start.AddMinutes(30), Active = false });
        data.Payouts.Add(new PayoutRecord { MissionId = PlayedSession.M1, Members = { PlayedSession.Player }, Each = Earnings.DefaultReward2, Payout = Earnings.DefaultReward2 });
        var received = Received(Load(data, cHasIt: true));
        Assert.Equal(Earnings.DefaultReward2, received[PlayedSession.Player]);
        Assert.False(received.ContainsKey("Pilot_C"));
    }
}
