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
