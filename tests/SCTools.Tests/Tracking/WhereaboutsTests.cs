using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>Where a worker is: from his last pickup, set by hand until the logs show him moving, the station he is flying to, still loading.</summary>
public class WhereaboutsTests
{
    const string Ruin = "rs_ext_pyro6_leo";            // the quantum targets as the log names them
    const string PatchCityTarget = "rs_ext_pyro3_l3";
    const string GaslightTarget = "rs_ext_pyro5_l2";

    [Fact]
    public void The_logs_place_a_worker_at_the_station_of_his_last_pickup()
    {
        var (t, _) = PlayedSession.Load();
        Assert.Equal(PlayedSession.Gaslight, t.Whereabouts.AutoLocationOf(PlayedSession.Player));
        Assert.Equal((PlayedSession.Gaslight, false), t.Whereabouts.LocationOf(PlayedSession.Player));
        Assert.Equal(PlayedSession.LastPickup, t.Whereabouts.LastMoveOf(PlayedSession.Player));
        Assert.Equal(PyroDistances.Ruin, t.Whereabouts.AutoLocationOf("Pilot_B"));   // nothing known of him: at Ruin
    }

    [Fact]
    public void The_maps_place_counts_when_it_is_newer_than_what_the_planner_saw_him_do()
    {
        // the map and the planner learn from each other (Frank, 29 Oct 2026): the server gives the planner where the map has everybody (a crew member's jump
        // through the pilot's log, a destination or an area named on Map places), and it counts when it is newer than his last pickup or delivery
        // ... but not while he is in the middle of a run (Frank, 30 Oct 2026: his trip is planned from the station of his last pickup, see the test below)
        var data = PlayedSession.Data();
        data.Party.Add("Pilot_B");
        var (t, _) = PlayedSession.Load(data, w => w.Minutes(1).LoadingPlatform().Seconds(5).Objective(PlayedSession.M2, "dropoff_m2_0", "COMPLETED").Seconds(1).MissionEnded(PlayedSession.M2, "COMPLETED"));   // his run is handed in: no run in progress
        Tracked.Place(t, PlayedSession.Player, new PlannerPlace(PlayedSession.PatchCity, PlayedSession.LastPickup.AddMinutes(10), null, null));
        Assert.Equal(PlayedSession.PatchCity, t.Whereabouts.AutoLocationOf(PlayedSession.Player));
        Assert.Equal((PlayedSession.PatchCity, false), t.Whereabouts.LocationOf(PlayedSession.Player));
        Tracked.Place(t, "Pilot_B", new PlannerPlace(PlayedSession.Gaslight, PlayedSession.LastPickup.AddMinutes(10), null, null));   // a member without a log: the map alone places him
        Assert.Equal(PlayedSession.Gaslight, t.Whereabouts.AutoLocationOf("Pilot_B"));
        Tracked.Place(t, PlayedSession.Player, new PlannerPlace(PlayedSession.PatchCity, PlayedSession.LastPickup.AddMinutes(-10), null, null));   // older than his pickup: the pickup wins
        Assert.Equal(PyroDistances.Ruin, t.Whereabouts.AutoLocationOf(PlayedSession.Player));
        Tracked.Place(t, PlayedSession.Player, new PlannerPlace(null, null, PlayedSession.PatchCity, PlayedSession.LastPickup.AddMinutes(5)));   // on his way, by the map
        Assert.Equal(PlayedSession.PatchCity, t.Whereabouts.HeadingFor(PlayedSession.Player));
        Assert.Equal(PyroDistances.Ruin, t.Whereabouts.AutoLocationOf(PlayedSession.Player));   // on his way: still where he was (Ruin, his run handed in)
        // a station set by hand gives way when the map sees him arrive somewhere after it
        t.Whereabouts.SetLocation(PlayedSession.Player, PlayedSession.PatchCity);
        Assert.Equal((PlayedSession.PatchCity, true), t.Whereabouts.LocationOf(PlayedSession.Player));
        Tracked.Place(t, PlayedSession.Player, new PlannerPlace(PyroDistances.Ruin, DateTime.UtcNow.AddMinutes(1), null, null));
        Assert.Equal((PyroDistances.Ruin, false), t.Whereabouts.LocationOf(PlayedSession.Player));
    }

    [Fact]
    public void The_maps_place_does_not_count_in_the_middle_of_a_run()
    {
        // Pilot_A has picked up at Gaslight and not handed in: his trip is planned from Gaslight whatever the map says (Frank, 30 Oct 2026: the map put him at
        // the station he was flying to, and the rows of the trip he was in the middle of vanished from the Current Route)
        var (t, _) = PlayedSession.Load();
        Tracked.Place(t, PlayedSession.Player, new PlannerPlace(PlayedSession.PatchCity, PlayedSession.LastPickup.AddMinutes(10), null, null));
        Assert.Equal(PlayedSession.Gaslight, t.Whereabouts.AutoLocationOf(PlayedSession.Player));
        Assert.Equal((PlayedSession.Gaslight, false), t.Whereabouts.LocationOf(PlayedSession.Player));
    }

    [Fact]
    public void A_station_set_by_hand_holds_until_the_logs_show_him_moving()
    {
        var setAt = PlayedSession.LastPickup.AddMinutes(1);
        var data = PlayedSession.Data();
        data.StartPoints[PlayedSession.Player] = new StartPoint { Id = PlayedSession.PatchCity, Since = setAt };
        var (t, _) = PlayedSession.Load(data);
        Assert.Equal((PlayedSession.PatchCity, true), t.Whereabouts.LocationOf(PlayedSession.Player));

        // he sets a quantum destination after that: the logs follow him again
        var (moved, saves) = PlayedSession.Load(data, w => w.Minutes(5).QuantumSelected(GaslightTarget));
        var before = saves.Count;
        Assert.Equal((PlayedSession.Gaslight, false), moved.Whereabouts.LocationOf(PlayedSession.Player));
        Assert.False(data.StartPoints.ContainsKey(PlayedSession.Player));
        Assert.Equal(before + 1, saves.Count);
    }

    [Fact]
    public void Setting_the_station_the_logs_say_is_no_choice_at_all()
    {
        var (t, _) = PlayedSession.Load();
        var points = t.State.Data.StartPoints;
        t.Whereabouts.SetLocation(PlayedSession.Player, PlayedSession.PatchCity);
        Assert.Equal(PlayedSession.PatchCity, points[PlayedSession.Player].Id);
        t.Whereabouts.SetLocation(PlayedSession.Player, PlayedSession.Gaslight);   // where the logs have him anyway
        Assert.Empty(points);
        t.Whereabouts.SetLocation(PlayedSession.Player, PlayedSession.PatchCity);
        t.Whereabouts.SetLocation(PlayedSession.Player, null);   // Automatic
        Assert.Empty(points);
    }

    [Fact]
    public void He_is_heading_for_the_destination_he_set_after_his_last_pickup()
    {
        var (t, _) = PlayedSession.Load(more: w => w.Minutes(2).QuantumSelected(PatchCityTarget));
        Assert.Equal(PlayedSession.PatchCity, t.Whereabouts.HeadingFor(PlayedSession.Player));

        var (home, _) = PlayedSession.Load(more: w => w.Minutes(2).QuantumSelected(Ruin));
        Assert.Null(home.Whereabouts.HeadingFor(PlayedSession.Player));   // on his way to Ruin: no station of the plan

        var (none, _) = PlayedSession.Load();
        Assert.Null(none.Whereabouts.HeadingFor(PlayedSession.Player));
    }

    [Fact]
    public void He_is_still_loading_until_he_selects_his_next_destination()
    {
        var since = PlayedSession.LastPickup.AddMinutes(7);   // after he arrived somewhere and took cargo out
        var (loading, _) = PlayedSession.Load(more: w => w.Minutes(1).QuantumSelected(GaslightTarget).Minutes(5).QuantumArrived());
        Assert.True(loading.Whereabouts.StillLoading(PlayedSession.Player, since));

        var (gone, _) = PlayedSession.Load(more: w => w.Minutes(1).QuantumSelected(GaslightTarget).Minutes(5).QuantumArrived().Minutes(3).QuantumSelected(PatchCityTarget));
        Assert.False(gone.Whereabouts.StillLoading(PlayedSession.Player, since));

        var (noTravel, _) = PlayedSession.Load();   // his log has no quantum travel: nothing tells
        Assert.False(noTravel.Whereabouts.StillLoading(PlayedSession.Player, since));
        Assert.False(loading.Whereabouts.StillLoading(PlayedSession.Player, default));
    }
}
