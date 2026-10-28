using SCTools.Play;

namespace SCTools.Tests.Play;

/// <summary>The Session tool's sums: the time played in a period (a game that runs over its edge counts only inside it), the games and crashes, the missions and what they earned.</summary>
public class PlayStatsTests
{
    static DateTime U(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Today_this_week_and_this_month_start_at_midnight_in_Sweden()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
        var p = PlayStats.Periods(U(28, 3), zone);   // Wednesday 28 Oct, 04:00 in Sweden (UTC+1 since Sunday)
        Assert.Equal(new[] { "Today", "This week", "This month", "All time" }, p.Select(x => x.Name));
        Assert.Equal(U(27, 23), p[0].FromUtc);   // 28 Oct 00:00 in Sweden
        Assert.Equal(U(25, 23), p[1].FromUtc);   // Monday 26 Oct
        Assert.Equal(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc), p[2].FromUtc);   // 1 Oct, still summer time
        Assert.Equal(DateTime.MinValue, p[3].FromUtc);
    }

    [Fact]
    public void A_period_counts_the_time_inside_it_the_games_that_started_in_it_and_the_missions_that_ended_in_it()
    {
        var games = new[]
        {
            new PlayedGame("a", U(27, 22), U(28, 2), true, false),     // over midnight: two hours of it are on the 28th
            new PlayedGame("b", U(28, 10), U(28, 12), false, false),   // crashed
            new PlayedGame("c", U(28, 13), U(28, 14), null, true),     // running: not a crash
        };
        var missions = new[]
        {
            new EarnedMission(U(27, 23), "x", false, 100_000, null, null),   // before the period
            new EarnedMission(U(28, 1), "x", false, 100_000, 10, null),
            new EarnedMission(U(28, 11), "y", true, null, null, 4),
            new EarnedMission(U(28, 11, 30), "z", false, null, null, null),   // nobody typed in its reward yet
        };
        var t = PlayStats.For(U(28, 0), U(28, 23), games, missions, mgValue: 1_000, councilValue: 2_500);
        Assert.Equal(TimeSpan.FromHours(5), t.Played);   // 2 + 2 + 1
        Assert.Equal((2, 1), (t.Games, t.Crashes));      // b and c started in it; b crashed
        Assert.Equal((3, 1), (t.Completed, t.WithoutReward));
        Assert.Equal((100_000L, 10L, 4L), (t.Auec, t.Mg, t.Council));
        Assert.Equal(100_000 + 10 * 1_000 + 4 * 2_500, t.Value);
        Assert.Equal(t.Value / 5, t.PerHour!.Value, 6);
        Assert.Null(PlayStats.For(U(20, 0), U(21, 0), games, missions, 0, 0).PerHour);   // nothing played: no rate
    }

    [Fact]
    public void An_old_log_says_whether_the_game_was_quit()
    {
        var lines = new[] { "<2026-10-21T08:00:00.000Z> [Notice] <Something> the game runs", "<2026-10-21T10:00:00.000Z> [Notice] <SystemQuit> CSystem::Quit invoked" };
        Assert.True(OldLog.Read(lines).Quit);
        Assert.False(OldLog.Read(lines[..1]).Quit);
        Assert.True(new LineFilter().Keep(lines[1]));   // the tracker sends the quit line
    }
}
