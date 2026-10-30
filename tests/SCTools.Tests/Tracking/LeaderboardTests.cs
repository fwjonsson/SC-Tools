using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>The leaderboards of the session and of all time, and the saved sessions.</summary>
public class LeaderboardTests
{
    static StatLine Line(string name, decimal value, int runs, int batches, double hours) => new()
    {
        Name = name,
        Sessions = 1,
        RunValue = value,
        Confirmed = value,
        Seconds = hours * 3600,
        S = new WorkerStat { Runs = runs, Perfect = runs, Missions = batches, RunMissions = batches, RunSeconds = runs * 1800 },
    };

    [Fact]
    public void One_players_accounts_are_added_up_in_a_row_after_the_players()
    {
        // Frank, 30 Oct 2026: zzoega, zzatvo and zzoii are his; the payouts are split between them, so a row adds them up (not numbered, after the players);
        // a group with one account on the board gets no row
        var lines = new List<StatLine> { Line("zzatvo", 100, 1, 2, 1), Line("zzoega", 300, 2, 5, 1), Line("Bileh123", 200, 1, 3, 1) };
        var rows = Leaderboard.Board(lines, 1, 3600, new[] { new[] { "zzoega", "zzatvo", "zzoii" }, new[] { "Bileh123", "nobody" } });
        Assert.Equal(new[] { "★ zzoega ★", "Bileh123", "zzatvo", "zzoega + zzatvo", "Total" }, rows.Select(r => r.Name));
        var both = rows[3];
        Assert.True(both.Combined);
        Assert.False(both.Top);
        Assert.Equal("400", both.RunValue);
        Assert.Equal("3", both.Runs);
        Assert.Equal("67%", both.Share);
        Assert.Equal("2.3", both.AvgBatches);
        Assert.Equal("600", rows[4].RunValue);   // the Total is the players alone
    }

    [Fact]
    public void The_top_earner_gets_a_star_and_the_total_adds_everybody_up()
    {
        var rows = Leaderboard.Board(new List<StatLine> { Line("Pilot_B", 100, 1, 2, 1), Line("Pilot_A", 300, 2, 5, 1) }, 1, 3600);
        Assert.Collection(rows,
            top =>
            {
                Assert.Equal("★ Pilot_A ★", top.Name);
                Assert.True(top.Top);
                Assert.Equal("75%", top.Share);
                Assert.Equal("2.5", top.AvgBatches);
                Assert.Equal("0:30:00", top.AvgTime);
            },
            second =>
            {
                Assert.Equal("Pilot_B", second.Name);
                Assert.False(second.Top);
                Assert.Equal("25%", second.Share);
            },
            total =>
            {
                Assert.True(total.Total);
                Assert.Equal("Total", total.Name);
                Assert.Equal("3", total.Runs);
                Assert.Equal("7", total.Delivered);
                Assert.Equal("400", total.RunValue);
                Assert.Equal("100%", total.Share);
                Assert.Equal("400", total.ValuePerHour);
            });
    }

    [Fact]
    public void Nobody_is_on_top_while_nothing_is_earned()
    {
        var rows = Leaderboard.Board(new List<StatLine> { Line("Pilot_A", 0, 0, 0, 1) }, 1, 3600);
        Assert.DoesNotContain(rows, r => r.Top);
        Assert.Equal("", rows[^1].Share);
    }

    [Fact]
    public void Only_finished_runs_count_and_a_perfect_run_lost_nothing()
    {
        var (t, _) = PlayedSession.Load();
        var inp = t.State.Inputs!;
        var stats = Leaderboard.ComputeStats(t.State.Trips, inp, t.Places.StationOf);
        var s = stats[PlayedSession.Player];
        Assert.Equal((1, 1, 2), (s.Runs, s.Perfect, s.Missions));   // the run on the way does not count yet
        Assert.True(s.Distance > 0);

        var (done, _) = PlayedSession.Runs(t);
        inp.LostCargo[done.Key] = "Little";
        s = Leaderboard.ComputeStats(t.State.Trips, inp)[PlayedSession.Player];
        Assert.Equal((1, 0), (s.Runs, s.Perfect));
        inp.LostCargo[done.Key] = "All";   // a failed run never counts
        Assert.Equal(0, Leaderboard.ComputeStats(t.State.Trips, inp)[PlayedSession.Player].Runs);
    }

    [Fact]
    public void The_session_leaderboard_has_everybody_who_ran()
    {
        var (t, _) = PlayedSession.Load();
        var line = Assert.Single(t.Leaderboard.SessionLines(out var seconds));
        Assert.Equal(PlayedSession.Player, line.Name);
        Assert.Equal(Earnings.DefaultReward2, line.RunValue);    // the finished run
        Assert.Equal(Earnings.DefaultReward2, line.Confirmed);   // its mission is completed
        Assert.Equal(0m, line.Lost);
        Assert.Equal(t.Figures.Elapsed()!.Value.TotalSeconds, seconds);
    }

    [Fact]
    public void The_all_time_leaderboard_adds_up_the_saved_sessions()
    {
        var start = GameLogWriter.Start;
        var data = new AppData { Party = new List<string> { "Pilot_A", "Pilot_B" } };
        data.Sessions["s1"] = new SessionInputs
        {
            StartUtc = start, EndUtc = start.AddHours(2), Members = new List<string> { "Pilot_A", "Pilot_B" },
            Carried = new() { ["Pilot_A"] = 1000, ["Pilot_B"] = 500 }, RunValues = new() { ["Pilot_A"] = 1200, ["Pilot_B"] = 500 },
            Stats = new() { ["Pilot_A"] = new WorkerStat { Runs = 2, Perfect = 1, Missions = 4, RunMissions = 4, RunSeconds = 3000 } },
        };
        data.Sessions["s2"] = new SessionInputs
        {
            StartUtc = start.AddDays(1), EndUtc = start.AddDays(1).AddHours(1), Members = new List<string> { "Pilot_A" },
            Carried = new() { ["Pilot_A"] = 2000 },
        };
        data.Sessions["empty"] = new SessionInputs();   // nothing carried: not a session of the board
        var t = new Tracker(data, _ => { });

        var lines = t.Leaderboard.AllTimeLines(out var sessions, out var totalSeconds);
        Assert.Equal(2, sessions);
        Assert.Equal(3 * 3600, totalSeconds);
        var a = lines.Single(l => l.Name == "Pilot_A");
        Assert.Equal((2, 3200m, 3000m, 200m), (a.Sessions, a.RunValue, a.Confirmed, a.Lost));   // the first session is long over: its unpaid part is lost
        Assert.Equal(2, a.S.Runs);
        Assert.Equal(1, lines.Single(l => l.Name == "Pilot_B").Sessions);
    }

    [Fact]
    public void The_saved_sessions_are_listed_newest_first_with_the_best_ones()
    {
        var start = GameLogWriter.Start;
        var data = new AppData();
        data.Sessions["old"] = new SessionInputs { StartUtc = start, EndUtc = start.AddHours(2), MissionCount = 3, Revenue = 4_000_000m, Carried = new() { ["Pilot_A"] = 1 } };
        data.Sessions["new"] = new SessionInputs { StartUtc = start.AddDays(1), EndUtc = start.AddDays(1).AddHours(1), MissionCount = 2, Revenue = 3_000_000m, Carried = new() { ["Pilot_A"] = 1 } };
        var t = new Tracker(data, _ => { });

        Assert.Equal(new[] { 2, 3 }, t.Leaderboard.HistoryRows().Select(r => r.Missions));
        Assert.Equal((5, 7_000_000m), t.Leaderboard.HistoryTotals());
        var best = t.Leaderboard.BestLine();
        Assert.Contains("Best session: ", best);
        Assert.Contains("4,000,000 aUEC", best);         // the most revenue
        Assert.Contains("Best aUEC/hour: ", best);
        Assert.EndsWith(", 3,000,000", best);            // the newer one earned more per hour
    }
}
