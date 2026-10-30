namespace SCTools.Gauntlet;

/// <summary>The sums of a set of runs: what they earned and how long they took.</summary>
public sealed record GauntletTotals(int Completed, int Cancelled, long Auec, long Scrip, double ScripAuec)
{
    public double TotalAuec => Auec + ScripAuec;
}

/// <summary>A grind session or the game session: the totals and the rates per hour.</summary>
public sealed record GauntletSessionStats(DateTime Start, DateTime End, GauntletTotals Totals)
{
    public double Hours => Math.Max((End - Start).TotalHours, 0);
    public double AuecPerHour => Hours > 0 ? Totals.Auec / Hours : 0;
    public double ScripPerHour => Hours > 0 ? Totals.Scrip / Hours : 0;
    public double TotalPerHour => Hours > 0 ? Totals.TotalAuec / Hours : 0;
}

/// <summary>One scenario: its counts, timings, top ten and what it earns per hour.</summary>
public sealed record GauntletScenarioStats(
    int Scenario, GauntletReward Reward, int Completed, int Cancelled, double? Average, double? Fastest, double? Slowest,
    IReadOnlyList<GauntletRun> Top, double? TotalPerHourAtAverage, double? TotalPerHourAtFastest, double LifetimeAuec, double LifetimeTotal)
{
    /// <summary>What it earns per hour at its average time, apart: the aUEC alone, and the MG scrip alone (null without a timed run).</summary>
    public double? AuecPerHourAtAverage => Average > 0 ? Reward.Auec / Average * 3600 : null;
    public double? ScripPerHourAtAverage => Average > 0 ? Reward.Scrip / Average * 3600 : null;
}

/// <summary>Works out the numbers of the Gauntlet Tracker from the runs, the rewards and what a MG scrip is worth in aUEC.</summary>
public static class GauntletStats
{
    public static GauntletTotals Totals(IEnumerable<GauntletRun> runs, IReadOnlyDictionary<int, GauntletReward>? rewards, double scripValue)
    {
        int done = 0, cancelled = 0;
        long auec = 0, scrip = 0;
        foreach (var r in runs)
        {
            if (r.Outcome != GauntletOutcome.Complete) { cancelled++; continue; }   // a cancelled mission never earns anything
            var w = GauntletRewards.Of(r.Scenario, rewards);
            done++;
            auec += w.Auec;
            scrip += w.Scrip;
        }
        return new GauntletTotals(done, cancelled, auec, scrip, scrip * scripValue);
    }

    /// <summary>The runs that ended between start and end.</summary>
    public static GauntletSessionStats Session(IEnumerable<GauntletRun> runs, DateTime start, DateTime end,
        IReadOnlyDictionary<int, GauntletReward>? rewards, double scripValue) =>
        new(start, end, Totals(runs.Where(r => r.EndedUtc >= start && r.EndedUtc <= end), rewards, scripValue));

    public static IReadOnlyList<GauntletScenarioStats> Scenarios(IReadOnlyCollection<GauntletRun> runs, IReadOnlyDictionary<int, GauntletReward>? rewards, double scripValue)
    {
        var list = new List<GauntletScenarioStats>();
        for (var n = 1; n <= GauntletRewards.Scenarios; n++)
        {
            var mine = runs.Where(r => r.Scenario == n).ToList();
            var reward = GauntletRewards.Of(n, rewards);
            var totals = Totals(mine, rewards, scripValue);
            var timed = mine.Where(r => r.TimingSec != null).OrderBy(r => r.TimingSec).ToList();
            double? avg = timed.Count > 0 ? timed.Average(r => r.TimingSec!.Value) : null;
            double? fastest = timed.Count > 0 ? timed[0].TimingSec : null;
            double? slowest = timed.Count > 0 ? timed[^1].TimingSec : null;
            var each = reward.Auec + reward.Scrip * scripValue;
            list.Add(new GauntletScenarioStats(n, reward, totals.Completed, totals.Cancelled, avg, fastest, slowest,
                timed.Take(10).ToList(), avg > 0 ? each / avg * 3600 : null, fastest > 0 ? each / fastest * 3600 : null, totals.Auec, totals.TotalAuec));
        }
        return list;
    }

    /// <summary>The scenarios that have a timing, the best earner per hour first.</summary>
    public static IReadOnlyList<GauntletScenarioStats> Ranking(IEnumerable<GauntletScenarioStats> scenarios) =>
        scenarios.Where(s => s.TotalPerHourAtAverage != null).OrderByDescending(s => s.TotalPerHourAtAverage).ToList();
}

/// <summary>
/// One player on the leaderboard: his lifetime earnings, his total aUEC per hour of the runs that were timed (the rewards of those runs over the time they
/// took, so a shared mission without a start does not make him look faster), and a few records.
/// </summary>
/// <param name="Combined">one player's accounts added up ("zzoega + zzatvo"), after the players (Frank, 30 Oct 2026)</param>
public sealed record GauntletLeader(
    string Player, int Completed, int Cancelled, long Auec, long Scrip, double Total, double TimedHours, double? TotalPerHour,
    GauntletRun? Fastest, int? Favourite, int? BestScenario, double? BestScenarioPerHour, DateTime? LastPlayed, bool Combined = false)
{
    /// <summary>The share of his missions he completed (null without missions).</summary>
    public double? CompletionRate => Completed + Cancelled > 0 ? (double)Completed / (Completed + Cancelled) : null;
}

public static class GauntletLeaderboard
{
    /// <summary>Every player with a gauntlet mission, the most earned first; then one player's accounts added up (combined: AppData.CombinedAccounts), when two or more of them are on it.</summary>
    public static IReadOnlyList<GauntletLeader> Make(IReadOnlyDictionary<string, List<GauntletRun>> runsByPlayer,
        IReadOnlyDictionary<int, GauntletReward>? rewards, double scripValue, IReadOnlyList<IReadOnlyList<string>>? combined = null)
    {
        var list = new List<GauntletLeader>();
        foreach (var (player, runs) in runsByPlayer)
        {
            if (runs.Count == 0) continue;
            list.Add(Of(player, runs, rewards, scripValue, false));
        }
        var board = list.OrderByDescending(l => l.Total).ThenBy(l => l.Player, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var group in combined ?? Array.Empty<IReadOnlyList<string>>())
        {
            var names = group.Where(n => runsByPlayer.Any(kv => string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase) && kv.Value.Count > 0)).ToList();
            if (names.Count < 2) continue;
            var runs = names.SelectMany(n => runsByPlayer.First(kv => string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase)).Value).ToList();
            board.Add(Of(string.Join(" + ", names), runs, rewards, scripValue, true));
        }
        return board;
    }

    /// <summary>One line of the leaderboard: a player's runs, or the runs of one player's accounts together.</summary>
    static GauntletLeader Of(string player, List<GauntletRun> runs, IReadOnlyDictionary<int, GauntletReward>? rewards, double scripValue, bool combined)
    {
        {
            var totals = GauntletStats.Totals(runs, rewards, scripValue);
            var timed = runs.Where(r => r.TimingSec != null).ToList();
            var hours = timed.Sum(r => r.TimingSec!.Value) / 3600;
            double Worth(GauntletRun r)
            {
                var w = GauntletRewards.Of(r.Scenario, rewards);
                return w.Auec + w.Scrip * scripValue;
            }
            var perHour = hours > 0 ? timed.Sum(Worth) / hours : (double?)null;
            var fastest = timed.OrderBy(r => r.TimingSec).ThenByDescending(r => r.Scenario).FirstOrDefault();
            var favourite = runs.Where(r => r.Outcome == GauntletOutcome.Complete).GroupBy(r => r.Scenario)
                .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).Select(g => (int?)g.Key).FirstOrDefault();
            var best = GauntletStats.Ranking(GauntletStats.Scenarios(runs, rewards, scripValue)).FirstOrDefault();
            return new GauntletLeader(player, totals.Completed, totals.Cancelled, totals.Auec, totals.Scrip, totals.TotalAuec, hours, perHour,
                fastest, favourite, best?.Scenario, best?.TotalPerHourAtAverage, runs.Max(r => r.EndedUtc), combined);
        }
    }
}
