using System.Globalization;

namespace HaulingTracker;

// The personal stats board between the pickup board and the run board.
public sealed partial class MainForm
{
    readonly DataGridView _statsGrid = new();

    void BuildStatsGrid()
    {
        _statsGrid.Dock = DockStyle.Fill;
        StyleGrid(_statsGrid);
        _statsGrid.AllowUserToAddRows = false;
        _statsGrid.AllowUserToDeleteRows = false;
        _statsGrid.Columns.Add(Col("Worker", 140, true, DataGridViewContentAlignment.MiddleLeft));
        _statsGrid.Columns.Add(Col("Avg run time", 110, true));
        _statsGrid.Columns.Add(Col("Avg missions/run", 140, true));
        _statsGrid.Columns.Add(Col("Completed runs", 130, true));
        _statsGrid.Columns.Add(Col("Perfect runs", 110, true));
        _statsGrid.Columns.Add(Col("Missions carried", 140, true));
        _statsGrid.Columns.Add(Col("Earned aUEC", 140, true, DataGridViewContentAlignment.MiddleRight));
        _statsGrid.Columns.Add(Col("Share of value", 120, true));
        _statsGrid.Columns.Add(Col("aUEC/hour", 140, true, DataGridViewContentAlignment.MiddleRight, true));
    }

    static string ShortDuration(TimeSpan d) => (int)d.TotalHours + ":" + d.Minutes.ToString("00");

    /// <summary>
    /// Per worker for the selected session. Completed runs are the ones ended by a hand-in (not by hand),
    /// perfect runs are completed runs with lost cargo None. Earned is the value of the locations he carried.
    /// </summary>
    void FillStats()
    {
        _statsGrid.Rows.Clear();
        if (_inputs == null || _current == null) return;

        var carried = ComputeCarried(_current, _inputs);

        TimeSpan? session = null;
        if (TryTime(_startBox.Text, out var s) && TryTime(_endBox.Text, out var e))
        {
            var d = e - s;
            if (d < TimeSpan.Zero) d += TimeSpan.FromDays(1);
            session = d;
        }

        var names = new List<string>(_data.Party);
        foreach (var t in _trips)
        {
            if (t.Worker != "Unassigned" && !names.Any(n => SameName(n, t.Worker))) names.Add(t.Worker);
        }

        var totalValue = carried.Values.Sum();
        var sumDone = 0;
        var sumPerfect = 0;
        var sumMissions = 0;
        decimal sumEarned = 0;
        foreach (var name in names)
        {
            var mine = _trips.Where(t => SameName(t.Worker, name)).ToList();
            var done = mine.Where(t => t.End.HasValue && !t.Manual).ToList();
            var perfect = done.Count(t => LostLevel(t.Key) == "None");
            var missions = mine.Sum(t => t.Pickups.Sum(p => p.Missions));
            var earned = CarriedBy(carried, name);

            var avgTime = "";
            var avgMissions = "";
            if (done.Count > 0)
            {
                avgTime = ShortDuration(TimeSpan.FromTicks((long)done.Average(t => (double)(t.End!.Value - t.Start).Ticks)));
                avgMissions = done.Average(t => (double)t.Pickups.Sum(p => p.Missions)).ToString("0.0", CultureInfo.InvariantCulture);
            }
            var perHour = session.HasValue && session.Value.TotalHours > 0 && earned > 0
                ? (earned / (decimal)session.Value.TotalHours).ToString("N0", CultureInfo.InvariantCulture)
                : "";

            var share = totalValue > 0 && earned > 0
                ? (earned / totalValue * 100m).ToString("0", CultureInfo.InvariantCulture) + "%"
                : "";

            _statsGrid.Rows.Add(name, avgTime, avgMissions, done.Count, perfect, missions,
                earned > 0 ? earned.ToString("N0", CultureInfo.InvariantCulture) : "", share, perHour);
            sumDone += done.Count;
            sumPerfect += perfect;
            sumMissions += missions;
            sumEarned += earned;
        }

        var totalHour = session.HasValue && session.Value.TotalHours > 0 && sumEarned > 0
            ? (sumEarned / (decimal)session.Value.TotalHours).ToString("N0", CultureInfo.InvariantCulture)
            : "";
        var tr = _statsGrid.Rows.Add("Total", "", "", sumDone, sumPerfect, sumMissions,
            sumEarned > 0 ? sumEarned.ToString("N0", CultureInfo.InvariantCulture) : "", sumEarned > 0 ? "100%" : "", totalHour);
        _statsGrid.Rows[tr].DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
    }
}
