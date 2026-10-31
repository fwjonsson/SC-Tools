using SCTools.Tests.Support;

namespace SCTools.Tests.Locations;

/// <summary>
/// When a quantum jump starts and ends, from the areas the player goes into (LocationTracker.JumpTimes): off the pad and out of the station's area is the start,
/// the area of where he jumped to is the end. The numbers are the ones of a real jump from Ruin Station (zzoega, 30 Oct 2026).
/// </summary>
public class JumpTimesTests
{
    static readonly DateTime T0 = new(2026, 10, 30, 3, 29, 31, DateTimeKind.Utc);
    const long Hangar = 2026442305, RuinArea = 685899972, Space = 3184339241, RatsNestArea = 3688917516;

    static QtEvent Picked() => new() { Time = T0, Kind = QtKind.Selected, Destination = "rs_ext_pyro5_l5" };
    static AreaChange Off(double s, long from, long to) => new(T0.AddSeconds(s), Hangar, 0, from, to);

    [Fact]
    public void Picked_on_the_pad_the_jump_starts_when_he_is_out_of_the_stations_area_and_ends_in_the_next_area()
    {
        var moves = new[] { Off(30.3, Hangar, RuinArea), Off(32.5, RuinArea, Space), Off(221.1, Space, RatsNestArea) };
        Assert.Equal((T0.AddSeconds(32.5), T0.AddSeconds(221.1)), LocationTracker.JumpTimes(Picked(), moves, true, 200));
        Assert.Equal((T0.AddSeconds(32.5), (DateTime?)null), LocationTracker.JumpTimes(Picked(), moves.Take(2).ToList(), true, 200));   // on his way
        Assert.Equal((T0.AddSeconds(30.3), (DateTime?)null), LocationTracker.JumpTimes(Picked(), moves.Take(1).ToList(), true, 200));   // off the pad
    }

    [Fact]
    public void Picked_on_the_pad_and_not_off_it_yet_the_jump_has_not_started()
    {
        Assert.Equal((null, null), LocationTracker.JumpTimes(Picked(), Array.Empty<AreaChange>(), true, 200));
        // walking about the station (still in a landing area) is not leaving
        Assert.Equal((null, null), LocationTracker.JumpTimes(Picked(), new[] { new AreaChange(T0.AddSeconds(5), Hangar, Hangar, Hangar, 12345) }, true, 200));
    }

    [Fact]
    public void Picked_out_in_space_the_jump_starts_a_few_seconds_after_the_pick()
    {
        var start = T0 + LocationTracker.JumpDelay;
        Assert.Equal((start, (DateTime?)null), LocationTracker.JumpTimes(Picked(), null, false, 200));
        Assert.Equal((start, (DateTime?)null), LocationTracker.JumpTimes(Picked(), null, null, 200));   // a tracker from before: the log does not say
        // near a station when he picked: out of its area soon after is the start; the next area, well into the jump, the end
        var moves = new[] { new AreaChange(T0.AddSeconds(15), 0, 0, RuinArea, Space), new AreaChange(T0.AddSeconds(190), 0, 0, Space, RatsNestArea) };
        Assert.Equal((T0.AddSeconds(15), T0.AddSeconds(190)), LocationTracker.JumpTimes(Picked(), moves, false, 200));
    }

    [Fact]
    public void An_area_halfway_is_not_where_he_jumped_to_and_a_landing_is()
    {
        var moves = new[] { Off(30, Hangar, Space), new AreaChange(T0.AddSeconds(100), 0, 0, Space, 777), new AreaChange(T0.AddSeconds(150), 0, 0, 777, Space) };
        Assert.Equal((T0.AddSeconds(30), (DateTime?)null), LocationTracker.JumpTimes(Picked(), moves, true, 200));   // passing through: he still flies
        var landing = moves.Take(2).Append(new AreaChange(T0.AddSeconds(160), 0, 4242, Space, 4242)).ToList();
        Assert.Equal((T0.AddSeconds(30), T0.AddSeconds(160)), LocationTracker.JumpTimes(Picked(), landing, true, 200));
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
