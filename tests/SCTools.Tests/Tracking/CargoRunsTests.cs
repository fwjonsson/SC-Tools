using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>The runs of the session: their order, what they did, lost cargo, the run a worker is flying, and the SCU estimates.</summary>
public class CargoRunsTests
{
    [Fact]
    public void A_stop_shows_the_scu_still_to_pick_up_there()
    {
        var (t, _) = PlayedSession.Load();
        var patch = t.State.PickupRows.Single(r => r.Zone == PlayedSession.PatchZone);   // the second mission's batch is still waiting there
        Assert.Equal(CargoRuns.AssumedMissionScu / 2.0, CargoRuns.WaitingScu(patch));     // the game did not say its SCU: 500 over 2 stops
        Assert.Equal("~" + 250.ToString("N0") + " SCU", CargoRuns.WaitingScuText(patch));
        Assert.Equal("", CargoRuns.WaitingScuText(new LocationRow()));                      // everything picked up
    }

    [Fact]
    public void The_runs_of_the_session_have_the_latest_activity_on_top()
    {
        var (t, _) = PlayedSession.Load();
        Assert.Collection(t.State.Trips,
            latest => Assert.Null(latest.End),          // the run on the way picked up last
            older => Assert.NotNull(older.End));
        Assert.All(t.State.Trips, r => Assert.Equal(PlayedSession.Player, r.Worker));
    }

    [Fact]
    public void A_run_shows_the_places_it_went_to_in_order()
    {
        var (t, _) = PlayedSession.Load();
        var (done, onTheWay) = PlayedSession.Runs(t);
        Assert.Equal("Gaslight, Patch City", t.Runs.RunPlacesText(done));
        var steps = t.Runs.RunStepsText(done);
        Assert.StartsWith("Gaslight ", steps);
        Assert.Contains(",  Patch City ", steps);
        Assert.Equal("Gaslight", t.Runs.RunPlacesText(onTheWay));
    }

    [Fact]
    public void The_batches_of_a_run_are_what_it_handed_in()
    {
        var (t, _) = PlayedSession.Load();
        var (done, onTheWay) = PlayedSession.Runs(t);
        Assert.Equal("1", t.Runs.BatchesText(done));
        Assert.Equal("", t.Runs.BatchesText(onTheWay));
        Assert.Equal(Formats.LongDuration(done.End!.Value - done.Start), t.Runs.RunDuration(done));
        Assert.Equal("", t.Runs.RunDuration(onTheWay));   // not watched live: nothing is counting
    }

    [Fact]
    public void All_cargo_lost_ends_the_run_and_none_lost_brings_it_back()
    {
        var (t, _) = PlayedSession.Load();
        var (_, onTheWay) = PlayedSession.Runs(t);

        Assert.True(t.Runs.SetLostLevel(onTheWay, "All"));   // the runs have to be worked out again
        Assert.Equal("All", t.Runs.LostLevel(onTheWay.Key));
        var end = Assert.Single(t.State.Inputs!.ManualEnds);
        Assert.Equal(PlayedSession.Player, end.Worker);

        t.LoadRuns();
        var failed = t.State.Trips.Single(r => r.Manual);
        Assert.Equal("failed", t.Runs.BatchesText(failed));
        Assert.True(CargoRuns.RunFailed(failed, t.State.Inputs));

        Assert.True(t.Runs.SetLostLevel(failed, "None"));
        Assert.Empty(t.State.Inputs.ManualEnds);
        Assert.Equal("None", t.Runs.LostLevel(failed.Key));
    }

    [Fact]
    public void Some_cargo_lost_changes_nothing_else()
    {
        var (t, _) = PlayedSession.Load();
        var (done, _) = PlayedSession.Runs(t);
        Assert.False(t.Runs.SetLostLevel(done, "Little"));
        Assert.Equal("Little", CargoRuns.LostLevelOf(t.State.Inputs!, done.Key));
        Assert.Empty(t.State.Inputs!.ManualEnds);
    }

    [Fact]
    public void Lost_cargo_ticked_by_an_older_version_counts_as_all()
    {
        var inp = new SessionInputs { LostRuns = new List<string> { "run" } };
        Assert.Equal("All", CargoRuns.LostLevelOf(inp, "run"));
        Assert.Equal("None", CargoRuns.LostLevelOf(inp, "other"));
        inp.LostCargo["run"] = "Little";
        Assert.Equal("Little", CargoRuns.LostLevelOf(inp, "run"));
    }

    [Fact]
    public void The_worker_flying_a_run_has_it_on_board_from_the_station_of_his_last_pickup()
    {
        var (t, _) = PlayedSession.Load();
        var active = t.Runs.ActiveRunOf("pilot_a");
        Assert.NotNull(active);
        Assert.Equal(1, active.Value.Loaded);
        Assert.Equal(PlayedSession.Gaslight, active.Value.From);
        Assert.True(t.Runs.HasActiveRun(PlayedSession.Player));
        Assert.Same(PlayedSession.Runs(t).OnTheWay, t.Runs.OpenRunOf(PlayedSession.Player));
        Assert.Null(t.Runs.ActiveRunOf("Pilot_B"));
    }

    [Fact]
    public void The_scu_of_a_stop_is_its_share_of_the_mission()
    {
        var known = new Mission { Multi = 2 };
        known.ObjScu["dropoff_x"] = 490;
        Assert.Equal(245, CargoRuns.StopScu(known));
        Assert.Equal(CargoRuns.AssumedMissionScu / 4.0, CargoRuns.StopScu(new Mission { Multi = 4 }));   // the game did not say: about 500 SCU
        Assert.Equal(0, CargoRuns.StopScu(new Mission()));

        var (t, _) = PlayedSession.Load();
        Assert.Equal((int)Math.Round(CargoRuns.AssumedMissionScu / 2.0), t.Runs.LoadedScuOf(PlayedSession.Player));
    }

    [Fact]
    public void Things_done_close_together_are_one_step()
    {
        var t0 = GameLogWriter.Start;
        var items = new[] { (T: t0, K: "Waste"), (T: t0.AddSeconds(60), K: "Waste"), (T: t0.AddSeconds(200), K: "Waste"), (T: t0.AddSeconds(210), K: "Ice") };
        var clusters = CargoRuns.Clusters(items, x => x.T, x => x.K);
        Assert.Equal(new[] { 2, 1, 1 }, clusters.Select(c => c.Count));
    }

    [Fact]
    public void The_flight_time_of_a_run_is_from_Ruin_to_its_stations_and_back()
    {
        var (t, _) = PlayedSession.Load();
        var drive = QuantumDrives.Assumed;   // no drive set
        var expected = drive.Seconds(PyroDistances.Between(PyroDistances.Ruin, PlayedSession.Gaslight)) * 2;
        Assert.Equal(expected, t.Runs.FlightOf(PlayedSession.Player, new[] { PlayedSession.Gaslight }), 6);
        Assert.StartsWith("~", t.Runs.RunFlightText(PlayedSession.Runs(t).Done));
    }
}
