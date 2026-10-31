using SCTools.Tests.Support;

namespace SCTools.Tests.Locations;

/// <summary>
/// When a quantum jump starts and ends, from the areas the player goes into (LocationTracker.JumpTimes): picked inside an area, it starts when he leaves into
/// open space (OpenSpace) and ends when he comes out of it into the next area; picked out in open space, 47 seconds after the pick. The numbers are the ones
/// of real jumps: from Ruin Station (zzoega, 30 Oct 2026) and from an asteroid base in Nyx to Levski (zzoega, 31 Oct 2026).
/// </summary>
public class JumpTimesTests
{
    static readonly DateTime T0 = new(2026, 10, 30, 3, 29, 31, DateTimeKind.Utc);
    const long Hangar = 2026442305, RuinArea = 685899972, PyroSpace = 3184339241, RatsNestArea = 3688917516;
    const long LevskiHangar = 3723364946, NyxSpace = 2627827560, MissionSite = 2969405299, AsteroidBase = 126935608, HurstonArea = 467417103;

    static QtEvent Picked(DateTime? at = null) => new() { Time = at ?? T0, Kind = QtKind.Selected, Destination = "rs_ext_pyro5_l5" };
    static AreaChange Line(double s, long from, long to, long hangar = Hangar) => new(T0.AddSeconds(s), hangar, 0, from, to);

    [Fact]
    public void Picked_in_a_hangar_the_jump_starts_when_he_leaves_into_open_space_and_ends_when_he_comes_out_of_it()
    {
        var moves = new[] { Line(30.3, Hangar, RuinArea), Line(32.5, RuinArea, PyroSpace), Line(221.1, PyroSpace, RatsNestArea) };
        Assert.Equal((T0.AddSeconds(32.5), T0.AddSeconds(221.1)), LocationTracker.JumpTimes(Picked(), moves, true, 200));
        Assert.Equal((T0.AddSeconds(32.5), (DateTime?)null), LocationTracker.JumpTimes(Picked(), moves.Take(2).ToList(), true, 200));   // on his way
        Assert.Equal(((DateTime?)null, (DateTime?)null), LocationTracker.JumpTimes(Picked(), moves.Take(1).ToList(), true, 200));   // taking off: not gone yet
        Assert.Equal((null, null), LocationTracker.JumpTimes(Picked(), Array.Empty<AreaChange>(), true, 200));
        // walking about the station (still in a landing area) is not leaving
        Assert.Equal((null, null), LocationTracker.JumpTimes(Picked(), new[] { new AreaChange(T0.AddSeconds(5), Hangar, Hangar, Hangar, 12345) }, true, 200));
    }

    [Fact]
    public void Picked_at_a_mission_site_the_jump_starts_when_he_leaves_it_however_long_after_the_pick()
    {
        // zzoega, 31 Oct 2026: at an asteroid base in Nyx he picked Levski at 20:19:53 and left the base into open space at 20:20:15; the map had started
        // the jump ten seconds after the pick. Here he lingers three minutes first, a hop inside the mission's site is not leaving either
        var pick = new DateTime(2026, 10, 31, 20, 19, 53, DateTimeKind.Utc);
        var moves = new[]
        {
            new AreaChange(pick.AddSeconds(60), LevskiHangar, 0, AsteroidBase, MissionSite),
            new AreaChange(pick.AddSeconds(180), LevskiHangar, 0, MissionSite, NyxSpace),
        };
        Assert.Equal(((DateTime?)null, (DateTime?)null), LocationTracker.JumpTimes(Picked(pick), moves.Take(1).ToList(), true, 120));
        Assert.Equal((pick.AddSeconds(180), (DateTime?)null), LocationTracker.JumpTimes(Picked(pick), moves, true, 120));
    }

    [Fact]
    public void Where_the_hangar_lets_out_into_open_space_out_of_the_hangar_is_leaving()
    {
        var moves = new[] { Line(40, LevskiHangar, NyxSpace, LevskiHangar) };
        Assert.Equal((T0.AddSeconds(40), (DateTime?)null), LocationTracker.JumpTimes(Picked(), moves, true, 200));
    }

    [Fact]
    public void A_short_hop_that_never_goes_into_open_space_is_not_drawn()
    {
        // between the orbital markers of Hurston: out of the station's area into the planet's, and the arrival comes; the map leaves him where he was until
        // the arrival puts him there (Frank, 31 Oct 2026: too small to draw on the map)
        var moves = new[] { Line(20, Hangar, RuinArea), Line(25, RuinArea, HurstonArea) };
        Assert.Equal((null, null), LocationTracker.JumpTimes(Picked(), moves, true, 60));
    }

    [Fact]
    public void Leaving_into_open_space_and_soon_into_a_planets_area_is_still_leaving()
    {
        // a station beside a planet: into open space, and a few seconds later into the planet's area, where the jump starts
        var moves = new[] { Line(30, RuinArea, PyroSpace), Line(36, PyroSpace, HurstonArea), Line(190, HurstonArea, RatsNestArea) };
        Assert.Equal((T0.AddSeconds(36), T0.AddSeconds(190)), LocationTracker.JumpTimes(Picked(), moves, true, 200));
    }

    [Fact]
    public void Picked_out_in_open_space_the_jump_starts_47_seconds_after_the_pick()
    {
        var start = T0 + LocationTracker.JumpDelay;
        Assert.Equal(TimeSpan.FromSeconds(47), LocationTracker.JumpDelay);
        Assert.Equal((start, (DateTime?)null), LocationTracker.JumpTimes(Picked(), null, false, 200));
        Assert.Equal((start, (DateTime?)null), LocationTracker.JumpTimes(Picked(), null, null, 200));   // a tracker from before: the log does not say
        // coming into where he jumped to: well into the jump, or even before the 47 seconds were up (a short jump)
        Assert.Equal((start, T0.AddSeconds(220)), LocationTracker.JumpTimes(Picked(), new[] { Line(220, PyroSpace, RatsNestArea) }, false, 200));
        Assert.Equal((start, T0.AddSeconds(30)), LocationTracker.JumpTimes(Picked(), new[] { Line(30, PyroSpace, RatsNestArea) }, false, 20));
    }

    [Fact]
    public void An_area_halfway_is_not_where_he_jumped_to_and_a_landing_is()
    {
        var moves = new[] { Line(30, Hangar, PyroSpace), new AreaChange(T0.AddSeconds(100), 0, 0, PyroSpace, 777), new AreaChange(T0.AddSeconds(150), 0, 0, 777, PyroSpace) };
        Assert.Equal((T0.AddSeconds(30), (DateTime?)null), LocationTracker.JumpTimes(Picked(), moves, true, 200));   // passing a planet: he still flies
        var landing = moves.Take(2).Append(new AreaChange(T0.AddSeconds(160), 0, 4242, PyroSpace, 4242)).ToList();
        Assert.Equal((T0.AddSeconds(30), T0.AddSeconds(160)), LocationTracker.JumpTimes(Picked(), landing, true, 200));
    }

    [Fact]
    public void Open_space_is_the_area_he_flies_his_jumps_in()
    {
        Assert.Equal(new long[] { 1902223495, 2627827560, 3184339241 }, OpenSpace.Known.Order());
        // a new system's open space: left shortly before the quantum arrival, more than a fifth of a hundred times or more
        const long newSpace = 11, station = 22, place = 33;
        var log = new HaulingLog();
        for (var i = 0; i < 100; i++)
        {
            var t = T0.AddHours(i);
            log.Areas.Add(new AreaChange(t, Hangar, 0, station, newSpace));
            log.Areas.Add(new AreaChange(t.AddMinutes(3), Hangar, 0, newSpace, place));
            if (i % 4 == 0) log.Qt.Add(new QtEvent { Time = t.AddMinutes(3).AddSeconds(10), Kind = QtKind.Arrived });
        }
        var learned = OpenSpace.Learn(new[] { log });
        Assert.Contains(newSpace, learned);
        Assert.DoesNotContain(station, learned);
        Assert.DoesNotContain(place, learned);
        Assert.Equal(OpenSpace.Known.Count, OpenSpace.Learn(new[] { new HaulingLog() }).Count);   // nothing new without enough lines
        Assert.DoesNotContain(newSpace, OpenSpace.Learn(new[] { log }, new HashSet<long> { newSpace }));   // a station's own area never is
    }

    [Fact]
    public void The_tracker_sends_the_area_lines_and_the_log_reads_them()
    {
        var w = new GameLogWriter().At(T0).Login("zzoega").AreaChange("zzoega", Hangar, 0, Hangar, RuinArea).AreaChange("someone", 1, 0, 1, 2);
        var line = w.Lines.Single(l => l.Contains("Player [zzoega] is changing location"));
        Assert.True(new LineFilter().Keep(line));
        var log = new HaulingLog();
        foreach (var l in w.Lines) log.ProcessLine(l);
        var a = Assert.Single(log.Areas);   // his own, not somebody else's
        Assert.Equal((Hangar, 0L, Hangar, RuinArea, false), (a.LandingFrom, a.LandingTo, a.From, a.To, a.Landed));
    }
}
