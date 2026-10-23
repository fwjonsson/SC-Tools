using System.Globalization;

namespace HaulingTracker.UI;

// The Trip Planner tab. It divides the open batches over the crew (the active party, each with a Batch Capacity) and shows
// the trips on two kinds of pages: an Overview with everybody, and a page for every crew member. A page has a map of Pyro with
// the planned trips and a table: the runs so far (coloured like Cargo Runs) followed by the planned trips.
public sealed partial class MainForm
{
    /// <summary>The colour of a worker on the Overview: the one picked in the Party table, else zzoega orange, zzatvo green, the others one of the palette.</summary>
    Color WorkerColor(string name)
    {
        if (_crew.PickedColor(name) is int picked) return Color.FromArgb(picked);
        if (SameName(name, "zzoega")) return Color.FromArgb(255, 150, 40);
        if (SameName(name, "zzatvo")) return Color.FromArgb(60, 205, 95);
        var index = Math.Max(0, _data.Party.FindIndex(x => SameName(x, name)));
        return MapPalette[index % MapPalette.Length];
    }

    /// <summary>A trip that is not the next one: the worker's colour, half faded into the table colour.</summary>
    static Color Faded(Color c) => Color.FromArgb(
        (c.R * 55 + Theme.Cell.R * 45) / 100, (c.G * 55 + Theme.Cell.G * 45) / 100, (c.B * 55 + Theme.Cell.B * 45) / 100);
    // routes on the map: colours that cannot be mistaken for the run colours (green, orange, red, yellow)
    static readonly Color[] MapPalette =
    {
        Color.FromArgb(70, 130, 255), Color.FromArgb(210, 70, 180), Color.FromArgb(0, 170, 160),
        Color.FromArgb(140, 100, 220), Color.FromArgb(205, 160, 100), Color.FromArgb(90, 190, 230),
    };

    // the button sits in the top bar of the window, level with Options and Stop Session, while the Trip Planner tab is shown
    readonly ToolStripButton _lockBtn = new() { DisplayStyle = ToolStripItemDisplayStyle.Text, Text = "Next Stops: Unlocked", Padding = new Padding(8, 0, 8, 0), Visible = false };
    readonly Label _planNote = new();
    int _planMapSide;                              // the size of the map (square), kept when the page is rebuilt so it does not jump
    TableLayoutPanel? _planPage;                   // the whole Trip Planner page (the row of the map and the tables is sized to fit everybody)
    Action? _fitPlanPage;                          // sizes that row for the page shown now
    readonly Panel _planBody = new();              // the page: the map and the tables (rebuilt when the plan changes)
    string _planShown = "";                        // what the pages show now, so they are only rebuilt when it changes

    Control BuildPlannerPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12), AutoScroll = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // a warning, only when there is one
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 500));   // the map with the short Cargo Runs under it, and the tables of everybody: the whole height of the page (PlanPage sets it)
        _planPage = t;
        t.Resize += (_, _) => _fitPlanPage?.Invoke();   // a taller or lower window changes how big the map can be

        // Next Stops: Locked / Unlocked is in the top bar of the window (AddSessionControlsToMenu); hold the mouse over it for the explanation
        _lockBtn.ToolTipText = "Unlocked: the plan is free, and is worked out again whenever something changes (someone joins, more contracts, a pickup).\n\n"
            + "Locked: the next stop of everybody is kept for him. Planning again leaves those alone and plans the rest around them, and a run in progress counts in the ship. "
            + "Press it again to unlock. Nothing is locked until you press it.\n\n"
            + "Who flies, how many batches each ship holds and which quantum drive each has is set in the Party table on the front page. The runs below get their worker from the logs, and from these trips for members without a log.\n\n"
            + "Flight time is the estimated flying time of that whole route with the drive of the worker (~, no loading); Time is how long a run really took. Green = done, orange = some cargo lost, red = all lost. The run you are flying is your Current row, in your colour.";

        _planNote.Dock = DockStyle.Fill;
        _planNote.AutoSize = true;
        _planNote.TextAlign = ContentAlignment.MiddleLeft;
        _planNote.ForeColor = Color.DimGray;
        _planNote.Tag = "muted";
        _planNote.Visible = false;
        t.Controls.Add(_planNote, 0, 0);

        _planBody.Dock = DockStyle.Fill;
        _planBody.Margin = new Padding(0);
        t.Controls.Add(_planBody, 0, 1);
        BuildPlanRunsGrid();   // it sits under the map (PlanPage puts it there)
        return t;
    }

    /// <summary>The text of the button follows the session: Unlocked (the plan is free) or Locked (the next stop of everybody is kept for him).</summary>
    void UpdateLockButton()
    {
        var on = _state.Inputs?.NextStopsLocked == true;
        _lockBtn.Text = on ? "Next Stops: Locked" : "Next Stops: Unlocked";
        if (_lockBtn.Font.Bold != on) _lockBtn.Font = new Font(_lockBtn.Font, on ? FontStyle.Bold : FontStyle.Regular);
    }

    /// <summary>
    /// Next Stops: Locked / Unlocked. Unlocked (the start) the plan is free. Pressing it works the plan out and locks the next stop of everybody to him:
    /// planning again later only changes the other trips, so someone who joins gets his share of the rest. Pressing it again takes the locks away.
    /// </summary>
    void ToggleLocks()
    {
        if (_state.Inputs == null)
        {
            SetStatus("There is no session to plan.");
            return;
        }
        var lockedNow = _state.Inputs.NextStopsLocked;
        var answer = MessageBox.Show(this,
            lockedNow
                ? "Unlock the next stops?\n\nThe plan is free again, so it can change while people are on their way to a stop."
                : "Lock the next stops?\n\nThe plan is worked out now and the next stop of everybody is locked to him. Planning again later only changes the other trips.",
            "Next Stops", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        _planShown = "";
        if (lockedNow)
        {
            _plan.Unlock();
            SetStatus("The next stops are unlocked: the plan is free.");
        }
        else
        {
            var locked = _plan.LockNextStops();
            SetStatus("The next stops are locked" + (locked > 0 ? " (" + locked + (locked == 1 ? " worker)." : " workers).") : "."));
        }
        UpdateLockButton();
        RefreshPlanner();
    }

    /// <summary>
    /// A short copy of Cargo Runs for the Trip Planner tab (under the map): the number, the worker, the value, when it started and was completed, its time and flight time
    /// (not the locations, the batches or the lost cargo), so it fits under the map. The Session tab has the whole table.
    /// It has the same cells as that one (the other columns are hidden), and it cannot be edited.
    /// </summary>
    void BuildPlanRunsGrid()
    {
        var g = _planRunsGrid;
        g.Dock = DockStyle.Fill;
        g.Margin = new Padding(0, 3, 0, 0);
        StyleGrid(g);
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.ReadOnly = true;
        g.Columns.Add(Col(RunCol.Worker, "Worker", 190, true));
        var run = Col(RunCol.Number, "#", 36, true);
        g.Columns.Add(run);
        run.DisplayIndex = 0;
        g.Columns.Add(Col(RunCol.Places, "Locations", 420, true, DataGridViewContentAlignment.MiddleLeft, true));
        g.Columns.Add(Col(RunCol.Batches, "Batches", 90, true));
        g.Columns.Add(Col(RunCol.Value, "Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        g.Columns.Add(Col(RunCol.Started, "Started", 76, true));
        g.Columns.Add(Col(RunCol.Completed, "Completed", 108, true));
        g.Columns.Add(Col(RunCol.Time, "Time", 90, true));
        g.Columns.Add(Col(RunCol.Flight, "Flight time", 90, true));
        g.Columns.Add(Col(RunCol.Lost, "Lost Cargo", 116, true));
        g.Columns.Add(Col(RunCol.PerHour, "aUEC/h", 96, true, DataGridViewContentAlignment.MiddleRight));
        FitColumns(g);
        foreach (DataGridViewColumn c in g.Columns)
        {
            // not the locations, the batches or the lost cargo (the cells are still there: the rows are the same as on the Session tab)
            if (c.Name is not (RunCol.Worker or RunCol.Number or RunCol.Value or RunCol.Started or RunCol.Completed or RunCol.Time or RunCol.Flight or RunCol.PerHour)) c.Visible = false;
        }
        // the worker keeps his width (like #); the other columns share the rest
        var worker = g.Columns[RunCol.Worker]!;
        worker.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        worker.Width = 110;
        foreach (DataGridViewColumn c in g.Columns) c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;   // the titles in the middle, also over the left-aligned Locations
        g.ScrollBars = ScrollBars.Vertical;
    }

    /// <summary>Sets (or, with null, clears) where a worker is by hand. His locked next stop is released, so the plan is free to start with a stop near him.</summary>
    void SetLocation(string name, string? stationId)
    {
        _where.SetLocation(name, stationId);
        _plan.ReleaseLock(name);
        _state.Save();
        BeginInvoke(new Action(() =>
        {
            _plan.Invalidate();
            _planShown = "";
            _plan.GetPlan();
            RefreshPlanner();
        }));
    }

    /// <summary>The title of a worker's table: just his name.</summary>
    static string PlanTitle(PlanWorkerView w) => w.Name.ToUpperInvariant();   // the name in capitals, in his colour (only how it is shown: the name itself stays as it is)

    /// <summary>Red = all cargo lost, orange = some lost, green = done, yellow = on the way. Planned trips have no colour.</summary>
    static Color? RowColor(PlanRow r) => r.Level switch
    {
        "all" => LostRed,
        "little" => LostOrange,
        "done" => DoneGreen,
        "active" => PartialYellow,
        _ => null,
    };

    /// <summary>Shows the plan: the map on the left, the tables on the right. It only runs while the Trip Planner tab is on screen.</summary>
    void RefreshPlanner()
    {
        UpdateLockButton();
        PlanView plan;
        try
        {
            plan = _plan.GetPlan();
        }
        catch (Exception ex)
        {
            // the plan could not be worked out: say so (and try again on the next update), instead of leaving an old or empty page
            _planNote.Text = "The plan could not be worked out: " + ex.Message;
            _planNote.Visible = true;
            _planShown = "";
            _plan.Invalidate();
            return;
        }
        _planNote.Visible = false;   // the line above the map (the loads that wait) is gone: the page has more room; only a problem is written there
        var shown = (Theme.Dark ? "dark" : "light") + "¦" + plan.Signature;   // the rows carry the colours of the theme too
        if (shown == _planShown) return;   // nothing changed, so nothing flickers

        _planBody.SuspendLayout();
        try
        {
            var page = PlanPage(plan);   // takes the Cargo Runs table from the page it replaces
            foreach (var old in _planBody.Controls.Cast<Control>().ToList())
            {
                _planBody.Controls.Remove(old);
                old.Dispose();
            }
            _planBody.Controls.Add(page);
            _planShown = shown;   // only when the page really got built, so a failed build is tried again
        }
        catch (Exception ex)
        {
            _planNote.Text = "The plan could not be shown: " + ex.Message;
            _planNote.Visible = true;
            _planShown = "";
        }
        finally
        {
            _planBody.ResumeLayout(true);
        }
        if (IsHandleCreated) BeginInvoke(new Action(() =>
        {
            RefitPlanGrids();         // the page is on screen now: the rows are measured at the widths they really have
            _fitPlanPage?.Invoke();   // and the map gets its size once more, now that the new page is laid out
        }));
    }

    /// <summary>Measures the rows of the tables of the Trip Planner again (a row with a long route is as high as its text needs at the width the table has).</summary>
    void RefitPlanGrids()
    {
        var stack = new Stack<Control>(_planBody.Controls.Cast<Control>());
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            foreach (Control child in c.Controls) stack.Push(child);
            if (c is PlanGridView grid) grid.FitHeight();
        }
    }

    /// <summary>The map on the left, square and as large as the page allows, the tables of everybody on the right.</summary>
    Control PlanPage(PlanView plan)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 4, 0, 0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var map = new PyroMapView { Dock = DockStyle.Top };
        // under the map: the small team status line (a text that changes every few seconds)
        var status = new StatusLine { Dock = DockStyle.Top };
        status.ShowText(_statusText, false);
        _statusLine = status;
        status.Disposed += (_, _) =>
        {
            if (ReferenceEquals(_statusLine, status)) _statusLine = null;
        };
        var left = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 0, 12, 0) };
        // the map on top (always square: it is as high as the column is wide), the team status under it and the short Cargo Runs in the room that is left;
        // what is added first is docked last, so it sits lowest
        var runsTitle = RunsTitle();   // with a button that opens every run in a window of its own
        runsTitle.Dock = DockStyle.Top;
        runsTitle.Height = TitleHeight;
        _planRunsGrid.Dock = DockStyle.Fill;
        left.Controls.Add(_planRunsGrid);   // moves it from the page that is replaced: that one must not dispose it
        left.Controls.Add(runsTitle);
        // like on the web page, the map and the team status are in one frame (a thin line round them, the colour of the tables)
        var frame = new MapFrame { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(1) };
        frame.Controls.Add(status);   // added first, so it sits under the map
        frame.Controls.Add(map);
        left.Controls.Add(frame);
        var squaring = false;
        void Square()
        {
            if (squaring) return;
            squaring = true;
            try
            {
                var w = left.ClientSize.Width - left.Padding.Horizontal - frame.Padding.Horizontal;
                if (w > 0 && map.Height != w) map.Height = w;   // a map that is not square is drawn small and squeezed
            }
            finally
            {
                squaring = false;
            }
        }
        left.Resize += (_, _) => Square();
        var host = new PlanHost { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, Margin = new Padding(0) };   // never scrolls: the row is as high as everybody's tables
        host.Resize += (_, _) => FitPlanBlocks(host);

        // one colour per worker, his current trip in bold and solid, the later ones dotted
        foreach (var w in plan.Workers)
        {
            var k = 0;
            foreach (var r in w.MapRows)
            {
                map.Routes.Add(new MapRoute { Label = k == 0 ? w.Name : "", Color = WorkerColor(w.Name), Stops = r.Stops, From = r.From, Passed = r.Passed, Bold = r.Solid, Dashed = !r.Solid, Tag = r });
                k++;
            }
        }
        if (_planMapSide > 0) t.ColumnStyles[0].Width = _planMapSide + 2 + left.Padding.Horizontal;   // the size it had: the new page does not start small
        var widths = PlanTextWidths(plan);   // the same for every worker, so his tables line up with the others
        foreach (var w in plan.Workers) host.Controls.Add(PlanBlock(w, widths));
        FitPlanBlocks(host);

        // The map is a square, 40% of the width (and no higher than the page leaves for the map, the status and a short Cargo Runs). The row is as high as the page,
        // or higher when the tables of everybody need more: the workers go down the right side, and the page only scrolls when they do not fit.
        var fitting = false;
        var again = false;   // a size changed while it was being fitted (setting the sizes below resizes the page, the map and the tables): fit once more
        void FitPage()
        {
            if (fitting)
            {
                again = true;
                return;
            }
            fitting = true;
            try
            {
                for (var pass = 0; pass < 4; pass++)
                {
                    again = false;
                    FitPageOnce();
                    if (!again) break;
                }
            }
            finally
            {
                fitting = false;
            }
        }
        void FitPageOnce()
        {
            {
                // The size of the map comes from the window only (the whole Trip Planner tab, which is never rebuilt), not from this page: a page that is just built
                // has a small default size for a moment, and a map sized from that stayed small. The room of a scroll bar is always left out, so the map does not
                // change when one comes or goes. Until the tab has a real size the map keeps the size it had.
                var page = _planPage;
                var note = _planNote.Visible ? _planNote.Height : 0;
                var width = page != null ? page.Width - page.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth : 0;
                var avail = page != null ? page.Height - page.Padding.Vertical - note : 0;
                var side = width >= 600 && avail >= 300
                    ? Math.Max(320, Math.Min((int)(width * 0.4), avail - t.Padding.Vertical - status.Height - frame.Padding.Vertical - TitleHeight - PlanRunsMin))
                    : _planMapSide;
                if (side <= 0) return;   // no real size yet, and none from before
                _planMapSide = side;
                if (avail < 300) avail = t.ClientSize.Height;
                var column = side + frame.Padding.Horizontal + left.Padding.Horizontal;
                if ((int)t.ColumnStyles[0].Width != column)
                {
                    t.ColumnStyles[0].Width = column;
                    t.PerformLayout();   // the column gets its new width now, not later
                }
                Square();   // the map is as high as the column really is wide (never set from the wanted size: a column that had not followed yet left it tall and squeezed)
                var blocks = host.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
                var leftNeed = map.Height + status.Height + frame.Padding.Vertical + TitleHeight + PlanRunsMin;
                var need = Math.Max(avail, Math.Max(leftNeed, blocks) + t.Padding.Vertical + 4);
                if (page != null && Math.Abs(page.RowStyles[1].Height - need) > 0.5f)
                {
                    page.RowStyles[1].Height = need;
                    page.AutoScrollMinSize = new Size(0, need + page.Padding.Vertical + note);
                }
            }
        }
        _fitPlanPage = FitPage;
        t.Resize += (_, _) => FitPage();
        t.VisibleChanged += (_, _) =>
        {
            if (!t.Visible) return;
            FitPage();   // the tab is shown again: its real size is known now (and once more when the window has laid itself out, the map was sometimes left small)
            if (t.IsHandleCreated) t.BeginInvoke(new Action(() => { FitPage(); Square(); }));
        };
        Square();
        foreach (Control b in host.Controls) b.SizeChanged += (_, _) => FitPage();
        FitPage();
        t.Controls.Add(left, 0, 0);
        t.Controls.Add(host, 1, 0);
        return t;
    }

    static void FitPlanBlocks(FlowLayoutPanel host)
    {
        var width = Math.Max(200, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control c in host.Controls) c.Width = width;
        // after the window got bigger again nothing may stay scrolled to the side
        if (host.AutoScrollPosition.X != 0) host.AutoScrollPosition = new Point(0, -host.AutoScrollPosition.Y);
    }

    /// <summary>The frame round the map and the team status, and round every worker, in the Trip Planner (like on the web page): the colour of the tables with a thin line.</summary>
    sealed class MapFrame : Panel
    {
        public MapFrame() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var fill = new SolidBrush(Theme.Cell)) e.Graphics.FillRectangle(fill, ClientRectangle);
            using var pen = new Pen(Theme.Lines);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>The panel that holds the tables of the Trip Planner: it scrolls up and down only, never sideways (the tables fit its width).</summary>
    sealed class PlanHost : FlowLayoutPanel
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowScrollBar(IntPtr hWnd, int bar, bool show);

        protected override void WndProc(ref Message m)
        {
            if (IsHandleCreated) ShowScrollBar(Handle, 0, false);   // 0 = the horizontal bar
            base.WndProc(ref m);
        }
    }

    /// <summary>A title and the two tables of one crew member: the Current Route, and right of it the narrow Queued routes (under it when the window is too narrow for both).</summary>
    Control PlanBlock(PlanWorkerView w, (int Current, int Queued, int Cargo) widths)
    {
        // every worker in his own frame (like the map), so it is clear where one ends and the next begins
        var block = new MapFrame { Margin = new Padding(0, 0, 0, 12), Padding = new Padding(10, 4, 10, 10), BackColor = Theme.Cell };
        // the title: his name, and right of it where he is now (his Current location: the logs keep it up to date, and here it can be set by hand)
        var label = new Panel { Dock = DockStyle.Top, Height = 34 };
        var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        var nameWidth = TextRenderer.MeasureText(PlanTitle(w), titleFont).Width;
        label.Controls.Add(new Label { Text = PlanTitle(w), AutoSize = true, Font = titleFont, ForeColor = WorkerColor(w.Name), Location = new Point(0, 6) });
        var hintFont = new Font("Segoe UI", 9.5f);
        var hintText = "Current location:";
        var hintX = nameWidth + 30;
        label.Controls.Add(new Label { Text = hintText, AutoSize = true, Font = hintFont, ForeColor = Theme.Muted, Location = new Point(hintX, 9) });
        var comboX = hintX + TextRenderer.MeasureText(hintText, hintFont).Width + 12;
        var combo = new ThemedCombo { Width = 250, Location = new Point(comboX, 5), Font = hintFont };
        var stations = PyroDistances.Points.OrderBy(s => s.Id == PyroDistances.Ruin ? 0 : 1).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();   // Ruin Station first
        combo.Items.Add("Automatic: " + PyroDistances.NameOf(w.AutoLocationId));   // the logs: his last pickup, delivery or hand-in
        foreach (var s in stations) combo.Items.Add(s.Name);
        combo.SelectedIndex = w.LocationByHand ? Math.Max(0, stations.FindIndex(s => s.Id == w.LocationId) + 1) : 0;
        combo.SelectedIndexChanged += (_, _) =>
            SetLocation(w.Name, combo.SelectedIndex <= 0 ? null : stations[combo.SelectedIndex - 1].Id);
        label.Controls.Add(combo);
        label.Controls.Add(new Label
        {
            Text = w.LocationByHand ? "set by you, until the logs show him moving (pickup, delivery, hand-in, waste or a quantum jump)" : "follows his pickups, deliveries and hand-ins",
            AutoSize = true, Font = new Font("Segoe UI", 9f), ForeColor = Theme.Muted, Location = new Point(comboX + combo.Width + 12, 10),
        });
        var height = label.Height;

        var visible = w.Rows.Where(x => !x.Hidden).ToList();
        if (visible.Count > 0)
        {
            var current = visible[0];
            var queued = visible.Skip(1).ToList();
            var currentGrid = CurrentRouteGrid(w, current, widths.Current, widths.Cargo);
            var queuedGrid = QueuedRoutesGrid(queued, widths.Queued);

            // each table with its small title above it
            Panel Boxed(string title, PlanGridView grid)
            {
                var box = new Panel { Margin = new Padding(0) };
                box.Controls.Add(grid);   // added first, so the title sits above it
                box.Controls.Add(PlanTableTitle(title));
                return box;
            }
            var currentBox = Boxed("Current Route", currentGrid);
            var queuedBox = Boxed("Queued routes", queuedGrid);
            var pair = new Panel { Dock = DockStyle.Top };
            pair.Controls.Add(currentBox);
            pair.Controls.Add(queuedBox);

            // The Current Route on top and the Queued routes under it, both as wide as the frame (so their columns line up).
            var fitting = false;
            void FitPair()
            {
                if (fitting) return;   // setting the sizes below changes the heights of the tables, which comes back here
                fitting = true;
                try
                {
                    var room = Math.Max(PlanCurrentFixed(widths.Current) + PlanMinText, pair.ClientSize.Width);
                    currentBox.Width = room;
                    queuedBox.Width = room;
                    // the tables are as high as their rows (a long text takes two lines), the boxes follow them, and the block follows the boxes
                    currentBox.Height = PlanTitleHeight + currentGrid.Height;
                    queuedBox.Height = PlanTitleHeight + queuedGrid.Height;
                    currentBox.Location = new Point(0, 0);
                    queuedBox.Location = new Point(0, currentBox.Height + PlanGap);
                    var tall = queuedBox.Bottom;
                    if (pair.Height != tall) pair.Height = tall;
                    var blockHeight = label.Height + tall + block.Padding.Vertical;
                    if (block.Height != blockHeight) block.Height = blockHeight;
                }
                finally
                {
                    fitting = false;
                }
            }
            currentGrid.SizeChanged += (_, _) => FitPair();
            queuedGrid.SizeChanged += (_, _) => FitPair();
            pair.Resize += (_, _) => FitPair();
            FitPair();
            height += pair.Height;
            block.Controls.Add(pair);   // added first, so the title sits above it
        }

        block.Controls.Add(label);
        block.Height = height + block.Padding.Vertical;
        return block;
    }

    const int PlanTitleHeight = 26;
    const int PlanMinText = 110;                            // the text column of a table (Cargo, Route) is never narrower than this
    const int PlanQueuedFixed = 36 + 64 + 84 + 80 + 74 + 68;  // the Queued routes without its Route column: #, Stops, Batches, Value, Distance, In flight
    const int PlanGap = 8;                                  // between the Current Route and the Queued routes under it
    const int PlanRunsMin = 150;                            // Cargo Runs under the map is never lower than this (the page scrolls instead)

    /// <summary>The Current Route without its Cargo column (the one that takes the room that is left): #, Stops, Drop/Collect, Value, Distance, In flight.</summary>
    static int PlanCurrentFixed(int action) => 36 + 120 + action + 80 + 74 + 68;

    static readonly Font PlanBoldFont = new("Segoe UI", 10f, FontStyle.Bold);

    /// <summary>
    /// The width that the Drop/Collect column of the Current Route (what is done at a stop) needs for its longest line, the width of its Cargo column, and the width that the
    /// route of the Queued routes needs, over all the workers: every worker gets the same tables, so they line up, and a row stays one line high when there is room.
    /// </summary>
    static (int Current, int Queued, int Cargo) PlanTextWidths(PlanView plan)
    {
        int Measure(string text) => TextRenderer.MeasureText(text, PlanBoldFont).Width;   // bold: the widest it gets
        var current = 0;
        var queued = 0;
        var cargo = 0;
        foreach (var w in plan.Workers)
        {
            var visible = w.Rows.Where(r => !r.Hidden).ToList();
            if (visible.Count == 0) continue;
            foreach (var l in visible[0].Lines)
            {
                current = Math.Max(current, Measure(l.ActionText));
                cargo = Math.Max(cargo, Measure(l.CargoText));
            }
            foreach (var r in visible.Skip(1)) queued = Math.Max(queued, Measure(r.Route));
        }
        return (Math.Clamp(current + 20, 150, 420), Math.Clamp(queued + 20, 150, 700), Math.Clamp(cargo + 20, 110, 420));
    }

    /// <summary>The small title above one of the two tables of a worker.</summary>
    static Label PlanTableTitle(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = PlanTitleHeight,
        AutoSize = false,
        TextAlign = ContentAlignment.BottomLeft,
        Padding = new Padding(0, 0, 0, 2),
        Font = PlanBoldFont,
        ForeColor = Theme.Text,
    };

    /// <summary>
    /// A table of the Trip Planner. Current Route: #, Stops (just the station), Drop/Collect (what is done there), Cargo (the SCU, and the containers of a load; this column
    /// takes the room that is left), Value, Distance, In flight. Queued routes: #, Route (the stations; takes the room that is left), Batches, Value, Distance, In flight.
    /// A text column wraps onto a second line only when it has to.
    /// </summary>
    PlanGridView NewPlanGrid(bool current, int textWidth, int cargoWidth)
    {
        var grid = new PlanGridView();
        StyleGrid(grid);
        grid.Dock = DockStyle.Top;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ScrollBars = ScrollBars.None;
        grid.ShowCellToolTips = false;   // a pop-up with the value of a cell that is a bit tight is only in the way
        grid.Columns.Add(Col(PlanCol.Number, "#", 36, true));
        DataGridViewColumn text;          // the column that takes the room that is left
        DataGridViewColumn? action = null;
        if (current)
        {
            grid.Columns.Add(Col(PlanCol.Stop, "Stops", 120, true, DataGridViewContentAlignment.MiddleLeft));
            action = Col(PlanCol.Action, "Deliver/Collect", textWidth, true, DataGridViewContentAlignment.MiddleLeft);
            grid.Columns.Add(action);
            text = Col(PlanCol.Cargo, "Cargo", cargoWidth, true, DataGridViewContentAlignment.MiddleLeft);
            grid.Columns.Add(text);
        }
        else
        {
            text = Col(PlanCol.Route, "Route", textWidth, true, DataGridViewContentAlignment.MiddleLeft);   // the stations of the trip in order (takes the room that is left)
            grid.Columns.Add(text);
            grid.Columns.Add(Col(PlanCol.Stops, "Stops", 64, true));   // how many stops it has
            grid.Columns.Add(Col(PlanCol.Batches, "Batches", 84, true));
        }
        grid.Columns.Add(Col(PlanCol.Value, "Value", 80, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col(PlanCol.Distance, "Distance", 74, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col(PlanCol.Flight, "In flight", 68, true, DataGridViewContentAlignment.MiddleRight));
        foreach (DataGridViewColumn c in grid.Columns)
        {
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            c.MinimumWidth = c == text ? PlanMinText : 30;
        }
        text.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        if (action != null) action.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.TextColumn = text;
        // the rows are added as whole lines of cells in the order of these columns: LineCells, QueuedCells and the totals make them so (the web page shows the same lines)
        grid.Width = current ? PlanCurrentFixed(textWidth) + cargoWidth : PlanQueuedFixed + textWidth;   // the width it is expected to get, so the first measure of its rows is about right
        return grid;
    }

    /// <summary>The Current Route of a worker: the run he is flying (or the trip he starts with), one stop per line. The worker colour is on the line where he is.</summary>
    PlanGridView CurrentRouteGrid(PlanWorkerView w, PlanRow current, int textWidth, int cargoWidth)
    {
        var grid = NewPlanGrid(true, textWidth, cargoWidth);
        var cells = PlanText.LineCells(current.Lines);
        for (var i = 0; i < cells.Count; i++)
        {
            var row = grid.Rows[grid.Rows.Add(cells[i])];
            if (i != current.Here) continue;
            var st = row.DefaultCellStyle;
            st.Font = PlanBoldFont;
            st.BackColor = WorkerColor(w.Name);
            st.ForeColor = TextOn(WorkerColor(w.Name));
            st.SelectionBackColor = WorkerColor(w.Name);
            st.SelectionForeColor = TextOn(WorkerColor(w.Name));
        }
        AddTotalRow(grid, PlanText.LineTotals(current.Lines));
        // a load the game did not tell the SCU of: click its Cargo cell to type it (the program works out the containers)
        var cargo = grid.Columns[PlanCol.Cargo]!.Index;
        bool Typable(int row, int col) => col == cargo && row >= 0 && row < current.Lines.Count && current.Lines[row].Unknown.Count > 0;
        for (var i = 0; i < current.Lines.Count; i++)
        {
            if (Typable(i, cargo)) grid.Rows[i].Cells[PlanCol.Cargo].Value = current.Lines[i].CargoText + "  (click to type)";
        }
        grid.CellMouseEnter += (_, e) => grid.Cursor = Typable(e.RowIndex, e.ColumnIndex) ? Cursors.Hand : Cursors.Default;
        grid.CellClick += (_, e) =>
        {
            if (Typable(e.RowIndex, e.ColumnIndex)) AskScu(current.Lines[e.RowIndex].Unknown.Distinct().ToList());
        };
        grid.FitHeight();
        return grid;
    }

    /// <summary>The Queued routes of a worker: the trips after his current one, each with a short route tag (just the stations).</summary>
    PlanGridView QueuedRoutesGrid(List<PlanRow> queued, int textWidth)
    {
        var grid = NewPlanGrid(false, textWidth, 0);
        if (queued.Count == 0)
        {
            var none = grid.AddRow((PlanCol.Route, "none"));
            none.DefaultCellStyle.ForeColor = Theme.Muted;
        }
        foreach (var r in queued)
        {
            var row = grid.Rows[grid.Rows.Add(PlanText.QueuedCells(r))];
            if (RowColor(r) is { } color) row.DefaultCellStyle.BackColor = color;
        }
        if (queued.Count > 0) AddTotalRow(grid, PlanText.PlanTotals(queued));
        grid.FitHeight();
        return grid;
    }

    /// <summary>The Total line at the bottom of a table.</summary>
    static void AddTotalRow(DataGridView grid, string[] cells)
    {
        var total = grid.Rows[grid.Rows.Add(cells)];
        total.DefaultCellStyle.Font = PlanBoldFont;
        total.DefaultCellStyle.BackColor = Theme.Calc;
        total.DefaultCellStyle.ForeColor = Theme.Text;
        total.DefaultCellStyle.SelectionBackColor = Theme.Calc;
        total.DefaultCellStyle.SelectionForeColor = Theme.Text;
    }

    /// <summary>
    /// A table that never scrolls: its text column takes the width that the narrow columns leave, and it is as high as its rows (a row with a long text that has to wrap
    /// is as high as that text needs at the width the column has).
    /// </summary>
    sealed class PlanGridView : DataGridView
    {
        bool _fitting;

        /// <summary>The column that takes the room that is left (the others keep their width).</summary>
        public DataGridViewColumn? TextColumn;

        public void FitHeight()
        {
            if (_fitting) return;
            _fitting = true;
            try
            {
                if (TextColumn != null)
                {
                    var narrow = 0;
                    foreach (DataGridViewColumn c in Columns)
                    {
                        if (c != TextColumn && c.Visible) narrow += c.Width;
                    }
                    var wide = Math.Max(TextColumn.MinimumWidth, ClientSize.Width - narrow);
                    if (TextColumn.Width != wide) TextColumn.Width = wide;
                }
                foreach (DataGridViewRow row in Rows)
                {
                    var h = RowTemplate.Height;
                    try
                    {
                        h = Math.Max(h, row.GetPreferredHeight(row.Index, DataGridViewAutoSizeRowMode.AllCells, true));
                    }
                    catch (Exception)
                    {
                        // the text cannot be measured yet: one line
                    }
                    if (row.Height != h) row.Height = h;
                }
                var total = ColumnHeadersHeight + Rows.GetRowsHeight(DataGridViewElementStates.Visible) + 3;
                if (Height != total) Height = total;
            }
            catch (InvalidOperationException)
            {
                // the table is busy changing its own layout, when no column or row may change its size: the next resize, or the refit after the page is shown, does it
            }
            finally
            {
                _fitting = false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitHeight();
        }
    }

    /// <summary>Black or white, whichever reads better on this colour.</summary>
    static Color TextOn(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140 ? Color.Black : Color.White;
}
