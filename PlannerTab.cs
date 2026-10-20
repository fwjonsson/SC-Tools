using System.Globalization;

namespace HaulingTracker;

// The Trip Planner tab. It divides the open batches over the crew (the active party, each with a Batch Capacity) and shows
// the trips on two kinds of pages: an Overview with everybody, and a page for every crew member. A page has a map of Pyro with
// the planned trips and a table: the runs so far (coloured like Cargo Runs) followed by the planned trips.
public sealed partial class MainForm
{
    const int DefaultShipCapacity = 3;
    const int MaxShipCapacity = 24;

    /// <summary>The colour of a worker on the Overview: the one picked in the Party table, else zzoega orange, zzatvo green, the others one of the palette.</summary>
    Color WorkerColor(string name)
    {
        var picked = _data.WorkerColors.FirstOrDefault(kv => SameName(kv.Key, name));
        if (picked.Key != null) return Color.FromArgb(picked.Value);
        if (SameName(name, "zzoega")) return Color.FromArgb(255, 150, 40);
        if (SameName(name, "zzatvo")) return Color.FromArgb(60, 205, 95);
        var index = Math.Max(0, _data.Party.FindIndex(x => SameName(x, name)));
        return MapPalette[index % MapPalette.Length];
    }

    /// <summary>
    /// The colour of the k-th planned trip of n (0 = the one to fly now): blue, like the rest of the program, then green for the one after it,
    /// and the later ones go through yellow and orange towards red.
    /// </summary>
    static Color TripColor(int k, int n)
    {
        if (k <= 0) return MapPalette[0];
        var hue = n <= 2 ? 125.0 : 125.0 * (1.0 - (k - 1) / (double)(n - 2));   // green (125) down to red (0)
        return FromHsv(hue, 0.78, 0.92);
    }

    static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : (0.0, c, x);
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
    // routes on the map: colours that cannot be mistaken for the run colours (green, orange, red, yellow)
    static readonly Color[] MapPalette =
    {
        Color.FromArgb(70, 130, 255), Color.FromArgb(210, 70, 180), Color.FromArgb(0, 170, 160),
        Color.FromArgb(140, 100, 220), Color.FromArgb(205, 160, 100), Color.FromArgb(90, 190, 230),
    };

    /// <summary>One line of a table: a run that is done (or on the way), or a planned trip.</summary>
    sealed class PlanRow
    {
        public string Label = "";
        public string Route = "";
        public string Batches = "";
        public int BatchCount;           // batches picked up or planned, for Gm per batch
        public double Gm;
        public double Seconds;           // a run: the time it really took; a planned trip: the estimated flight time
        public string Level = "plan";    // done, little, all, active, plan
        public List<string> Stops = new();   // station ids in flying order
        public PlannedTrip? Trip;
        public bool IsPlan => Level == "plan";
    }

    sealed class PlanWorkerView
    {
        public string Name = "";
        public int Capacity;
        public string Drive = "";        // the quantum drive, or that none is set
        public Color Color;
        public List<PlanRow> Rows = new();
        public List<PlanRow> Planned => Rows.Where(r => r.IsPlan).ToList();
    }

    sealed class PlanView
    {
        public string Signature = "";
        public string Summary = "";
        public string Note = "";
        public List<PlanWorkerView> Workers = new();
    }

    sealed class PlanCore
    {
        public string Key = "";
        public List<PlannedTrip> Trips = new();
        public int Batches;
        public string Note = "";
    }

    readonly Button _planBtn = new();
    readonly Button _assignBtn = new();
    readonly Label _planSummary = new();
    readonly Label _planNote = new();
    readonly ThemedTabControl _planTabs = new();   // Overview and one page per crew member
    string _planShown = "";                        // what the pages show now, so they are only rebuilt when it changes
    PlanCore? _planCore;

    Control BuildPlannerPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));    // title
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // hint
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));    // button
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // summary
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // note
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // the pages

        t.Controls.Add(Caption("Trip Planner", "Divides the open batches over the crew, so everybody flies as little as possible and you finish together"), 0, 0);

        var hint = new Label
        {
            Text = "Who flies, how many batches each ship holds and which quantum drive each has is set in the Party table on the front page. A run shows the time it really took; a planned trip shows ~ the estimated flying time (spool time + distance / drive speed for every jump, no loading). Green = done, orange = some cargo lost, red = all lost, yellow = on the way.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Tag = "muted",
        };
        t.Controls.Add(hint, 0, 1);

        StyleButton(_planBtn, "Plan Trips");
        _planBtn.Margin = new Padding(0, 5, 0, 5);
        _planBtn.Click += (_, _) =>
        {
            _planCore = null;
            _planShown = "";
            RefreshPlanner();
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        StyleButton(_assignBtn, "Assign Workers to Stops");
        _assignBtn.Margin = new Padding(0, 5, 0, 5);
        _assignBtn.Click += (_, _) => AssignPlanToStops();
        bar.Controls.Add(_planBtn);
        bar.Controls.Add(_assignBtn);
        t.Controls.Add(bar, 0, 2);

        _planSummary.Dock = DockStyle.Fill;
        _planSummary.TextAlign = ContentAlignment.MiddleLeft;
        _planSummary.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        t.Controls.Add(_planSummary, 0, 3);
        _planNote.Dock = DockStyle.Fill;
        _planNote.TextAlign = ContentAlignment.MiddleLeft;
        _planNote.ForeColor = Color.DimGray;
        _planNote.Tag = "muted";
        t.Controls.Add(_planNote, 0, 4);

        _planTabs.Dock = DockStyle.Fill;
        _planTabs.Margin = new Padding(0);
        t.Controls.Add(_planTabs, 0, 5);
        return t;
    }

    /// <summary>
    /// Puts the workers of the plan on the pickup board: every stop that is not finished goes to the worker who takes the most batches
    /// there in the plan (the one who flies it first when it is a tie).
    /// </summary>
    void AssignPlanToStops()
    {
        GetPlan();
        var core = _planCore;
        if (core == null || core.Trips.Count == 0 || _inputs == null)
        {
            SetStatus("Nothing is planned, so there is nobody to assign.");
            return;
        }
        var batches = new Dictionary<string, Dictionary<string, int>>();   // station id -> worker -> batches
        var firstSeen = new Dictionary<string, List<string>>();            // station id -> the workers in the order of the plan
        foreach (var trip in core.Trips)
        {
            foreach (var (id, n) in trip.Stops)
            {
                if (!batches.TryGetValue(id, out var per)) batches[id] = per = new Dictionary<string, int>();
                per[trip.Worker] = per.GetValueOrDefault(trip.Worker) + n;
                if (!firstSeen.TryGetValue(id, out var order)) firstSeen[id] = order = new List<string>();
                if (!order.Contains(trip.Worker)) order.Add(trip.Worker);
            }
        }
        var choice = new Dictionary<string, string>();
        foreach (var (id, per) in batches)
        {
            var best = "";
            var bestN = 0;
            foreach (var worker in firstSeen[id])
            {
                if (per[worker] <= bestN) continue;
                best = worker;
                bestN = per[worker];
            }
            choice[id] = best;
        }
        var assigned = 0;
        foreach (var r in _pickupRows)
        {
            var id = StationOf(r.Zone);
            if (id == null || !choice.TryGetValue(id, out var worker)) continue;
            GetRow(r.Zone).Worker = worker;
            assigned++;
        }
        Storage.Save(_data);
        SetStatus("Assigned the workers of the plan to " + assigned + (assigned == 1 ? " stop." : " stops."));
        _renderedVersion = -1;
        RefreshFromLog();
    }

    int ShipCapacityOf(string name) =>
        _data.ShipCapacity.TryGetValue(name, out var c) ? Math.Clamp(c, 0, MaxShipCapacity) : DefaultShipCapacity;

    List<string> ActiveCrew() => _data.Party.Where(n => !_data.InactiveParty.Any(x => SameName(x, n))).ToList();

    static string RouteText(PlannedTrip trip) =>
        string.Join("  →  ", trip.Stops.Select(s => PyroDistances.NameOf(s.Id) + " ×" + s.Batches));

    static string PlanTitle(PlanWorkerView w)
    {
        var planned = w.Planned;
        var done = w.Rows.Count - planned.Count;
        var text = w.Name + "  –  ship holds " + w.Capacity + (w.Capacity == 1 ? " batch" : " batches") + "  –  " + w.Drive + "  –  "
            + done + (done == 1 ? " run so far" : " runs so far") + "  –  ";
        text += planned.Count == 0
            ? "nothing planned"
            : planned.Count + (planned.Count == 1 ? " trip planned (" : " trips planned (") + planned.Sum(r => r.BatchCount) + " batches, " + Gm(planned.Sum(r => r.Gm)) + ", about " + FlightSpan(planned.Sum(r => r.Seconds)) + " of flying)";
        return text;
    }

    /// <summary>Red = all cargo lost, orange = some lost, green = done, yellow = on the way. Planned trips have no colour.</summary>
    static Color? RowColor(PlanRow r) => r.Level switch
    {
        "all" => LostRed,
        "little" => LostOrange,
        "done" => DoneGreen,
        "active" => PartialYellow,
        _ => null,
    };

    /// <summary>The runs of one worker in the selected session, oldest first, as lines for his table.</summary>
    List<PlanRow> RunRows(string name)
    {
        var rows = new List<PlanRow>();
        foreach (var t in _trips.Where(x => SameName(x.Worker, name)).OrderBy(RunStartedAt))
        {
            var level = t.Manual ? "All" : LostLevel(t.Key);
            var stops = new List<string>();
            var parts = new List<(string Name, int N)>();
            var picked = 0;
            foreach (var p in t.Pickups.OrderBy(x => x.Time))
            {
                var n = p.MissionIds.Count > 0 ? p.MissionIds.Count : p.Missions;
                picked += n;
                var shown = LocationName(p.Zone);
                if (parts.Count > 0 && parts[^1].Name == shown) parts[^1] = (shown, parts[^1].N + n);
                else parts.Add((shown, n));
                var id = StationOf(p.Zone);
                if (id != null) stops.Add(id);
            }
            rows.Add(new PlanRow
            {
                Label = "Run #" + t.Number,
                Route = string.Join("  →  ", parts.Select(x => x.Name + " ×" + x.N)),
                Batches = level == "All" ? "failed" : t.End.HasValue ? t.HandedIn.ToString(CultureInfo.InvariantCulture) : picked.ToString(CultureInfo.InvariantCulture),
                BatchCount = picked,
                Gm = stops.Count > 0 ? PyroDistances.Run(stops) : 0,
                Seconds = t.End.HasValue ? Math.Max(0, (t.End.Value - t.Start).TotalSeconds) : 0,
                Level = level == "All" ? "all" : level == "Little" ? "little" : t.End.HasValue ? "done" : "active",
                Stops = stops,
            });
        }
        return rows;
    }

    /// <summary>The plan for the open batches of the selected session, with the runs so far. The trips are only worked out again when the batches or the crew change.</summary>
    PlanView GetPlan()
    {
        var crew = ActiveCrew().Select(n => (Name: n, Capacity: ShipCapacityOf(n), Drive: QtOf(n))).ToList();
        var remaining = new Dictionary<string, int>();
        var unknown = new List<string>();
        if (_inputs != null)
        {
            foreach (var r in _openRows)
            {
                var left = r.Missions - r.Loaded;   // batches not picked up yet
                if (left <= 0) continue;
                var id = StationOf(r.Zone);
                if (id == null)
                {
                    unknown.Add(LocationName(r.Zone) + " (" + left + ")");
                    continue;
                }
                remaining[id] = remaining.GetValueOrDefault(id) + left;
            }
        }

        var key = string.Join("|", crew.Select(c => c.Name + ":" + c.Capacity + ":" + c.Drive.Key))
            + "#" + string.Join("|", remaining.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value))
            + "#" + string.Join("|", unknown);
        if (_planCore == null || _planCore.Key != key)
        {
            var note = "Planning every open batch, whoever is assigned to the location now.";
            if (unknown.Count > 0) note += " Not in the distance table, so left out: " + string.Join(", ", unknown) + ".";
            _planCore = new PlanCore { Key = key, Trips = TripPlanner.Plan(remaining, crew), Batches = remaining.Values.Sum(), Note = note };
        }
        var core = _planCore;

        var plan = new PlanView { Note = core.Note };
        var flying = crew.Where(c => c.Capacity > 0).ToList();
        for (var i = 0; i < flying.Count; i++)
        {
            var c = flying[i];
            var drive = DriveOf(c.Name);
            var w = new PlanWorkerView
            {
                Name = c.Name,
                Capacity = c.Capacity,
                Drive = drive == null ? "no QT drive set (assuming " + QuantumDrives.Assumed.Key + ")" : drive.Key + ", " + (drive.Speed / 1e6).ToString("0", CultureInfo.InvariantCulture) + " Mm/s",
                Color = WorkerColor(c.Name),
            };
            w.Rows.AddRange(RunRows(c.Name));
            var mine = core.Trips.Where(t => t.Worker == c.Name).ToList();
            for (var k = 0; k < mine.Count; k++)
            {
                w.Rows.Add(new PlanRow
                {
                    Label = k == 0 ? "Next" : "Plan " + (k + 1),
                    Route = RouteText(mine[k]),
                    Batches = mine[k].Batches + "/" + c.Capacity,
                    BatchCount = mine[k].Batches,
                    Gm = mine[k].Distance,
                    Seconds = mine[k].Seconds,
                    Level = "plan",
                    Stops = mine[k].Stops.Select(s => s.Id).ToList(),
                    Trip = mine[k],
                });
            }
            plan.Workers.Add(w);
        }

        var planned = plan.Workers.SelectMany(w => w.Planned).ToList();
        if (flying.Count == 0)
        {
            plan.Summary = "Nobody is flying. Tick Active and set a Batch Capacity for the crew in Options → Party…";
        }
        else if (planned.Count == 0)
        {
            plan.Summary = "No open batches to pick up.";
        }
        else
        {
            var longest = plan.Workers.OrderByDescending(w => w.Planned.Sum(r => r.Seconds)).First();
            plan.Summary = planned.Count + " trips planned, " + Gm(planned.Sum(r => r.Gm)) + " in total for " + core.Batches + " batches. Longest: "
                + longest.Name + " with " + Gm(longest.Planned.Sum(r => r.Gm)) + ", about " + FlightSpan(longest.Planned.Sum(r => r.Seconds)) + " of flying.";
        }

        plan.Signature = plan.Summary + "¦" + (Theme.Dark ? "dark" : "light") + "¦" + string.Join("¦", plan.Workers.SelectMany(w =>
            w.Rows.Select(r => w.Name + "|" + w.Capacity + "|" + r.Label + "|" + r.Route + "|" + r.Batches + "|" + r.Level + "|" + r.Gm.ToString("F1", CultureInfo.InvariantCulture) + "|" + r.Seconds.ToString("F0", CultureInfo.InvariantCulture) + "|" + w.Drive)));
        return plan;
    }

    /// <summary>Shows the plan on its pages. It only runs while the Trip Planner tab is on screen.</summary>
    void RefreshPlanner()
    {
        var plan = GetPlan();
        _planSummary.Text = plan.Summary;
        _planNote.Text = plan.Note;
        if (plan.Signature == _planShown) return;   // nothing changed, so nothing flickers
        _planShown = plan.Signature;

        var selected = _planTabs.SelectedTab?.Text ?? "Overview";
        _planTabs.SuspendLayout();
        foreach (var old in _planTabs.TabPages.Cast<TabPage>().ToList())
        {
            _planTabs.TabPages.Remove(old);
            old.Dispose();
        }
        _planTabs.TabPages.Add(PlanPage("Overview", plan, null));
        foreach (var w in plan.Workers) _planTabs.TabPages.Add(PlanPage(w.Name, plan, w));
        var keep = _planTabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == selected);
        if (keep != null) _planTabs.SelectedTab = keep;
        _planTabs.ResumeLayout(true);
        _planTabs.RefreshStyle();
    }

    /// <summary>A page: the map on the left, the tables on the right. only = null is the overview with everybody.</summary>
    TabPage PlanPage(string title, PlanView plan, PlanWorkerView? only)
    {
        var page = new TabPage(title) { BackColor = Theme.Window };
        // the map on the left, square and as large as the page allows, the tables on the right
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var map = new PyroMapView { Dock = DockStyle.Top, Margin = new Padding(0, 0, 12, 0) };
        var host = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0) };
        host.Resize += (_, _) => FitPlanBlocks(host);

        var workers = only == null ? plan.Workers : new List<PlanWorkerView> { only };
        if (only == null)
        {
            // everybody: one colour per worker, his next trip in bold
            foreach (var w in workers)
            {
                var k = 0;
                foreach (var r in w.Planned)
                {
                    map.Routes.Add(new MapRoute { Label = k == 0 ? w.Name : "", Color = w.Color, Stops = r.Stops, Bold = k == 0, Tag = r });
                    k++;
                }
            }
        }
        else
        {
            // one worker: the colours of TripColor, the next one in bold (click a planned trip in the table to bold that one)
            var k = 0;
            var n = only.Planned.Count;
            foreach (var r in only.Planned)
            {
                map.Routes.Add(new MapRoute { Label = r.Label + "  " + Gm(r.Gm), Color = TripColor(k, n), Stops = r.Stops, Bold = k == 0, Tag = r });
                k++;
            }
        }

        Action<PlanRow>? onSelect = null;
        if (only != null)
        {
            onSelect = row =>
            {
                foreach (var route in map.Routes) route.Bold = ReferenceEquals(route.Tag, row);
                map.Invalidate();
            };
        }
        foreach (var w in workers) host.Controls.Add(PlanBlock(w, onSelect));
        FitPlanBlocks(host);

        // the map is a square: as high as the page, but at most 55% of its width (the tables keep the rest)
        void FitMap()
        {
            var room = Math.Max(320, t.ClientSize.Height - t.Padding.Vertical);
            var side = Math.Max(320, Math.Min(room, (int)((t.ClientSize.Width - t.Padding.Horizontal) * 0.55)));
            t.ColumnStyles[0].Width = side + map.Margin.Horizontal;
            map.Height = side;
        }
        t.Resize += (_, _) => FitMap();
        FitMap();
        t.Controls.Add(map, 0, 0);
        t.Controls.Add(host, 1, 0);
        page.Controls.Add(t);
        return page;
    }

    static void FitPlanBlocks(FlowLayoutPanel host)
    {
        var width = Math.Max(400, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control c in host.Controls) c.Width = width;
    }

    /// <summary>A title and the table of one crew member: the runs so far, then the planned trips.</summary>
    Control PlanBlock(PlanWorkerView w, Action<PlanRow>? onSelect)
    {
        var block = new Panel { Margin = new Padding(0, 0, 0, 14) };
        var label = new Label
        {
            Text = PlanTitle(w),
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.BottomLeft,
        };
        var height = label.Height;

        if (w.Rows.Count > 0)
        {
            var grid = new DataGridView();
            StyleGrid(grid);
            grid.Dock = DockStyle.Top;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ScrollBars = ScrollBars.None;
            grid.Columns.Add(Col("Trip", 80, true));
            grid.Columns.Add(Col("Route (Ruin, then these, then back to Ruin)", 460, true, DataGridViewContentAlignment.MiddleLeft, true));
            grid.Columns.Add(Col("Batches", 80, true));
            grid.Columns.Add(Col("Distance", 110, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Time", 90, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Gm / Batch", 100, true, DataGridViewContentAlignment.MiddleRight));
            var boldFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            var plannedCount = w.Planned.Count;
            var plannedIndex = 0;
            foreach (var r in w.Rows)
            {
                var idx = grid.Rows.Add(
                    r.Label,
                    r.Route,
                    r.Batches,
                    r.Gm > 0 ? Gm(r.Gm) : "",
                    TimeText(r),
                    r.Gm > 0 && r.BatchCount > 0 ? (r.Gm / r.BatchCount).ToString("N1", CultureInfo.InvariantCulture) : "");
                var row = grid.Rows[idx];
                row.Tag = r;
                if (RowColor(r) is { } color) row.DefaultCellStyle.BackColor = color;
                if (r.Label == "Next") row.DefaultCellStyle.Font = boldFont;
                if (r.IsPlan && onSelect != null)   // a worker's own page (the Overview keeps one colour per worker)
                {
                    // the Trip cell has the colour of the trip's line on the map
                    var c = TripColor(plannedIndex++, plannedCount);
                    var cell = row.Cells[0].Style;
                    cell.BackColor = c;
                    cell.ForeColor = Color.Black;
                    cell.SelectionBackColor = c;
                    cell.SelectionForeColor = Color.Black;
                }
            }
            if (onSelect != null)
            {
                grid.SelectionChanged += (_, _) =>
                {
                    if (grid.CurrentRow?.Tag is PlanRow picked && picked.IsPlan) onSelect(picked);
                };
            }
            grid.Height = grid.ColumnHeadersHeight + w.Rows.Count * grid.RowTemplate.Height + 3;
            height += grid.Height;
            block.Controls.Add(grid);   // added first, so the title sits above it
        }

        block.Controls.Add(label);
        block.Height = height;
        return block;
    }

    static string Gm(double v) => v.ToString("N1", CultureInfo.InvariantCulture) + " Gm";

    /// <summary>m:ss or h:mm:ss.</summary>
    static string FlightSpan(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? (int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
    }

    /// <summary>A run: the time it really took. A planned trip: the estimated flight time, with a ~ in front.</summary>
    static string TimeText(PlanRow r) => r.Seconds <= 0 ? "" : (r.IsPlan ? "~" : "") + FlightSpan(r.Seconds);
}
