namespace HaulingTracker.Gauntlet;

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
    IReadOnlyList<GauntletRun> Top, double? TotalPerHourAtAverage, double? TotalPerHourAtFastest, double LifetimeAuec, double LifetimeTotal);

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
