using SCTools.Runs;
using SCTools.Tests.Support;

namespace SCTools.Tests.Runs;

/// <summary>Who did a step of a from-Ruin contract: the loading platform lines say, and only a holder of the mission can be it.</summary>
public class OutStepsTests
{
    [Fact]
    public void Two_players_at_loading_platforms_at_the_same_moment_are_told_apart_by_who_has_the_mission()
    {
        // 29 Oct 2026, 00:53: zzatvo hands his own direct haul in at Gaslight while zzoega loads at Orbituary a moment later; the nearest platform line in
        // time was zzoega's, so the delivery (and the map) went to him
        var w = GameLogWriter.MissionId(71);
        var zzatvo = new GameLogWriter().Login("zzatvo").Accepted(w)
            .Marker(w, GameLogWriter.DirectContract, "dropoff_w_0", "900000007101", GameLogWriter.Gaslight)
            .Minutes(10).LoadingPlatform().Seconds(6).Objective(w, "dropoff_w_0", "COMPLETED");
        var zzoega = new GameLogWriter().Login("zzoega").Minutes(10).Seconds(4).LoadingPlatform();   // 2 seconds before the delivery: nearer in time
        var log = HaulingLog.Merge(new[] { zzatvo.Read(), zzoega.Read() });
        var step = Assert.Single(WorkerTripBuilder.OutSteps(log));
        Assert.Equal("zzatvo", step.Worker);
        Assert.Equal("zzoega", WorkerTripBuilder.ActorAt(log, false, step.Time));   // without the holders it would be the nearer one
    }

    [Fact]
    public void Of_two_holders_at_loading_platforms_at_the_same_moment_the_one_whose_jumps_had_him_at_the_station_did_it()
    {
        // a shared waste contract (both hold it): zzatvo jumped to Gaslight and delivers there, zzoega jumped to Patch City and loads there a moment later;
        // the nearer platform line in time is zzoega's, but his own jumps had him at Patch City, and zzatvo's had him at Gaslight (Frank, 30 Oct 2026)
        var w = GameLogWriter.MissionId(72);
        var waste = string.Format(GameLogWriter.WasteContract, 2);
        var zzatvo = new GameLogWriter().Login("zzatvo").Accepted(w)
            .Marker(w, waste, "dropoff_w_0", "900000007201", GameLogWriter.Gaslight)
            .Minutes(1).QuantumSelected("rs_ext_pyro5_l2").Minutes(5).QuantumArrived()
            .Minutes(4).LoadingPlatform().Seconds(6).Objective(w, "dropoff_w_0", "COMPLETED");
        var zzoega = new GameLogWriter().Login("zzoega").Accepted(w)
            .Marker(w, waste, "dropoff_w_0", "900000007201", GameLogWriter.Gaslight)
            .Minutes(1).QuantumSelected("rs_ext_pyro3_l3").Minutes(5).QuantumArrived()
            .Minutes(4).Seconds(4).LoadingPlatform().Seconds(2).Objective(w, "dropoff_w_0", "COMPLETED");
        var log = HaulingLog.Merge(new[] { zzatvo.Read(), zzoega.Read() });
        var step = Assert.Single(WorkerTripBuilder.OutSteps(log));
        Assert.Equal("zzatvo", step.Worker);
        Assert.Equal("zzoega", WorkerTripBuilder.ActorAt(log, false, step.Time, step.Mission.Holders));   // without the station it would be the nearer one
    }
}
