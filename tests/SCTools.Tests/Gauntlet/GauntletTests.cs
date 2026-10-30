using SCTools.Gauntlet;

namespace SCTools.Tests.Gauntlet;

public class GauntletTests
{
    const string A = "11111111-1111-1111-1111-111111111111";
    const string B = "22222222-2222-2222-2222-222222222222";

    static string Notice(string time, string title, string id) =>
        $"<2026-09-30T{time}Z> [Notice] <SHUDEvent_OnNotification> Added notification \"{title}\" [12] to queue. New queue size: 1, MissionId: [{id}], ObjectiveId: [x]";

    static string Accept(string time, int n, string id) => Notice(time, $"Contract Accepted:  Combat Gauntlet - Scenario #{n}: ", id);
    static string Done(string time, int n, string id) => Notice(time, $"Contract Complete:  Combat Gauntlet - Scenario #{n}: ", id);
    static string End(string time, string id, string type) =>
        $"<2026-09-30T{time}Z> [Notice] <EndMission> Ending mission for player. MissionId[{id}] Player[zzoega] PlayerId[1] CompletionType[{type}] Reason[Mission Ended]";

    static GauntletReader Read(params string[] lines)
    {
        var r = new GauntletReader();
        foreach (var l in lines) r.ProcessLine(l);
        return r;
    }

    [Fact]
    public void A_completed_run_has_its_time_from_the_first_accept()
    {
        var r = Read(Accept("18:00:00.000", 7, A), Accept("18:01:00.000", 7, A), Done("18:05:00.000", 7, A), End("18:05:01.000", A, "Complete"));
        var run = Assert.Single(r.Runs);
        Assert.Equal(7, run.Scenario);
        Assert.Equal(GauntletOutcome.Complete, run.Outcome);
        Assert.Equal(300, run.TimingSec);
    }

    [Fact]
    public void Abandoned_and_failed_missions_are_kept_but_earn_nothing()
    {
        var r = Read(Accept("18:00:00.000", 6, A), End("18:02:00.000", A, "Abandon"), Accept("18:03:00.000", 5, B), End("18:04:00.000", B, "Fail"),
            End("18:05:00.000", "33333333-3333-3333-3333-333333333333", "Abandon"));   // never accepted here: leaves nothing
        Assert.Equal(2, r.Runs.Count);
        var totals = GauntletStats.Totals(r.Runs, null, 1000);
        Assert.Equal(0, totals.Completed);
        Assert.Equal(2, totals.Cancelled);
        Assert.Equal(0, totals.TotalAuec);
    }

    [Fact]
    public void A_mission_counts_once_and_a_shared_one_has_no_time()
    {
        var r = Read(Done("18:05:00.000", 7, A), Done("18:05:01.000", 7, A));
        var run = Assert.Single(r.Runs);
        Assert.Null(run.TimingSec);
        Assert.Equal(227_750, GauntletStats.Totals(r.Runs, null, 0).Auec);
    }

    [Fact]
    public void A_run_over_the_limit_is_not_timed_but_still_pays_and_scenario_8_has_an_hour()
    {
        var r = Read(Accept("18:00:00.000", 7, A), Done("18:20:00.000", 7, A), Accept("18:00:00.000", 8, B), Done("18:50:00.000", 8, B));
        Assert.Null(r.Runs[0].TimingSec);
        Assert.Equal(3000, r.Runs[1].TimingSec);
        var totals = GauntletStats.Totals(r.Runs, null, 100);
        Assert.Equal(2, totals.Completed);
        Assert.Equal(227_750 + 349_750, totals.Auec);
        Assert.Equal(36, totals.Scrip);
        Assert.Equal(3600, totals.ScripAuec);
    }

    [Fact]
    public void The_ranking_orders_by_total_per_hour_at_the_average_time()
    {
        var r = Read(Accept("18:00:00.000", 7, A), Done("18:05:00.000", 7, A), Accept("19:00:00.000", 6, B), Done("19:10:00.000", 6, B));
        var scenarios = GauntletStats.Scenarios(r.Runs, null, 0);
        var ranking = GauntletStats.Ranking(scenarios);
        Assert.Equal(new[] { 7, 6 }, ranking.Select(s => s.Scenario));
        Assert.Equal(227_750.0 / 300 * 3600, ranking[0].TotalPerHourAtAverage!.Value);
        Assert.Equal(16.0 / 300 * 3600, ranking[0].ScripPerHourAtAverage!.Value);   // the scrip alone: #7 pays 16
        Assert.Equal(227_750, ranking[0].LifetimeAuec);
    }

    [Fact]
    public void The_uploads_feed_the_reader_and_the_lines_pass_the_tracker_filter()
    {
        var lines = new[] { Accept("18:00:00.000", 7, A), Done("18:05:00.000", 7, A), End("18:05:01.000", A, "Complete") };
        var filter = new LineFilter();
        Assert.All(lines, l => Assert.True(filter.Keep(l)));
        var uploads = new LogUploads();
        uploads.Add("zzoega", "2026-09-30T18_00_00", 0, 100, lines);
        var runs = Assert.Single(uploads.GauntletRuns()["zzoega"]);
        Assert.Equal(A, runs.MissionId);
    }

    [Fact]
    public void The_leaderboard_has_every_player_with_his_earnings_and_his_rate_over_the_timed_runs()
    {
        var zzoega = Read(Accept("18:00:00.000", 7, A), Done("18:05:00.000", 7, A),      // 5 minutes
            Done("18:20:00.000", 8, B),                                                    // shared to him: earns, but has no time
            Accept("18:30:00.000", 6, "33333333-3333-3333-3333-333333333333"), End("18:31:00.000", "33333333-3333-3333-3333-333333333333", "Abandon")).Runs;
        var bileh = Read(Accept("18:00:00.000", 1, A), Done("18:10:00.000", 1, A)).Runs;
        var board = GauntletLeaderboard.Make(new Dictionary<string, List<GauntletRun>> { ["bileh123"] = bileh.ToList(), ["zzoega"] = zzoega.ToList() }, null, 0);

        Assert.Equal(new[] { "zzoega", "bileh123" }, board.Select(l => l.Player));   // the most earned first
        var z = board[0];
        Assert.Equal((2, 1), (z.Completed, z.Cancelled));
        Assert.Equal(227_750 + 349_750, z.Auec);
        Assert.Equal(227_750.0 / (5.0 / 60), z.TotalPerHour!.Value, 6);   // only the timed run: the shared one does not make him faster
        Assert.Equal(7, z.Fastest!.Scenario);
        Assert.Equal(2.0 / 3, z.CompletionRate!.Value, 6);
        Assert.Equal(7, z.BestScenario);
        Assert.Equal(23_000.0 * 6, board[1].TotalPerHour!.Value, 6);
        Assert.Empty(GauntletLeaderboard.Make(new Dictionary<string, List<GauntletRun>> { ["nobody"] = new() }, null, 0));

        // one player's accounts added up, after the players (Frank, 30 Oct 2026)
        var runs = new Dictionary<string, List<GauntletRun>> { ["bileh123"] = bileh.ToList(), ["zzoega"] = zzoega.ToList() };
        var withBoth = GauntletLeaderboard.Make(runs, null, 0, new[] { new[] { "zzoega", "bileh123", "zzoii" } });
        Assert.Equal(new[] { "zzoega", "bileh123", "zzoega + bileh123" }, withBoth.Select(l => l.Player));
        Assert.True(withBoth[2].Combined);
        Assert.Equal(z.Auec + board[1].Auec, withBoth[2].Auec);
        Assert.Equal((3, 1), (withBoth[2].Completed, withBoth[2].Cancelled));
        Assert.Equal(2, GauntletLeaderboard.Make(runs, null, 0, new[] { new[] { "zzoega", "zzoii" } }).Count);   // one of them on the board: no row
    }
}
