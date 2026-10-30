using SCTools.Tests.Support;

namespace SCTools.Tests.GameLog;

/// <summary>What the parser reads from the lines of one player's Game.log.</summary>
public class HaulingLogTests
{
    static readonly string M1 = GameLogWriter.MissionId(1);

    [Fact]
    public void The_character_of_the_log_is_the_player()
    {
        var log = new GameLogWriter().Login("Pilot_A").Read();
        Assert.Equal("Pilot_A", log.Player);
    }

    [Fact]
    public void A_contract_to_Ruin_is_a_hauling_mission_with_its_cargo_and_number_of_stops()
    {
        var log = new GameLogWriter()
            .Accepted(M1)
            .Marker(M1, string.Format(GameLogWriter.HaulingContract, 3), "pickup_aaaa_0", "900000000101", GameLogWriter.Gaslight)
            .Read();
        var m = log.Missions[M1];
        Assert.True(m.IsHauling);
        Assert.False(m.IsOut);
        Assert.Equal("Carbon", m.Cargo);
        Assert.Equal(3, m.Multi);
        Assert.Equal(GameLogWriter.Start, m.Accepted);
        Assert.Equal("900000000101", m.ObjZone["pickup_aaaa_0"]);
    }

    [Fact]
    public void A_contract_to_another_station_is_a_hauling_mission_whose_drop_off_is_that_station()
    {
        // zzoega, 26 Oct 2026: "Master | Stellar Large Haul | to Stanton Gateway", the same shape as a to-Ruin contract (stage 1a of docs/planner-any-hauling.md)
        var log = new GameLogWriter()
            .Accepted(M1)
            .Marker(M1, string.Format(GameLogWriter.StationContract, 4), "pickup_dddd_0", "900000000501", GameLogWriter.Gaslight)
            .Marker(M1, string.Format(GameLogWriter.StationContract, 4), "dropoff_dddd_0", "900000000502", GameLogWriter.RodsFuel)
            .Read();
        var m = log.Missions[M1];
        Assert.True(m.IsHauling);
        Assert.True(m.ToStation);
        Assert.False(m.ToRuin);
        Assert.Equal(("Waste", 4), (m.Cargo, m.Multi));
        Assert.Equal("900000000502", SessionBuilder.DropZoneOf(m));
    }

    [Fact]
    public void A_station_is_recognised_by_the_position_of_its_marker()
    {
        new GameLogWriter().Marker(M1, string.Format(GameLogWriter.HaulingContract, 2), "pickup_bbbb_0", "900000000202", GameLogWriter.PatchCity).Read();
        Assert.Equal("70120", StationIds.Of("900000000202"));   // Patch City
    }

    [Fact]
    public void A_contract_from_Ruin_is_a_load_with_drop_offs_and_their_SCU()
    {
        var log = new GameLogWriter()
            .Marker(M1, string.Format(GameLogWriter.WasteContract, 4), "dropoff_cccc_1", "900000000303", GameLogWriter.RodsFuel)
            .DeliverPopup(M1, "dropoff_cccc_1", 147, "Waste", "Rod's Fuel 'N Supplies at the LN Lagrange of Pyro V")
            .Read();
        var m = log.Missions[M1];
        Assert.True(m.IsOut);
        Assert.False(m.IsHauling);
        Assert.Equal("Waste", m.OutCargo);
        Assert.Equal(4, m.OutDrops);
        Assert.Equal(147, m.ObjScu["dropoff_cccc_1"]);
    }

    [Fact]
    public void A_direct_contract_from_Ruin_is_a_load_with_one_drop_off()
    {
        // as zzatvo's log of 23 Oct 15:09 writes it: a pickup at Ruin and one drop-off
        var log = new GameLogWriter()
            .Marker(M1, GameLogWriter.DirectContract, "pickup_eeee_0", "900000000401", GameLogWriter.RuinStation)
            .Marker(M1, GameLogWriter.DirectContract, "dropoff_eeee_0", "900000000402", GameLogWriter.Gaslight)
            .DeliverPopup(M1, "dropoff_eeee_0", 421, "Waste", "Gaslight at the L2 Lagrange of Pyro V")
            .Read();
        var m = log.Missions[M1];
        Assert.True(m.IsOut);
        Assert.True(m.IsDirect);
        Assert.False(m.IsHauling);
        Assert.Equal("Waste", m.OutCargo);
        Assert.Equal(1, m.OutDrops);
        Assert.Equal(421, m.ObjScu["dropoff_eeee_0"]);
    }

    [Fact]
    public void Objective_updates_and_the_end_of_a_mission_are_kept()
    {
        var w = new GameLogWriter().Accepted(M1).Minutes(10).Objective(M1, "pickup_dddd_0", "COMPLETED").Minutes(20).MissionEnded(M1, "COMPLETED");
        var log = w.Read();
        var m = log.Missions[M1];
        Assert.Equal("COMPLETED", Assert.Single(m.Events["pickup_dddd_0"]).State);
        Assert.True(m.Completed);
        Assert.Equal(w.Now, m.EndedAt);
        Assert.Equal(w.Now, log.LastTime);
    }

    [Fact]
    public void An_abandoned_mission_is_dead_until_it_is_accepted_again()
    {
        var abandoned = new GameLogWriter().Accepted(M1).Minutes(5).Abandoned(M1, "Pilot_A");
        Assert.True(abandoned.Read().Missions[M1].Dead);

        var again = abandoned.Minutes(5).Accepted(M1).Read().Missions[M1];
        Assert.False(again.Dead);
        Assert.Null(again.EndState);
    }

    [Fact]
    public void Quantum_travel_is_read_with_destination_start_and_ship()
    {
        var log = new GameLogWriter()
            .QuantumSelected("rs_ext_pyro3_l3").Seconds(0.2).RouteStart("Dudley & Daughters", "rs_ext_pyro3_l3")
            .Minutes(5).QuantumArrived()
            .Read();
        Assert.Collection(log.Qt,
            selected =>
            {
                Assert.Equal(QtKind.Selected, selected.Kind);
                Assert.Equal("rs_ext_pyro3_l3", selected.Destination);
                Assert.Equal("Dudley & Daughters", selected.From);
                Assert.Equal("CRUS_Starlifter_C2", selected.Ship);
            },
            arrived => Assert.Equal(QtKind.Arrived, arrived.Kind));
    }

    [Fact]
    public void Money_sent_and_received_is_read_with_the_amount_on_the_next_line()
    {
        var log = new GameLogWriter().Login("Pilot_A").Sent("Pilot_B", 250_000).Minutes(1).Received("Pilot_C", 1_268_250).Read();
        Assert.Collection(log.Transfers,
            sent => { Assert.Equal("", sent.From); Assert.Equal("Pilot_B", sent.To); Assert.Equal(250_000m, sent.Amount); },
            got => { Assert.Equal("Pilot_C", got.From); Assert.Equal("", got.To); Assert.Equal(1_268_250m, got.Amount); });
    }

    [Fact]
    public void A_burst_of_body_items_is_one_death()
    {
        var w = new GameLogWriter();
        var died = w.Now;
        w.BodyItem().Seconds(0.1).BodyItem().Seconds(0.1).BodyItem().Seconds(0.1).BodyItem();
        Assert.Equal(died, Assert.Single(w.Read().Deaths));
    }

    [Fact]
    public void Two_body_items_are_not_a_death()
    {
        Assert.Empty(new GameLogWriter().BodyItem().Seconds(0.1).BodyItem().Read().Deaths);
    }
}
