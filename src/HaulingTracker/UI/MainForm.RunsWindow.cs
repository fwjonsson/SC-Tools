namespace HaulingTracker.UI;

// Cargo Runs in a window of its own: every run of the session in one long list you can scroll through (the boards on the tabs only have room for the latest ones).
public sealed partial class MainForm
{
    Form? _runsWindow;
    DataGridView? _runsWindowGrid;
    bool _runsCopyQueued;
    bool _runsCopyHooked;

    /// <summary>The title "Cargo Runs" with a small button right of it that opens every run in a window of its own.</summary>
    Control RunsTitle()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Height = TitleHeight };
        var title = SectionTitle("Cargo Runs");
        title.Dock = DockStyle.Left;
        title.Width = TextRenderer.MeasureText(title.Text, title.Font).Width + 10;
        var open = new Button
        {
            Text = "Show all runs",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Height = 24,
            Location = new Point(title.Width + 8, 4),
            Cursor = Cursors.Hand,
        };
        if (Theme.Dark)
        {
            open.FlatStyle = FlatStyle.Flat;
            open.UseVisualStyleBackColor = false;
            open.BackColor = Theme.Button;
            open.ForeColor = Theme.Text;
            open.FlatAppearance.BorderColor = Theme.Lines;
        }
        open.Click += (_, _) => ShowRunsWindow();
        panel.Controls.Add(open);
        panel.Controls.Add(title);
        return panel;
    }

    /// <summary>Opens the window with every run of the session (or brings it to the front). It follows the Cargo Runs board while it is open; editing stays on the Session tab.</summary>
    void ShowRunsWindow()
    {
        if (_runsWindow is { IsDisposed: false })
        {
            if (_runsWindow.WindowState == FormWindowState.Minimized) _runsWindow.WindowState = FormWindowState.Normal;
            _runsWindow.Activate();
            return;
        }

        var grid = new DataGridView();
        StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        // the same columns as the Cargo Runs board, with the same names, in the order they are shown there
        foreach (var c in ShownColumns(_tripGrid))
        {
            var wide = c.Name == RunCol.Places;
            var col = Col(c.Name, c.HeaderText, c.Width, true, c.DefaultCellStyle.Alignment == DataGridViewContentAlignment.NotSet ? DataGridViewContentAlignment.MiddleCenter : c.DefaultCellStyle.Alignment, wide);
            if (wide) col.DefaultCellStyle.WrapMode = DataGridViewTriState.True;   // a long run takes two lines instead of being cut off
            grid.Columns.Add(col);
        }
        FitColumns(grid);
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.ScrollBars = ScrollBars.Vertical;
        ThemeGrid(grid);

        var f = new Form
        {
            Text = "Cargo Runs" + (_current != null ? " – " + _sessionBox.Text : ""),
            Width = Math.Min(1600, Screen.FromControl(this).WorkingArea.Width - 80),
            Height = Math.Min(900, Screen.FromControl(this).WorkingArea.Height - 80),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Icon = Icon,
            ShowInTaskbar = true,
            Padding = new Padding(8),
        };
        f.Controls.Add(grid);
        if (f.IsHandleCreated) Theme.TitleBar(f);
        else f.HandleCreated += (_, _) => Theme.TitleBar(f);
        f.FormClosed += (_, _) =>
        {
            _runsWindow = null;
            _runsWindowGrid = null;
        };
        _runsWindow = f;
        _runsWindowGrid = grid;

        if (!_runsCopyHooked)
        {
            // whatever changes on the board (a new run, a pickup, the clock of a run on the way, Lost Cargo) shows in the window too
            _runsCopyHooked = true;
            _tripGrid.CellValueChanged += (_, _) => QueueRunsCopy();
            _tripGrid.RowsAdded += (_, _) => QueueRunsCopy();
            _tripGrid.RowsRemoved += (_, _) => QueueRunsCopy();
        }
        CopyRunsToWindow();
        f.Show(this);
    }

    /// <summary>Copies the board into the window once, after a batch of changes (not once per cell).</summary>
    void QueueRunsCopy()
    {
        if (_runsWindowGrid == null || _runsCopyQueued || !IsHandleCreated) return;
        _runsCopyQueued = true;
        BeginInvoke(new Action(() =>
        {
            _runsCopyQueued = false;
            CopyRunsToWindow();
        }));
    }

    /// <summary>The rows of the Cargo Runs board, with their colours, into the window (where you were scrolled stays).</summary>
    void CopyRunsToWindow()
    {
        var g = _runsWindowGrid;
        if (g == null || g.IsDisposed) return;
        var top = g.Rows.Count > 0 ? g.FirstDisplayedScrollingRowIndex : 0;
        g.SuspendLayout();
        try
        {
            g.Rows.Clear();
            foreach (DataGridViewRow r in _tripGrid.Rows)
            {
                if (r.IsNewRow) continue;
                var row = g.AddRow(g.Columns.Cast<DataGridViewColumn>().Select(c => (c.Name, (object?)(Convert.ToString(r.Cells[c.Name].Value) ?? ""))).ToArray());
                row.DefaultCellStyle.BackColor = r.DefaultCellStyle.BackColor;
            }
            if (top > 0 && top < g.Rows.Count) g.FirstDisplayedScrollingRowIndex = top;
        }
        finally
        {
            g.ResumeLayout();
        }
        if (_runsWindow != null) _runsWindow.Text = "Cargo Runs – " + _tripGrid.Rows.Count + (_tripGrid.Rows.Count == 1 ? " run" : " runs") + (_current != null ? " – " + _sessionBox.Text : "");
    }
}
