using SCTools.Tests.Support;

namespace SCTools.Tests.Sessions;

/// <summary>
/// The sessions that start and stop by themselves: one starts when a mission is accepted, goes on while anything happens with the missions, and after an
/// hour with nothing ended 5 minutes after the last thing.
/// </summary>
public class AutoSessionsTests
{
    static DateTime At(int hour, int minute) => new(2026, 10, 28, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void A_session_starts_with_an_accept_and_ends_five_minutes_after_the_last_thing_once_an_hour_went_by()
    {
        var starts = new[] { At(10, 0), At(10, 20), At(12, 30) };
        var activity = new[] { At(10, 30), At(10, 50), At(12, 40) };

        var later = AutoSessions.Find(starts, activity, At(14, 0));
        Assert.Equal(new[]
        {
            new AutoSessions.Window(At(10, 0), At(10, 55), At(10, 50)),
            new AutoSessions.Window(At(12, 30), At(12, 45), At(12, 40)),   // more than an hour after 10:50: a session of its own
        }, later);

        var meanwhile = AutoSessions.Find(starts, activity, At(13, 0));
        Assert.Null(meanwhile[^1].Stop);   // it runs: less than an hour since 12:40
        Assert.Empty(AutoSessions.Find(Array.Empty<DateTime>(), activity, At(14, 0)));   // nothing accepted: no session
    }

    [Fact]
    public void The_hauling_sessions_are_kept_go_on_when_a_late_log_shows_more_and_never_get_shorter()
    {
        var sessions = new List<ManualSession> { new() { StartUtc = At(9, 0), StopUtc = At(9, 30) } };   // stopped by hand: it stays
        var starts = new List<DateTime> { At(8, 50), At(10, 0) };
        var activity = new List<DateTime> { At(10, 40) };

        Assert.True(AutoSessions.Apply(sessions, starts, activity, At(12, 0)));
        Assert.Equal(2, sessions.Count);
        Assert.Equal((At(9, 0), At(9, 30), false), (sessions[0].StartUtc, sessions[0].StopUtc!.Value, sessions[0].Auto));
        Assert.Equal((At(10, 0), At(10, 45), true), (sessions[1].StartUtc, sessions[1].StopUtc!.Value, sessions[1].Auto));
        Assert.False(AutoSessions.Apply(sessions, starts, activity, At(12, 1)));   // nothing new

        activity.Add(At(11, 20));   // a tracker that was off sends its log: the session did not end
        Assert.True(AutoSessions.Apply(sessions, starts, activity, At(12, 0)));
        Assert.Null(sessions[1].StopUtc);
        Assert.True(AutoSessions.Apply(sessions, starts, activity, At(12, 30)));
        Assert.Equal(At(11, 25), sessions[1].StopUtc);

        Assert.False(AutoSessions.Apply(sessions, Array.Empty<DateTime>(), Array.Empty<DateTime>(), At(20, 0)));   // its logs are gone: it stays as it was
        Assert.Equal(At(11, 25), sessions[1].StopUtc);
    }

    [Fact]
    public void A_session_started_by_hand_and_left_running_stops_by_itself()
    {
        var sessions = new List<ManualSession> { new() { StartUtc = At(0, 32) } };
        Assert.True(AutoSessions.Apply(sessions, Array.Empty<DateTime>(), Array.Empty<DateTime>(), At(5, 0)));
        Assert.Equal((At(0, 37), true), (sessions[0].StopUtc!.Value, sessions[0].Auto));
    }

    [Fact]
    public void A_hauling_contract_is_what_starts_a_session_and_its_pickups_keep_it_going()
    {
        var m1 = GameLogWriter.MissionId(1);
        var log = new GameLogWriter().At(At(10, 0)).Login("zzoega").Accepted(m1)
            .Marker(m1, string.Format(GameLogWriter.HaulingContract, 2), "pickup_aaaa_0", "900000000101", GameLogWriter.Gaslight)
            .Minutes(40).Objective(m1, "pickup_aaaa_0", "COMPLETED")
            .Read();
        var (starts, activity) = AutoSessions.HaulingActivity(log);
        Assert.Equal(new[] { At(10, 0) }, starts);
        Assert.Contains(At(10, 40), activity);
    }

    [Fact]
    public void A_contract_still_going_keeps_the_session_alive_while_somebody_jumps_around()
    {
        // 29 Oct 2026: long routes, nothing with the contracts themselves for over an hour, nine contracts going: the session ended under the party.
        // The jumps count while a contract is going; once every contract ended, they count for nothing
        var m1 = GameLogWriter.MissionId(21);
        GameLogWriter Hauling(bool ended)
        {
            var w = new GameLogWriter().At(At(10, 0)).Login("zzoega").Accepted(m1)
                .Marker(m1, string.Format(GameLogWriter.HaulingContract, 2), "pickup_bbbb_0", "900000000201", GameLogWriter.Gaslight)
                .Minutes(40).Objective(m1, "pickup_bbbb_0", "COMPLETED");
            if (ended) w = w.Minutes(10).MissionEnded(m1, "COMPLETED");
            for (var i = 0; i < 6; i++) w = w.Minutes(ended && i == 0 ? 15 : 25).QuantumSelected("Gaslight").Minutes(5).QuantumArrived();   // flying until 13:40
            return w;
        }
        var (starts, activity) = AutoSessions.HaulingActivity(Hauling(false).Read());
        Assert.Contains(At(11, 5), activity);   // a jump while the contract is going
        Assert.Null(Assert.Single(AutoSessions.Find(starts, activity, At(14, 0))).Stop);   // still running at 14:00: the last jump was at 13:40
        var (done, doneActivity) = AutoSessions.HaulingActivity(Hauling(true).Read());
        Assert.DoesNotContain(At(11, 5), doneActivity);
        Assert.Equal(At(10, 55), Assert.Single(AutoSessions.Find(done, doneActivity, At(14, 0))).Stop);   // ended 5 minutes after the hand-in at 10:50
    }
}
