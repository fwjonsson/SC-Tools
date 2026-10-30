using SCTools.Tests.Support;

namespace SCTools.Tests.Runs;

/// <summary>The finished steps of the from-Ruin contracts are worked out once per log and kept until the log reads another line or a mission is marked gone.</summary>
public class OutStepsKeptTests
{
    [Fact]
    public void The_steps_are_kept_until_the_log_changes()
    {
        var log = new HaulingLog();
        foreach (var line in PlayedSession.Writer().Lines) log.ProcessLine(line);
        var first = WorkerTripBuilder.OutSteps(log);
        Assert.Same(first, WorkerTripBuilder.OutSteps(log));   // nothing new: the same list
        var version = log.Version;

        foreach (var line in new GameLogWriter().At(PlayedSession.LastPickup.AddMinutes(1)).Accepted(GameLogWriter.MissionId(77)).Lines) log.ProcessLine(line);
        Assert.NotEqual(version, log.Version);
        Assert.NotSame(first, WorkerTripBuilder.OutSteps(log));   // the log read more: worked out again

        var again = WorkerTripBuilder.OutSteps(log);
        log.Missions.Values.First().Dropped = true;   // a mission marked gone
        Assert.NotSame(again, WorkerTripBuilder.OutSteps(log));
    }
}
