using System.Globalization;

namespace HaulingTracker;

// The Trip Planner tab: divides the open batches over the whole crew and shows one table of trips for every crew member.
// The crew is the active party (Options -> Party), each with the number of batches his ship holds.
public sealed partial class MainForm
{
    const int DefaultShipCapacity = 3;
    const int MaxShipCapacity = 24;

    sealed class PlanWorkerView
    {
        public string Name = "";
        public int Capacity;
        public List<PlannedTrip> Trips = new();
        public int Batches => Trips.Sum(t => t.Batches);
        public double Gm => Trips.Sum(t => t.Distance);
    }

    sealed class PlanView
    {
        public string Key = "";
        public string Summary = "";
        public string Note = "";
        public List<PlanWorkerView> Workers = new();
    }

    readonly Button _planBtn = new();
    readonly Label _planSummary = new();
    readonly Label _planNote = new();
    readonly FlowLayoutPanel _planHost = new();   // one block (title and table) for every crew member
    string _planShown = "";                       // what the blocks show now, so they are only rebuilt when it changes
    PlanView? _planCache;

    Control BuildPlannerPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));    // title
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // hint
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));    // button
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // summary
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));    // note
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // the tables

        t.Controls.Add(Caption("Trip Planner", "Divides the open batches over the crew, so everybody flies as little as possible and you finish together"), 0, 0);

        var hint = new Label
        {
            Text = "Who flies and how many batches each ship holds is set in Options → Party…  (Active and Batch Capacity).",
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
            _planCache = null;
            _planShown = "";
            RefreshPlanner();
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        bar.Controls.Add(_planBtn);
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

        _planHost.Dock = DockStyle.Fill;
        _planHost.FlowDirection = FlowDirection.TopDown;
        _planHost.WrapContents = false;
        _planHost.AutoScroll = true;
        _planHost.Margin = new Padding(0);
        _planHost.Resize += (_, _) => FitPlanBlocks();
        t.Controls.Add(_planHost, 0, 5);
        return t;
    }

    int ShipCapacityOf(string name) =>
        _data.ShipCapacity.TryGetValue(name, out var c) ? Math.Clamp(c, 0, MaxShipCapacity) : DefaultShipCapacity;

    List<string> ActiveCrew() => _data.Party.Where(n => !_data.InactiveParty.Any(x => SameName(x, n))).ToList();

    static string RouteText(PlannedTrip trip) =>
        string.Join("  →  ", trip.Stops.Select(s => PyroDistances.NameOf(s.Id) + " ×" + s.Batches));

    static string PlanTitle(PlanWorkerView w) =>
        w.Name + "  –  ship holds " + w.Capacity + (w.Capacity == 1 ? " batch" : " batches") + "  –  "
        + (w.Trips.Count == 0 ? "no trips" : w.Trips.Count + (w.Trips.Count == 1 ? " trip, " : " trips, ") + w.Batches + " batches, " + Gm(w.Gm));

    /// <summary>The plan for the open batches of the selected session. Calculated again only when the batches or the crew change.</summary>
    PlanView GetPlan()
    {
        var crew = ActiveCrew().Select(n => (Name: n, Capacity: ShipCapacityOf(n))).ToList();
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

        var key = string.Join("|", crew.Select(c => c.Name + ":" + c.Capacity))
            + "#" + string.Join("|", remaining.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value))
            + "#" + string.Join("|", unknown);
        if (_planCache != null && _planCache.Key == key) return _planCache;

        var plan = new PlanView { Key = key };
        var flying = crew.Where(c => c.Capacity > 0).ToList();
        var batches = remaining.Values.Sum();
        var trips = TripPlanner.Plan(remaining, crew);
        foreach (var c in flying)
        {
            plan.Workers.Add(new PlanWorkerView { Name = c.Name, Capacity = c.Capacity, Trips = trips.Where(t => t.Worker == c.Name).ToList() });
        }

        if (flying.Count == 0)
        {
            plan.Summary = "Nobody is flying. Tick Active and set a Batch Capacity for the crew in Options → Party…";
        }
        else if (trips.Count == 0)
        {
            plan.Summary = "No open batches to pick up.";
        }
        else
        {
            var longest = plan.Workers.OrderByDescending(w => w.Gm).First();
            plan.Summary = trips.Count + " trips, " + Gm(plan.Workers.Sum(w => w.Gm)) + " in total for " + batches + " batches. Longest: "
                + longest.Name + " with " + Gm(longest.Gm) + ".";
        }

        plan.Note = "Planning every open batch, whoever is assigned to the location now.";
        if (unknown.Count > 0) plan.Note += " Not in the distance table, so left out: " + string.Join(", ", unknown) + ".";

        _planCache = plan;
        return plan;
    }

    /// <summary>Shows the plan: one table for every crew member. It only runs while the Trip Planner tab is on screen.</summary>
    void RefreshPlanner()
    {
        var plan = GetPlan();
        _planSummary.Text = plan.Summary;
        _planNote.Text = plan.Note;

        var shown = plan.Key + (Theme.Dark ? "#dark" : "#light");
        if (shown == _planShown) return;   // nothing changed, so nothing flickers
        _planShown = shown;

        _planHost.SuspendLayout();
        foreach (var old in _planHost.Controls.Cast<Control>().ToList())
        {
            _planHost.Controls.Remove(old);
            old.Dispose();
        }
        _planHost.BackColor = Theme.Window;
        foreach (var w in plan.Workers) _planHost.Controls.Add(PlanBlock(w));
        FitPlanBlocks();
        _planHost.ResumeLayout(true);
    }

    void FitPlanBlocks()
    {
        var width = Math.Max(400, _planHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control c in _planHost.Controls) c.Width = width;
    }

    /// <summary>A title and the table of trips of one crew member.</summary>
    Control PlanBlock(PlanWorkerView w)
    {
        var block = new Panel { BackColor = Theme.Window, Margin = new Padding(0, 0, 0, 14) };
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

        if (w.Trips.Count > 0)
        {
            var grid = new DataGridView();
            StyleGrid(grid);
            grid.Dock = DockStyle.Top;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ScrollBars = ScrollBars.None;
            grid.Columns.Add(Col("Trip", 60, true));
            grid.Columns.Add(Col("Route (Ruin, then these, then back to Ruin)", 480, true, DataGridViewContentAlignment.MiddleLeft, true));
            grid.Columns.Add(Col("Batches", 90, true));
            grid.Columns.Add(Col("Distance", 110, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Gm / Batch", 100, true, DataGridViewContentAlignment.MiddleRight));
            var number = 1;
            foreach (var trip in w.Trips)
            {
                var idx = grid.Rows.Add(
                    number++,
                    RouteText(trip),
                    trip.Batches + "/" + w.Capacity,
                    Gm(trip.Distance),
                    (trip.Distance / trip.Batches).ToString("N1", CultureInfo.InvariantCulture));
                grid.Rows[idx].Cells[2].Style.BackColor = trip.Batches >= w.Capacity ? DoneGreen : PartialYellow;   // green = full ship
            }
            grid.Height = grid.ColumnHeadersHeight + w.Trips.Count * grid.RowTemplate.Height + 3;
            height += grid.Height;
            block.Controls.Add(grid);   // added first, so the title sits above it
        }

        block.Controls.Add(label);
        block.Height = height;
        return block;
    }

    static string Gm(double v) => v.ToString("N1", CultureInfo.InvariantCulture) + " Gm";
}
