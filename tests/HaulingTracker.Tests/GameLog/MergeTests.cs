using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.GameLog;

/// <summary>The logs of several accounts read together: a mission shared in the party appears once.</summary>
public class MergeTests
{
    static readonly string M1 = GameLogWriter.MissionId(11);

    [Fact]
    public void A_shared_mission_is_one_mission_held_by_everybody_who_has_it()
    {
        var a = new GameLogWriter().Login("Pilot_A").Accepted(M1).Read();
        var b = new GameLogWriter().Login("Pilot_B").Minutes(3).Accepted(M1).Read();
        var merged = HaulingLog.Merge(new[] { a, b });
        var m = Assert.Single(merged.Missions.Values);
        Assert.Equal(new[] { "Pilot_A", "Pilot_B" }, m.Holders.OrderBy(x => x));
        Assert.Equal("Pilot_A", m.Taker);   // the first log that accepted it
        Assert.Equal(GameLogWriter.Start, m.Accepted);
    }

    [Fact]
    public void A_mission_abandoned_by_one_player_lives_on_while_another_has_it()
    {
        var a = new GameLogWriter().Login("Pilot_A").Accepted(M1).Read();
        var b = new GameLogWriter().Login("Pilot_B").Accepted(M1).Minutes(5).Abandoned(M1, "Pilot_B").Read();
        var m = HaulingLog.Merge(new[] { a, b }).Missions[M1];
        Assert.Null(m.EndState);
        Assert.Equal(new[] { "Pilot_A" }, m.Holders);
    }

    [Fact]
    public void A_transfer_seen_in_both_logs_counts_once()
    {
        var a = new GameLogWriter().Login("Pilot_A").Sent("Pilot_B", 500_000).Read();
        var b = new GameLogWriter().Login("Pilot_B").Seconds(2).Received("Pilot_A", 500_000).Read();
        var t = Assert.Single(HaulingLog.Merge(new[] { a, b }).Transfers);
        Assert.Equal("Pilot_A", t.From);
        Assert.Equal("Pilot_B", t.To);
        Assert.Equal(500_000m, t.Amount);
    }

    [Fact]
    public void Every_player_keeps_his_own_quantum_travel()
    {
        var a = new GameLogWriter().Login("Pilot_A").QuantumSelected("rs_ext_pyro6_leo").Read();
        var b = new GameLogWriter().Login("Pilot_B").Read();
        var merged = HaulingLog.Merge(new[] { a, b });
        Assert.Single(merged.QtByPlayer["Pilot_A"]);
        Assert.Empty(merged.QtByPlayer["Pilot_B"]);
    }
}
