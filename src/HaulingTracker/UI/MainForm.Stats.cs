namespace HaulingTracker.UI;

// The session leaderboard (between the pickup board and the run board) and the all-time leaderboard on the History tab.
public sealed partial class MainForm
{
    readonly DataGridView _statsGrid = new();
    readonly DataGridView _allTimeGrid = new();
    static Color TopGold => Theme.Gold;

    void BuildStatColumns(DataGridView grid, bool withSessions)
    {
        grid.Name = "wrapheader";
        StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.Columns.Add(Col(StatCol.Worker, "Worker", 190, true));   // as wide as Pickup Location above
        if (withSessions) grid.Columns.Add(Col(StatCol.Sessions, "Sessions", 80, true));
        grid.Columns.Add(Col(StatCol.Runs, "Completed Runs", 100, true));
        grid.Columns.Add(Col(StatCol.Perfect, "Perfect Runs", 90, true));
        grid.Columns.Add(Col(StatCol.Delivered, "Delivered Batches", 105, true));
        grid.Columns.Add(Col(StatCol.AvgBatches, "Avg Batches/Run", 100, true));
        grid.Columns.Add(Col(StatCol.AvgTime, "Avg Run Time", 90, true));
        grid.Columns.Add(Col(StatCol.Distance, "Distance Travelled", 105, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col(StatCol.GmPerHour, "Gm / Hour", 80, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col(StatCol.RunValue, "Total Run Value", 105, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col(StatCol.Earned, "Total Earned aUEC", 105, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col(StatCol.Lost, "Lost Value", 90, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col(StatCol.Share, "Share of Run Value", 100, true));
        grid.Columns.Add(Col(StatCol.ValuePerHour, "Run Value / Hour", 110, true, DataGridViewContentAlignment.MiddleRight, true));
    }

    void BuildStatsGrid()
    {
        BuildStatColumns(_statsGrid, false);
        AddNumberColumn(_statsGrid);   // the session leaderboard counts its workers: 1, 2, 3...
    }

    /// <summary>The leaderboard: the highest earner on top (a star and a gold row), the Total row last.</summary>
    static void WriteBoard(DataGridView grid, List<StatLine> lines, bool withSessions, int totalSessions, double totalSeconds)
    {
        grid.Rows.Clear();
        foreach (var b in Leaderboard.Board(lines, totalSessions, totalSeconds))
        {
            var cells = new List<(string, object?)>
            {
                (StatCol.Worker, b.Name),
                (StatCol.Runs, b.Runs),
                (StatCol.Perfect, b.Perfect),
                (StatCol.Delivered, b.Delivered),
                (StatCol.AvgBatches, b.AvgBatches),
                (StatCol.AvgTime, b.AvgTime),
                (StatCol.Distance, b.Distance),
                (StatCol.GmPerHour, b.GmPerHour),
                (StatCol.RunValue, b.RunValue),
                (StatCol.Earned, b.Earned),
                (StatCol.Lost, b.Lost),
                (StatCol.Share, b.Share),
                (StatCol.ValuePerHour, b.ValuePerHour),
            };
            if (withSessions) cells.Add((StatCol.Sessions, b.Sessions));
            var row = grid.AddRow(cells.ToArray());
            if (b.Top) row.DefaultCellStyle.BackColor = TopGold;
            if (b.Total) row.DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        }
    }

    /// <summary>The leaderboard of the selected session.</summary>
    void FillStats()
    {
        _statsGrid.Rows.Clear();
        if (_state.Inputs == null || _state.Current == null) return;
        var lines = _leaders.SessionLines(out var seconds);
        WriteBoard(_statsGrid, lines, false, 1, seconds);
        NumberRows(_statsGrid, lines.Count);   // not the Total row
    }

    /// <summary>The leaderboard over every saved session (History tab).</summary>
    void FillAllTime()
    {
        var lines = _leaders.AllTimeLines(out var sessions, out var totalSeconds);
        WriteBoard(_allTimeGrid, lines, true, sessions, totalSeconds);
    }
}
