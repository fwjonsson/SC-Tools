using SCTools.Server;
using SCTools.Tests.Support;
using static SCTools.Server.MapService;

namespace SCTools.Tests.GameData;

/// <summary>
/// The missions on the Map tool: what an objective says, its system and its quantum beacon (from the log); a jump to the beacon is to the objective, also a
/// jump with no destination picked (at the marker he looked at); a crew's jump goes where the pilot picked; else he is somewhere the map does not know.
/// </summary>
public class MapMissionTests
{
    static readonly DateTime T = new(2026, 10, 28, 13, 18, 0, DateTimeKind.Utc);
    const string Beacon = "MISSION_QT_Quantum_Beacon_LongRange_865476577647";
    const string Perseus = "898683539008";

    static QtEvent Picked(DateTime t, string destination, string from = "", string shipId = "") =>
        new() { Time = t, Kind = QtKind.Selected, Destination = destination, From = from, ShipId = shipId };
    static QtEvent Arrived(DateTime t, string shipId = "") => new() { Time = t, Kind = QtKind.Arrived, ShipId = shipId };

    static MissionPoint Point(DateTime? until = null, Place? place = null, string text = "Go to Asteroid Cluster") =>
        new(Beacon, "21517", "Reclusive Bounty", text, "nyx", place, T, until);

    static readonly LocationSeen[] AtLevski = { new(T.AddMinutes(-5), "Nyx_Levski") };

    [Fact]
    public void The_log_says_what_an_objective_is_its_system_and_its_beacon()
    {
        var m = GameLogWriter.MissionId(7);
        var w = new GameLogWriter().At(T).Login("zzoega").MissionMarker(m, GameLogWriter.BountyContract, "obj-1", "21517")
            .ObjectivePopup(m, "obj-1", "Go to Asteroid Cluster").BeaconPaired("21517", Beacon).Seconds(5).BeaconPaired("21517", Beacon)
            .Minutes(4).QuantumArrived("RSI_Perseus", Perseus)
            .Seconds(5).Objective(m, "obj-1", "COMPLETED");
        Assert.All(w.Lines, l => Assert.True(new LineFilter().Keep(l), l));   // the trackers send all of these lines
        var log = w.Read();
        var mission = log.Missions[m];
        Assert.Equal(("nyx", "Go to Asteroid Cluster"), (mission.System, mission.ObjText["obj-1"]));
        var b = Assert.Single(log.Beacons);   // paired twice: once
        Assert.Equal((T, Beacon, "21517", m, "obj-1"), (b.Time, b.NavPoint, b.Marker, b.MissionId, b.Objective));
        Assert.Equal(("RSI_Perseus", Perseus), (log.Qt[^1].Ship, log.Qt[^1].ShipId));

        // the objective to jump to, until it was done
        var p = Assert.Single(Points(log.Beacons, log.Missions, _ => "Reclusive Bounty", Atlas.Default));
        Assert.Equal(("Go to Asteroid Cluster", "nyx", (Place?)null, T.AddMinutes(4).AddSeconds(10)), (p.Text, p.System, p.Place, p.Until));
        Assert.True(p.Is(Beacon) && p.Is("missionobjectivemarker_21517") && !p.Is("rs_ext_pyro5_l2"));

        // a beacon is never a place of the map, nor in the admin's list of what the map does not know
        Assert.Equal((true, (Place?)null), Atlas.Default.AliasOf(Beacon));
    }

    [Fact]
    public void A_jump_with_no_destination_picked_is_to_the_objective_going_on()
    {
        var qt = new List<QtEvent> { Arrived(T.AddMinutes(4)) };
        var w = Follow(qt, AtLevski, new[] { Point() });
        Assert.Equal(("nyx", (Place?)null, T.AddMinutes(4), false), (w.System, w.At, w.Since, w.Jumped));
        Assert.Equal("Go to Asteroid Cluster", w.AtMission?.Text);

        // the objective was done a minute before he arrived: still the jump to it (the game ends it as he gets there)
        Assert.NotNull(Follow(qt, AtLevski, new[] { Point(until: T.AddMinutes(3.5)) }).AtMission);
        // done long before: somewhere the map does not know (not at Levski any more)
        w = Follow(qt, AtLevski, new[] { Point(until: T.AddMinutes(1)) });
        Assert.Equal(("nyx", (Place?)null, (MissionPoint?)null, true, T.AddMinutes(4)), (w.System, w.At, w.AtMission, w.Jumped, w.Since));
        Assert.Equal("nyx/levski", w.LastPing?.Key);   // off the grid: the last place the map had him at

        // the objective's text names a place: he is there (at the mission)
        var daymar = StarMap.Find("stanton/daymar")!;
        w = Follow(qt, AtLevski, new[] { Point(place: daymar, text: "Go to a debris field above Daymar") with { System = "stanton" } });
        Assert.Equal(("stanton", "stanton/daymar", "Go to a debris field above Daymar"), (w.System, w.At?.Key, w.AtMission?.Text));

        // he opened his inventory at Levski again: there
        w = Follow(qt, new[] { AtLevski[0], new LocationSeen(T.AddMinutes(30), "Nyx_Levski") }, new[] { Point() });
        Assert.Equal(("nyx/levski", (MissionPoint?)null), (w.At?.Key, w.AtMission));
    }

    [Fact]
    public void A_jump_to_the_beacon_of_an_objective_is_to_the_mission()
    {
        var qt = new List<QtEvent> { Picked(T.AddMinutes(1), Beacon, "Levski") };
        var w = Follow(qt, AtLevski, new[] { Point() });
        Assert.Equal(("nyx", "nyx/levski", (Place?)null, "Go to Asteroid Cluster"), (w.System, w.From?.Key, w.To, w.ToMission?.Text));
        qt.Add(Arrived(T.AddMinutes(3)));
        w = Follow(qt, AtLevski, new[] { Point() });
        Assert.Equal(((Place?)null, "Go to Asteroid Cluster", (QtEvent?)null, false), (w.At, w.AtMission?.Text, w.Picked, w.Jumped));
    }

    [Fact]
    public void A_jump_with_no_destination_and_no_mission_is_somewhere_the_map_does_not_know()
    {
        // he was at Ruin; the arrival of a jump at something he looked at: not at Ruin any more
        var qt = new List<QtEvent> { Picked(T, "rs_ext_pyro5_l2", "Ruin Station"), Arrived(T.AddMinutes(3)), Arrived(T.AddMinutes(20)) };
        var w = Follow(qt);
        Assert.Equal(("pyro", (Place?)null, true, T.AddMinutes(20), "pyro/gaslight"), (w.System, w.At, w.Jumped, w.Since, w.LastPing?.Key));
        // another jump the map cannot follow: still the last place it knew
        Assert.Equal("pyro/gaslight", Follow(qt.Append(Arrived(T.AddMinutes(22))).ToList()).LastPing?.Key);
        // a route from where he is then: there again
        qt.Add(Picked(T.AddMinutes(25), "pyro3", "Gaslight"));
        w = Follow(qt);
        Assert.Equal(("pyro/gaslight", "pyro/bloom", false, (Place?)null), (w.From?.Key, w.To?.Key, w.Jumped, w.LastPing));
    }

    [Fact]
    public void A_crew_jumps_where_the_pilot_picked()
    {
        // zzoega flies the Perseus from Ruin to Gaslight; zzatvo is aboard (his log has the arrival, not the destination)
        var pilot = new List<QtEvent> { Picked(T, "rs_ext_pyro5_l2", "Ruin Station", Perseus), Arrived(T.AddMinutes(3), Perseus) };
        var crew = new List<QtEvent> { Arrived(T.AddMinutes(3).AddSeconds(1), Perseus) };
        var w = Follow(WithPilots(crew, new[] { (IReadOnlyList<QtEvent>)pilot }));
        Assert.Equal(("pyro", "pyro/gaslight", T.AddMinutes(3).AddSeconds(1)), (w.System, w.At?.Key, w.Since));

        // another ship, or a minute later: not the pilot's jump
        Assert.True(Follow(WithPilots(new List<QtEvent> { Arrived(T.AddMinutes(3), "111111111111") }, new[] { (IReadOnlyList<QtEvent>)pilot })).Jumped);
        Assert.True(Follow(WithPilots(new List<QtEvent> { Arrived(T.AddMinutes(4), Perseus) }, new[] { (IReadOnlyList<QtEvent>)pilot })).Jumped);
        // his own pick stays his
        var own = WithPilots(new List<QtEvent> { Picked(T, "pyro3", "Ruin Station", Perseus), Arrived(T.AddMinutes(3), Perseus) }, new[] { (IReadOnlyList<QtEvent>)pilot });
        Assert.Equal("pyro/bloom", Follow(own).At?.Key);
    }

    [Theory]
    [InlineData("Go to a debris field above Daymar", "stanton", "stanton/daymar")]
    [InlineData("Go to the asteroids surrounding Yela", null, "stanton/yela")]
    [InlineData("Go to a Hurston Weapons Testing Site above Magda", "stanton", "stanton/magda")]
    [InlineData("Go to a debris field above Daymar", "nyx", null)]   // not in the contract's system
    [InlineData("Go to Asteroid Cluster", "nyx", null)]
    [InlineData("Go to Moraine Base", "nyx", null)]
    public void An_objective_that_names_a_place_is_there(string text, string? system, string? key) =>
        Assert.Equal(key, NamedIn(text, system, Atlas.Default)?.Key);
}
