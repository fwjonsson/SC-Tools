using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.Tracking;

/// <summary>What the contracts pay, and what stops and runs are worth.</summary>
public class EarningsTests
{
    [Fact]
    public void A_contract_pays_what_was_typed_or_else_the_built_in_reward()
    {
        var inp = new SessionInputs();
        Assert.Equal(Earnings.DefaultReward3, Earnings.RewardOf(new Mission { Multi = 3 }, inp));
        inp.Reward3 = 1_700_000m;
        Assert.Equal(1_700_000m, Earnings.RewardOf(new Mission { Multi = 3 }, inp));
        Assert.Equal(0m, Earnings.RewardOf(new Mission { Multi = 5 }, inp));
        // a from-Ruin contract pays the same as a to-Ruin one with as many stops
        Assert.Equal(Earnings.DefaultReward2, Earnings.WasteRewardOf(new Mission { OutDrops = 2 }, inp));
        Assert.Equal(0m, Earnings.WasteRewardOf(new Mission { OutDrops = 2 }, null));
        Assert.Equal(795_750m, Earnings.WasteRewardOf(new Mission { OutDrops = 1 }, inp));   // a direct one
        inp.DirectReward = 800_000m;
        Assert.Equal(800_000m, Earnings.WasteRewardOf(new Mission { OutDrops = 1 }, inp));
    }

    [Fact]
    public void Nothing_typed_is_filled_in_with_the_built_in_payouts()
    {
        var inp = new SessionInputs { Reward2 = 1000m };
        Earnings.FillDefaults(inp);
        Assert.Equal(1000m, inp.Reward2);   // what was typed stays
        Assert.Equal(Earnings.DefaultReward3, inp.Reward3);
        Assert.Equal(Earnings.DefaultReward4, inp.Reward4);
        Assert.Equal(Earnings.DefaultReward2, inp.WasteReward2);
        Assert.Equal(Earnings.DefaultReward4, inp.WasteReward4);
        Assert.Equal(Earnings.DefaultDirectReward, inp.DirectReward);
    }

    [Fact]
    public void The_rewards_table_has_a_row_for_every_kind_of_contract()
    {
        Assert.Equal(new[] { "2 To Ruin", "3 To Ruin", "4 To Ruin", "2 From Ruin", "3 From Ruin", "4 From Ruin", "Direct From Ruin" }, Earnings.RewardLines.Select(l => l.Contract));
        var accepted = new[] { new Mission { Multi = 2 }, new Mission { Multi = 3 }, new Mission { Multi = 3 } };
        var waste = new[] { new Mission { OutDrops = 3 }, new Mission { OutDrops = 1 }, new Mission { OutDrops = 1 } };
        Assert.Equal(new[] { 1, 2, 0, 0, 1, 0, 2 }, Earnings.RewardLines.Select(l => l.CountIn(accepted, waste)));
        var inp = new SessionInputs();
        Earnings.RewardLines[4].Set(inp, 123m);
        Assert.Equal(123m, inp.WasteReward3);
    }

    [Fact]
    public void A_stop_is_worth_the_reward_of_each_mission_split_over_its_stops()
    {
        var (t, _) = PlayedSession.Load();
        var earnings = t.Earnings;
        var row = new LocationRow { Stops2 = 1, Stops3 = 2 };
        Assert.Equal(Earnings.DefaultReward2 / 2 + 2 * Earnings.DefaultReward3 / 3, earnings.StopValue(row));
        Assert.Equal("", earnings.StopValueText(new LocationRow()));
    }

    [Fact]
    public void A_stop_with_waste_to_bring_there_is_worth_that_too_and_moves_up_the_board()
    {
        var w = GameLogWriter.MissionId(61);
        var waste = string.Format(GameLogWriter.WasteContract, 2);
        var (t, _) = PlayedSession.Load(more: x => x.Minutes(1).Accepted(w)
            .Marker(w, waste, "dropoff_w_0", "900000006101", GameLogWriter.PatchCity)    // where a batch is still waiting to be picked up
            .Marker(w, waste, "dropoff_w_1", "900000006102", GameLogWriter.RodsFuel));   // nothing to pick up there
        var board = t.Earnings.StopsBoard(t.DropOffs.OpenDrops());

        var patch = board.Single(s => s.Zone == PlayedSession.PatchZone);   // the drop-off goes with the stop at the same station
        Assert.Single(patch.Drops);
        Assert.Equal(t.Earnings.StopValue(patch.Row!) + Earnings.DefaultReward2 / 2, patch.Value);   // its pickups and its part of the waste contract
        Assert.Same(patch, board[0]);
        var rods = board.Single(s => s.Row == null);   // only a drop-off: a stop of its own
        Assert.Equal("900000006102", rods.Zone);
        Assert.Equal(Earnings.DefaultReward2 / 2, rods.Value);
        Assert.Equal(board.Select(s => s.Value).OrderByDescending(v => v), board.Select(s => s.Value));   // the most valuable stop on top

        var (plain, _) = PlayedSession.Load();   // no waste: the pickups alone
        Assert.All(plain.Earnings.StopsBoard(plain.DropOffs.OpenDrops()), s =>
        {
            Assert.Empty(s.Drops);
            Assert.Equal(plain.Earnings.StopValue(s.Row!), s.Value);
        });
    }

    [Fact]
    public void A_run_is_worth_its_batches_and_the_part_of_completed_missions_is_earned()
    {
        var (t, _) = PlayedSession.Load();
        var (done, onTheWay) = PlayedSession.Runs(t);
        var g = t.State.Current!;
        var inp = t.State.Inputs!;

        var first = Earnings.RunValues(done, g, inp);
        Assert.Equal(Earnings.DefaultReward2, first.Total);       // both stops of the first mission
        Assert.Equal(Earnings.DefaultReward2, first.Confirmed);   // and the mission is completed
        Assert.Equal(0m, first.Failed);

        var second = Earnings.RunValues(onTheWay, g, inp);
        Assert.Equal(Earnings.DefaultReward2 / 2, second.Total);  // one stop of the second mission so far
        Assert.Equal(0m, second.Confirmed);
        Assert.Equal(Earnings.DefaultReward2 / 2, t.Earnings.RunValue(onTheWay));
        Assert.Equal(Earnings.DefaultReward2 / 2, t.Earnings.PickupValue(Assert.Single(onTheWay.Pickups)));
    }

    [Fact]
    public void A_run_that_lost_all_its_cargo_is_worth_nothing_earned()
    {
        var (t, _) = PlayedSession.Load();
        var (done, _) = PlayedSession.Runs(t);
        t.State.Inputs!.LostCargo[done.Key] = "All";
        var v = Earnings.RunValues(done, t.State.Current!, t.State.Inputs);
        Assert.Equal(v.Total, v.Failed);
        Assert.Equal(0m, v.Confirmed);
    }

    [Fact]
    public void The_session_has_earned_what_its_completed_missions_paid()
    {
        var (t, _) = PlayedSession.Load();
        Assert.Equal(Earnings.DefaultReward2, t.Earnings.Earned());
        Assert.Equal(2 * Earnings.DefaultReward2, t.Earnings.Revenue(t.State.Current!, t.State.Inputs!));   // both contracts together
    }
}
