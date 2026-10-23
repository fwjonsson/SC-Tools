using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.Tracking;

/// <summary>Who of the party has which contract (the logs, or a box clicked on the Contracts tab), and the contracts marked as gone.</summary>
public class ContractsTests
{
    static AppData TwoMembers()
    {
        var data = PlayedSession.Data();
        data.Party.Add("Pilot_B");   // he has no log of his own
        return data;
    }

    [Fact]
    public void A_member_with_a_log_has_the_missions_his_log_holds_and_one_without_is_assumed_to_have_them()
    {
        var (t, _) = PlayedSession.Load(TwoMembers());
        var m2 = t.State.Log!.Missions[PlayedSession.M2];
        Assert.True(t.Contracts.HasMission(m2, PlayedSession.Player));
        Assert.True(t.Contracts.HasMission(m2, "Pilot_B"));   // nobody can read his game: he has it while he is in the party
        t.State.Data.InactiveParty.Add("Pilot_B");
        Assert.False(t.Contracts.HasMission(m2, "Pilot_B"));
    }

    [Fact]
    public void A_clicked_box_wins_over_the_logs_until_it_says_the_same()
    {
        var (t, _) = PlayedSession.Load(TwoMembers());
        var m2 = t.State.Log!.Missions[PlayedSession.M2];

        t.Contracts.SetHolder(m2, PlayedSession.Player, false);
        Assert.False(t.Contracts.HasMission(m2, PlayedSession.Player));
        Assert.True(t.Contracts.LogSaysHas(m2, PlayedSession.Player));
        Assert.True(t.State.Data.MissionHolders.ContainsKey(m2.Id));

        t.Contracts.SetHolder(m2, PlayedSession.Player, true);   // the same as the logs: nothing to remember
        Assert.True(t.Contracts.HasMission(m2, PlayedSession.Player));
        Assert.False(t.State.Data.MissionHolders.ContainsKey(m2.Id));
    }

    [Fact]
    public void A_mission_marked_as_gone_is_dropped_everywhere_until_it_is_brought_back()
    {
        var (t, _) = PlayedSession.Load();
        var logs = new[] { PlayedSession.Writer().Read() };
        t.Contracts.ToggleGone(t.State.Log!.Missions[PlayedSession.M2]);
        Assert.True(t.State.Data.DroppedMissions.ContainsKey(PlayedSession.M2));

        t.Load(logs);   // the logs are read again: the mission counts as abandoned
        var m2 = t.State.Log!.Missions[PlayedSession.M2];
        Assert.True(m2.Dropped);
        Assert.Equal("DROPPED", m2.EndState);
        Assert.DoesNotContain(t.State.Sessions[^1].Missions, m => m.Id == PlayedSession.M2);

        t.Contracts.ToggleGone(m2);   // Bring back
        Assert.False(t.State.Data.DroppedMissions.ContainsKey(PlayedSession.M2));
        t.Load(logs);
        Assert.Null(t.State.Log!.Missions[PlayedSession.M2].EndState);
    }

    [Fact]
    public void The_contracts_that_are_going_and_their_stops()
    {
        var (t, _) = PlayedSession.Load();
        var going = Assert.Single(t.Contracts.ActiveMissions());
        Assert.Equal(PlayedSession.M2, going.Id);
        Assert.Equal("Gaslight ✓,  Patch City", t.Contracts.ContractStopsText(going));   // ✓ = picked up
    }

    [Fact]
    public void A_contract_is_named_like_its_row_in_the_rewards_table()
    {
        Assert.Equal("3 To Ruin", Contracts.ContractName(new Mission { Multi = 3 }));
        Assert.Equal("2 From Ruin", Contracts.ContractName(new Mission { OutDrops = 2 }));
        Assert.Equal("Direct From Ruin", Contracts.ContractName(new Mission { OutDrops = 1 }));
        Assert.Equal("?", Contracts.ContractName(new Mission()));
    }
}
