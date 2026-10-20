using System.Globalization;

namespace HaulingTracker;

// The session leaderboard (between the pickup board and the run board) and the all-time leaderboard on the History tab.
public sealed partial class MainForm
{
    readonly DataGridView _statsGrid = new();
    readonly DataGridView _allTimeGrid = new();
    static Color TopGold => Theme.Gold;
    static readonly TimeSpan SettleTime = TimeSpan.FromHours(6);   // after this long without activity the unpaid part of a session counts as lost (missions can take hours)

    /// <summary>One row of a leaderboard before it is turned into text.</summary>
    sealed class StatLine
    {
        public string Name = "";
        public int Sessions;
        public WorkerStat S = new();
        public decimal RunValue;    // value of the finished runs
        public decimal Confirmed;   // the part whose missions are completed (earned)
        public decimal Lost;        // run value that was never paid, once the session is over
        public double Seconds;      // hours worked, for aUEC/hour
    }

    void BuildStatColumns(DataGridView grid, bool withSessions)
    {
        grid.Name = "wrapheader";
        StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.Columns.Add(Col("Worker", 190, true));   // as wide as Pickup Location above
        if (withSessions) grid.Columns.Add(Col("Sessions", 80, true));
        grid.Columns.Add(Col("Completed Runs", 100, true));
        grid.Columns.Add(Col("Perfect Runs", 90, true));
        grid.Columns.Add(Col("Delivered Batches", 105, true));
        grid.Columns.Add(Col("Avg Batches/Run", 100, true));
        grid.Columns.Add(Col("Avg Run Time", 90, true));
        grid.Columns.Add(Col("Distance Travelled", 105, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("Gm / Hour", 80, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("Total Run Value", 105, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("Total Earned aUEC", 105, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("Lost Value", 90, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("Share of Run Value", 100, true));
        grid.Columns.Add(Col("Run Value / Hour", 110, true, DataGridViewContentAlignment.MiddleRight, true));
    }

    void BuildStatsGrid()
    {
        BuildStatColumns(_statsGrid, false);
        AddNumberColumn(_statsGrid);   // the session leaderboard counts its workers: 1, 2, 3...
    }

    static string ShortDuration(TimeSpan d) => (int)d.TotalHours + ":" + d.Minutes.ToString("00");

    static string Num(decimal v) => v > 0 ? v.ToString("N0", CultureInfo.InvariantCulture) : "";

    static string Dist(double gm) => gm > 0 ? gm.ToString("N1", CultureInfo.InvariantCulture) + " Gm" : "";

    /// <summary>The station of a pickup location: by its location id, or by the name you gave it. Null when the distance table does not know it.</summary>
    string? StationOf(string zone) => PyroDistances.IdOfTail(Tail(zone)) ?? PyroDistances.IdOfName(LocationName(zone));

    /// <summary>Gm flown in one run: Ruin, the pickup stations in the order they were visited, and back to Ruin. Locations missing from the table are skipped.</summary>
    static double RunDistance(WorkerTrip t, Func<string, string?> stationOf) =>
        PyroDistances.Run(t.Pickups.OrderBy(p => p.Time).Select(p => stationOf(p.Zone)).OfType<string>());

    static string GmPerHour(double gm, double seconds) =>
        gm > 0 && seconds > 0 ? (gm / (seconds / 3600.0)).ToString("N1", CultureInfo.InvariantCulture) : "";

    static string PerHour(decimal earned, double seconds) =>
        earned > 0 && seconds > 0 ? Num(earned / (decimal)(seconds / 3600.0)) : "";

    /// <summary>Runs and missions per worker. Completed runs are the ones ended by a hand-in (not by hand), perfect runs are completed runs with lost cargo None.</summary>
    static Dictionary<string, WorkerStat> ComputeStats(IEnumerable<WorkerTrip> trips, SessionInputs inp, Func<string, string?>? stationOf = null, HashSet<string>? completed = null)
    {
        var result = new Dictionary<string, WorkerStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in trips)
        {
            if (t.Worker == "Unassigned") continue;
            if (!result.TryGetValue(t.Worker, out var s)) result[t.Worker] = s = new WorkerStat();
            var missions = t.Pickups.Sum(p => p.Missions);
            if (!t.End.HasValue || RunFailed(t, inp)) continue;   // nothing counts until the run is done, and a failed run never counts
            if (completed != null && !t.Pickups.All(p => p.MissionIds.All(completed.Contains))) continue;   // saved stats: only when the missions are completed
            s.Missions += missions;
            s.Runs++;
            if (LostLevelOf(inp, t.Key) == "None") s.Perfect++;
            s.RunMissions += missions;
            s.RunSeconds += (t.End.Value - t.Start).TotalSeconds;
            if (stationOf != null) s.Distance += RunDistance(t, stationOf);
        }
        return result;
    }

    /// <summary>A run is failed when it was ended by hand or all of its cargo was lost.</summary>
    static bool RunFailed(WorkerTrip t, SessionInputs inp) => t.Manual || LostLevelOf(inp, t.Key) == "All";

    static bool SameStats(Dictionary<string, WorkerStat> a, Dictionary<string, WorkerStat> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var o) && o == kv.Value);

    /// <summary>The leaderboard: the highest earner on top, the total row last.</summary>
    static void WriteBoard(DataGridView grid, List<StatLine> lines, bool withSessions, int totalSessions, double totalSeconds)
    {
        grid.Rows.Clear();
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

            var cells = new List<object> { l.Name };
            if (withSessions) cells.Add(l.Sessions);
            cells.AddRange(new object[] { l.S.Runs, l.S.Perfect, l.S.Missions, avgMissions, avgTime, Dist(l.S.Distance), GmPerHour(l.S.Distance, l.Seconds), Num(l.RunValue), Num(l.Confirmed), Num(l.Lost), share, PerHour(l.RunValue, l.Seconds) });
            var idx = grid.Rows.Add(cells.ToArray());
            if (first && l.RunValue > 0)
            {
                // the top earner: a star and a gold row
                grid.Rows[idx].Cells[0].Value = "★ " + l.Name + " ★";
                grid.Rows[idx].DefaultCellStyle.BackColor = TopGold;
                first = false;
            }

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

        var total = new List<object> { "Total" };
        if (withSessions) total.Add(totalSessions);
        total.AddRange(new object[]
        {
            sum.S.Runs, sum.S.Perfect, sum.S.Missions, "", "", Dist(sum.S.Distance), GmPerHour(sum.S.Distance, totalSeconds), Num(sum.RunValue), Num(sum.Confirmed), Num(sum.Lost), sum.RunValue > 0 ? "100%" : "", PerHour(sum.RunValue, totalSeconds),
        });
        var tr = grid.Rows.Add(total.ToArray());
        grid.Rows[tr].DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
    }

    /// <summary>The leaderboard of the selected session.</summary>
    void FillStats()
    {
        _statsGrid.Rows.Clear();
        if (_inputs == null || _current == null) return;

        var carried = ComputeCarried(_current, _inputs);
        var confirmed = ComputeCarried(_current, _inputs, CarryMode.Confirmed);
        var failedNow = ComputeCarried(_current, _inputs, CarryMode.Failed);
        var settled = _summary?.End is DateTime lastActivity && DateTime.UtcNow - lastActivity > SettleTime;   // still running: the rest is only queued

        var seconds = SessionElapsed()?.TotalSeconds ?? 0;

        var stats = ComputeStats(_trips, _inputs, StationOf);
        // only workers who started a cargo run this session
        var names = new List<string>();
        foreach (var t in _trips)
        {
            if (t.Worker != "Unassigned" && !names.Any(x => SameName(x, t.Worker))) names.Add(t.Worker);
        }

        var lines = names.Select(n => new StatLine
        {
            Name = n,
            Sessions = 1,
            S = stats.FirstOrDefault(kv => SameName(kv.Key, n)).Value ?? new WorkerStat(),
            RunValue = CarriedBy(carried, n),
            Confirmed = CarriedBy(confirmed, n),
            Seconds = seconds,
        }).ToList();
        foreach (var l in lines) l.Lost = settled ? Math.Max(0, l.RunValue - l.Confirmed) : CarriedBy(failedNow, l.Name);
        WriteBoard(_statsGrid, lines, false, 1, seconds);
        NumberRows(_statsGrid, lines.Count);   // not the Total row
    }

    /// <summary>The leaderboard over every saved session (History tab).</summary>
    void FillAllTime()
    {
        var lines = new Dictionary<string, StatLine>(StringComparer.OrdinalIgnoreCase);
        StatLine Line(string name)
        {
            if (!lines.TryGetValue(name, out var l)) lines[name] = l = new StatLine { Name = name };
            return l;
        }
        foreach (var p in _data.Party) Line(p);

        var sessions = 0;
        double totalSeconds = 0;
        foreach (var s in _data.Sessions.Values.Where(x => x.Carried.Count > 0 || x.RunValues.Count > 0))
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
                var earned = CarriedBy(s.Carried, n);
                var run = s.RunValues.Count > 0 ? CarriedBy(s.RunValues, n) : earned;   // sessions saved by older versions only know the earned part
                var l = Line(n);
                l.RunValue += run;
                l.Confirmed += earned;
                l.Lost += settled ? Math.Max(0, run - earned) : CarriedBy(s.FailedValues, n);
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
        WriteBoard(_allTimeGrid, lines.Values.ToList(), true, sessions, totalSeconds);
    }
}
