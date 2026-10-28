using System.Globalization;

namespace SCTools.Tracking;

/// <summary>One row of a leaderboard before it is turned into text.</summary>
public sealed class StatLine
{
    public string Name = "";
    public int Sessions;
    public WorkerStat S = new();
    public decimal RunValue;    // value of the finished runs
    public decimal Confirmed;   // the part whose missions are completed (earned)
    public decimal Lost;        // run value that was never paid, once the session is over
    public double Seconds;      // hours worked, for aUEC/hour
}

/// <summary>A row of a leaderboard as it is shown: the texts of its cells, and whether it is the top earner or the Total row.</summary>
public sealed record BoardRow(
    string Name, string Sessions, string Runs, string Perfect, string Delivered, string AvgBatches, string AvgTime, string Distance, string GmPerHour,
    string RunValue, string Earned, string Lost, string Share, string ValuePerHour, bool Top, bool Total);

/// <summary>A saved session on the History tab.</summary>
public sealed record HistoryRow(string Date, string Time, int Missions, string Revenue, string Carried);

/// <summary>The leaderboards: the one of the session shown (Session tab) and the all-time one over every saved session (History tab), and the saved sessions.</summary>
public sealed class Leaderboard
{
    /// <summary>After this long without activity the unpaid part of a session counts as lost (missions can take hours).</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromHours(6);

    readonly TrackerState _s;
    readonly Places _places;
    readonly Payouts _payouts;
    readonly SessionFigures _figures;

    public Leaderboard(TrackerState state, Places places, Payouts payouts, SessionFigures figures)
    {
        _s = state;
        _places = places;
        _payouts = payouts;
        _figures = figures;
    }

    /// <summary>Runs and missions per worker. Completed runs are the ones ended by a hand-in (not by hand), perfect runs are completed runs with lost cargo None.</summary>
    public static Dictionary<string, WorkerStat> ComputeStats(IEnumerable<WorkerTrip> trips, SessionInputs inp, Func<string, string?>? stationOf = null, HashSet<string>? completed = null)
    {
        var result = new Dictionary<string, WorkerStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in trips)
        {
            if (t.Worker == "Unassigned") continue;
            if (!result.TryGetValue(t.Worker, out var s)) result[t.Worker] = s = new WorkerStat();
            var missions = t.Pickups.Sum(p => p.Missions);
            if (!t.End.HasValue || CargoRuns.RunFailed(t, inp) || t.Pickups.Count == 0) continue;   // nothing counts until the run is done, a failed run never counts, and a run with only waste in it (no cargo picked up) is no run of the board
            if (completed != null && !t.Pickups.All(p => p.MissionIds.All(completed.Contains))) continue;   // saved stats: only when the missions are completed
            s.Missions += missions;
            s.Runs++;
            if (CargoRuns.LostLevelOf(inp, t.Key) == "None") s.Perfect++;
            s.RunMissions += missions;
            s.RunSeconds += (t.End.Value - t.Start).TotalSeconds;
            if (stationOf != null) s.Distance += CargoRuns.RunDistance(t, stationOf);
        }
        return result;
    }

    /// <summary>The same stats for every worker.</summary>
    public static bool SameStats(Dictionary<string, WorkerStat> a, Dictionary<string, WorkerStat> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var o) && o == kv.Value);

    /// <summary>The leaderboard of the session shown: every worker who started a cargo run this session, and the hours of the session. Empty while no session is shown.</summary>
    public List<StatLine> SessionLines(out double seconds)
    {
        seconds = 0;
        var inp = _s.Inputs;
        var current = _s.Current;
        if (inp == null || current == null) return new List<StatLine>();

        var carried = _payouts.ComputeCarried(current, inp);
        var confirmed = _payouts.ComputeCarried(current, inp, CarryMode.Confirmed);
        var failedNow = _payouts.ComputeCarried(current, inp, CarryMode.Failed);
        var settled = _s.Summary?.End is DateTime lastActivity && DateTime.UtcNow - lastActivity > SettleTime;   // still running: the rest is only queued

        var hours = _figures.Elapsed()?.TotalSeconds ?? 0;
        seconds = hours;

        var stats = ComputeStats(_s.Trips, inp, _places.StationOf);
        // only workers who started a cargo run this session
        var names = new List<string>();
        foreach (var t in _s.Trips)
        {
            if (t.Worker != "Unassigned" && !names.Any(x => SameName(x, t.Worker))) names.Add(t.Worker);
        }

        var lines = names.Select(n => new StatLine
        {
            Name = n,
            Sessions = 1,
            S = stats.FirstOrDefault(kv => SameName(kv.Key, n)).Value ?? new WorkerStat(),
            RunValue = Payouts.CarriedBy(carried, n),
            Confirmed = Payouts.CarriedBy(confirmed, n),
            Seconds = hours,
        }).ToList();
        foreach (var l in lines) l.Lost = settled ? Math.Max(0, l.RunValue - l.Confirmed) : Payouts.CarriedBy(failedNow, l.Name);
        return lines;
    }

    /// <summary>The leaderboard over every saved session (History tab), with how many sessions that is and their hours together.</summary>
    public List<StatLine> AllTimeLines(out int sessions, out double totalSeconds)
    {
        var lines = new Dictionary<string, StatLine>(StringComparer.OrdinalIgnoreCase);
        StatLine Line(string name)
        {
            if (!lines.TryGetValue(name, out var l)) lines[name] = l = new StatLine { Name = name };
            return l;
        }
        foreach (var p in _s.Data.Party) Line(p);

        sessions = 0;
        totalSeconds = 0;
        foreach (var s in _s.Data.Sessions.Values.Where(x => x.Carried.Count > 0 || x.RunValues.Count > 0))
        {
            sessions++;
            var seconds = s.StartUtc.HasValue && s.EndUtc.HasValue ? Math.Max(0, (s.EndUtc.Value - s.StartUtc.Value).TotalSeconds) : 0;
            totalSeconds += seconds;

            foreach (var n in s.Members.Concat(s.Carried.Keys).Concat(s.Stats.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var l = Line(n);
                l.Sessions++;
                l.Seconds += seconds;
            }
            var settled = !s.EndUtc.HasValue || DateTime.UtcNow - s.EndUtc.Value > SettleTime;
            foreach (var n in s.Carried.Keys.Concat(s.RunValues.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var earned = Payouts.CarriedBy(s.Carried, n);
                var run = s.RunValues.Count > 0 ? Payouts.CarriedBy(s.RunValues, n) : earned;   // sessions saved by older versions only know the earned part
                var l = Line(n);
                l.RunValue += run;
                l.Confirmed += earned;
                l.Lost += settled ? Math.Max(0, run - earned) : Payouts.CarriedBy(s.FailedValues, n);
            }
            foreach (var kv in s.Stats)
            {
                var l = Line(kv.Key);
                l.S.Runs += kv.Value.Runs;
                l.S.Perfect += kv.Value.Perfect;
                l.S.Missions += kv.Value.Missions;
                l.S.RunMissions += kv.Value.RunMissions;
                l.S.RunSeconds += kv.Value.RunSeconds;
                l.S.Distance += kv.Value.Distance;
            }
        }
        return lines.Values.ToList();
    }

    /// <summary>The rows of a leaderboard: the highest earner on top (marked with a star), the Total row last. Nobody on it: no rows at all (no Total of zeros).</summary>
    public static List<BoardRow> Board(List<StatLine> lines, int totalSessions, double totalSeconds)
    {
        var rows = new List<BoardRow>();
        if (lines.Count == 0) return rows;
        var totalValue = lines.Sum(l => l.RunValue);
        var sum = new StatLine();
        var first = true;
        foreach (var l in lines.OrderByDescending(l => l.RunValue).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            var avgTime = "";
            var avgMissions = "";
            if (l.S.Runs > 0)
            {
                avgTime = LongDuration(TimeSpan.FromSeconds(l.S.RunSeconds / l.S.Runs));
                avgMissions = ((double)l.S.RunMissions / l.S.Runs).ToString("0.0", CultureInfo.InvariantCulture);
            }
            var share = totalValue > 0 && l.RunValue > 0
                ? (l.RunValue / totalValue * 100m).ToString("0", CultureInfo.InvariantCulture) + "%"
                : "";
            var top = first && l.RunValue > 0;   // the top earner: a star and a gold row
            if (top) first = false;
            rows.Add(new BoardRow(top ? "★ " + l.Name + " ★" : l.Name, Count(l.Sessions), Count(l.S.Runs), Count(l.S.Perfect), Count(l.S.Missions), avgMissions, avgTime,
                Dist(l.S.Distance), GmPerHour(l.S.Distance, l.Seconds), Num(l.RunValue), Num(l.Confirmed), Num(l.Lost), share, PerHour(l.RunValue, l.Seconds), top, false));

            sum.S.Runs += l.S.Runs;
            sum.S.Perfect += l.S.Perfect;
            sum.S.Missions += l.S.Missions;
            sum.S.RunMissions += l.S.RunMissions;
            sum.S.RunSeconds += l.S.RunSeconds;
            sum.S.Distance += l.S.Distance;
            sum.RunValue += l.RunValue;
            sum.Confirmed += l.Confirmed;
            sum.Lost += l.Lost;
        }
        rows.Add(new BoardRow("Total", Count(totalSessions), Count(sum.S.Runs), Count(sum.S.Perfect), Count(sum.S.Missions), "", "", Dist(sum.S.Distance),
            GmPerHour(sum.S.Distance, totalSeconds), Num(sum.RunValue), Num(sum.Confirmed), Num(sum.Lost), sum.RunValue > 0 ? "100%" : "", PerHour(sum.RunValue, totalSeconds), false, true));
        return rows;
    }

    static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ the saved sessions (History tab)

    /// <summary>The saved sessions with what they earned, newest first.</summary>
    List<SessionInputs> Saved() => _s.Data.Sessions.Values
        .Where(s => s.Carried.Count > 0 && s.StartUtc.HasValue)
        .OrderByDescending(s => s.StartUtc)
        .ToList();

    /// <summary>The rows of Saved Sessions: date, time, missions, revenue and what every worker carried (the Total row is not in it).</summary>
    public List<HistoryRow> HistoryRows()
    {
        var rows = new List<HistoryRow>();
        foreach (var s in Saved())
        {
            var start = s.StartUtc!.Value.ToLocalTime();
            var time = start.ToString("HH:mm", CultureInfo.InvariantCulture)
                + (s.EndUtc.HasValue ? " to " + s.EndUtc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : "");
            var carried = string.Join(",  ", s.Carried.OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key + " " + kv.Value.ToString("N0", CultureInfo.InvariantCulture)));
            rows.Add(new HistoryRow(start.ToString("d MMM yyyy", CultureInfo.CurrentCulture), time, s.MissionCount, s.Revenue.ToString("N0", CultureInfo.InvariantCulture), carried));
        }
        return rows;
    }

    /// <summary>The missions and the revenue of every saved session together.</summary>
    public (int Missions, decimal Revenue) HistoryTotals()
    {
        var saved = Saved();
        return (saved.Sum(s => s.MissionCount), saved.Sum(s => s.Revenue));
    }

    /// <summary>The best session (most revenue) and the best aUEC per hour, in one line.</summary>
    public string BestLine()
    {
        var saved = Saved();
        var best = saved.OrderByDescending(x => x.Revenue).FirstOrDefault();
        var bestHour = saved.Where(x => x.StartUtc.HasValue && x.EndUtc.HasValue && x.EndUtc > x.StartUtc)
            .OrderByDescending(x => x.Revenue / (decimal)(x.EndUtc!.Value - x.StartUtc!.Value).TotalHours).FirstOrDefault();
        var parts = new List<string>();
        if (best != null)
            parts.Add("Best session: " + best.StartUtc!.Value.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture) + ", "
                + best.Revenue.ToString("N0", CultureInfo.InvariantCulture) + " aUEC");
        if (bestHour != null)
            parts.Add("Best aUEC/hour: " + bestHour.StartUtc!.Value.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture) + ", "
                + (bestHour.Revenue / (decimal)(bestHour.EndUtc!.Value - bestHour.StartUtc!.Value).TotalHours).ToString("N0", CultureInfo.InvariantCulture));
        return string.Join("      ", parts);
    }
}
