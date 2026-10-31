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

    [Fact]
    public void A_mission_the_game_took_from_him_at_an_exit_to_the_menu_is_dead_until_it_is_accepted_again()
    {
        // zzoega, 31 Oct 2026 (#399): he exited to the menu with a bounty going; his log has the game's PlayerLeft for his own id a second later and no
        // EndMission, and the bounty was gone when he came back
        var w = new GameLogWriter().Login("Pilot_A").Minutes(5).Accepted(M1).Minutes(20).ExitToMenu().Seconds(1.4).PlayerLeft(M1, GameLogWriter.OwnId);
        var left = w.Now;
        var log = w.Read();
        Assert.Equal(GameLogWriter.OwnId, log.PlayerId);   // from the login line: the log has no "Ending mission" line
        var m = log.Missions[M1];
        Assert.True(m.Dead);
        Assert.Equal(Mission.Left, m.EndState);
        Assert.Equal(left, m.EndedAt);
        Assert.Equal(GameLogWriter.OwnId, Assert.Single(log.MemberEvents).Player);   // what the game said is kept too

        // an event mission said PlayerLeft for him and "Contract Accepted" again two seconds later (28 Aug 2026): running again
        var again = w.Seconds(1.7).Accepted(M1).Read().Missions[M1];
        Assert.False(again.Dead);
        Assert.Null(again.EndState);
        Assert.Null(again.EndedAt);
    }

    [Fact]
    public void Another_player_leaving_his_mission_and_his_own_abandon_are_read_as_before()
    {
        // somebody else left the mission he shared: a member event, the mission goes on for him
        var others = new GameLogWriter().Login("Pilot_A").Accepted(M1).Minutes(5).PlayerLeft(M1, "200000000002").Read();
        Assert.False(others.Missions[M1].Dead);
        Assert.Equal("200000000002", Assert.Single(others.MemberEvents).Player);

        // the normal abandon: the game's PlayerLeft for him and his EndMission two milliseconds later, which says how it ended (82 of 83 in Frank's logs of Aug-Oct 2026)
        var w = new GameLogWriter().Login("Pilot_A").Accepted(M1).Minutes(5).PlayerLeft(M1, GameLogWriter.OwnId).Seconds(0.002).Abandoned(M1, "Pilot_A");
        var m = w.Read().Missions[M1];
        Assert.Equal("ABANDON", m.EndState);
        Assert.Equal(w.Now, m.EndedAt);
    }

    [Fact]
    public void His_id_comes_from_the_markers_that_name_him_when_the_login_line_has_none()
    {
        var log = new HaulingLog();
        log.ProcessLine("<2026-10-21T08:00:00.000Z> [Notice] <AccountLoginCharacterStatus_Character> Character: createdAt 1791557604958 - updatedAt 1791557605400 - accountId 1000001 - name Pilot_A - state STATE_CURRENT [Team_GameServices][Login]");
        Assert.Equal(("Pilot_A", null), (log.Player, log.PlayerId));
        // his objective marker names him with his id ("Added to DataBank of Player: zzoega[493008374737]"), a shared one names the other player
        var w = new GameLogWriter().Minutes(1).Accepted(M1).DataBank(M1, "obj-1", "Pilot_B", "200000000002").DataBank(M1, "obj-1", "Pilot_A", "300000000003")
            .Minutes(9).PlayerLeft(M1, "300000000003");
        foreach (var line in w.Lines) log.ProcessLine(line);
        Assert.Equal("300000000003", log.PlayerId);
        Assert.True(log.Missions[M1].Dead);
    }
}
