using System.Globalization;

namespace HaulingTracker;

// The Trip Planner tab: how to pick up the open batches with a ship that holds a few batches, flying as little as possible.
public sealed partial class MainForm
{
    const string PlanEveryone = "Everyone";

    readonly ThemedCombo _planWorker = new();
    readonly NumericUpDown _planCapacity = new();
    readonly Button _planBtn = new();
    readonly Label _planSummary = new();
    readonly Label _planNote = new();
    readonly DataGridView _planGrid = new();
    bool _planFilling;   // the controls are being filled by the program, not by you

    Control BuildPlannerPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        t.Controls.Add(Caption("Trip Planner", "Picks up every open batch in the fewest Gm for a ship that holds a few batches"), 0, 0);

        static Label Lbl(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 9, 4, 0) };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        bar.Controls.Add(Lbl("Plan for"));
        _planWorker.Width = 200;
        _planWorker.Margin = new Padding(0, 5, 0, 0);
        bar.Controls.Add(_planWorker);
        bar.Controls.Add(Lbl("Ship holds"));
        _planCapacity.Minimum = 1;
        _planCapacity.Maximum = 24;
        _planCapacity.Value = 3;
        _planCapacity.Width = 60;
        _planCapacity.Tag = "input";
        _planCapacity.Margin = new Padding(0, 5, 0, 0);
        bar.Controls.Add(_planCapacity);
        bar.Controls.Add(Lbl("batches"));
        StyleButton(_planBtn, "Plan Trips");
        _planBtn.Margin = new Padding(16, 4, 0, 4);
        bar.Controls.Add(_planBtn);
        t.Controls.Add(bar, 0, 1);

        _planSummary.Dock = DockStyle.Fill;
        _planSummary.TextAlign = ContentAlignment.MiddleLeft;
        _planSummary.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        t.Controls.Add(_planSummary, 0, 2);
        _planNote.Dock = DockStyle.Fill;
        _planNote.TextAlign = ContentAlignment.MiddleLeft;
        _planNote.ForeColor = Color.DimGray;
        _planNote.Tag = "muted";
        t.Controls.Add(_planNote, 0, 3);

        StyleGrid(_planGrid);
        _planGrid.Dock = DockStyle.Fill;
        _planGrid.AllowUserToAddRows = false;
        _planGrid.AllowUserToDeleteRows = false;
        _planGrid.Columns.Add(Col("Trip", 60, true));
        _planGrid.Columns.Add(Col("Route (Ruin, then these, then back to Ruin)", 560, true, DataGridViewContentAlignment.MiddleLeft, true));
        _planGrid.Columns.Add(Col("Batches", 90, true));
        _planGrid.Columns.Add(Col("Distance", 110, true, DataGridViewContentAlignment.MiddleRight));
        _planGrid.Columns.Add(Col("Gm / Batch", 100, true, DataGridViewContentAlignment.MiddleRight));
        t.Controls.Add(_planGrid, 0, 4);

        _planWorker.SelectedIndexChanged += (_, _) =>
        {
            if (_planFilling) return;
            LoadPlanCapacity();
            RefreshPlanner();
        };
        _planCapacity.ValueChanged += (_, _) =>
        {
            if (_planFilling) return;
            _data.ShipCapacity[PlanKey()] = (int)_planCapacity.Value;
            Storage.Save(_data);
            RefreshPlanner();
        };
        _planBtn.Click += (_, _) => RefreshPlanner();
        return t;
    }

    /// <summary>The key the ship size is saved under: the worker, or * for everyone.</summary>
    string PlanKey() => _planWorker.SelectedItem is string w && w != PlanEveryone ? w : "*";

    string? PlanWorker() => _planWorker.SelectedItem is string w && w != PlanEveryone ? w : null;

    void LoadPlanCapacity()
    {
        _planFilling = true;
        try
        {
            _planCapacity.Value = _data.ShipCapacity.TryGetValue(PlanKey(), out var c) ? Math.Clamp(c, 1, 24) : 3;
        }
        finally
        {
            _planFilling = false;
        }
    }

    void FillPlanWorkers()
    {
        var keep = _planWorker.SelectedItem as string ?? PlanEveryone;
        var names = new List<string> { PlanEveryone };
        names.AddRange(_data.Party.Where(n => !_data.InactiveParty.Any(x => SameName(x, n))));
        if (_planWorker.Items.Count == names.Count && names.Select((n, i) => (string?)_planWorker.Items[i] == n).All(same => same)) return;   // nothing changed, leave an open drop-down alone
        _planFilling = true;
        try
        {
            _planWorker.Items.Clear();
            foreach (var n in names) _planWorker.Items.Add(n);
            var idx = names.FindIndex(n => n == keep);
            _planWorker.SelectedIndex = idx < 0 ? 0 : idx;
        }
        finally
        {
            _planFilling = false;
        }
        LoadPlanCapacity();
    }

    /// <summary>Plans the trips for the open batches of the selected session. It only runs while the Trip Planner tab is on screen.</summary>
    void RefreshPlanner()
    {
        FillPlanWorkers();
        _planGrid.Rows.Clear();
        _planSummary.Text = "";
        _planNote.Text = "";

        var worker = PlanWorker();
        var capacity = (int)_planCapacity.Value;
        var remaining = new Dictionary<string, int>();
        var unknown = new List<string>();
        if (_inputs != null)
        {
            foreach (var r in _openRows)
            {
                var left = r.Missions - r.Loaded;   // batches not picked up yet
                if (left <= 0) continue;
                if (worker != null)
                {
                    _inputs.Rows.TryGetValue(r.Zone, out var input);
                    var assigned = input?.Worker?.Trim() ?? "";
                    if (assigned.Length > 0 && !SameName(assigned, worker)) continue;   // somebody else's location
                }
                var id = StationOf(r.Zone);
                if (id == null)
                {
                    unknown.Add(LocationName(r.Zone) + " (" + left + ")");
                    continue;
                }
                remaining[id] = remaining.GetValueOrDefault(id) + left;
            }
        }

        var trips = TripPlanner.Plan(remaining, capacity);
        var batches = remaining.Values.Sum();
        if (trips.Count == 0)
        {
            _planSummary.Text = "No open batches to pick up.";
        }
        else
        {
            var total = trips.Sum(t => t.Distance);
            var separate = TripPlanner.SeparateTrips(remaining, capacity);
            _planSummary.Text = trips.Count + " trips, " + Gm(total) + " for " + batches + " batches (" + Gm(total / batches) + " per batch)."
                + (separate > total + 0.05 ? " One trip per location would be " + Gm(separate) + "." : "");
        }

        var note = worker == null
            ? "Planning every open location."
            : "Planning the locations assigned to " + worker + " and the ones nobody is assigned to.";
        if (unknown.Count > 0) note += " Not in the distance table, so left out: " + string.Join(", ", unknown) + ".";
        _planNote.Text = note;

        var number = 1;
        foreach (var trip in trips)
        {
            var route = string.Join("  →  ", trip.Stops.Select(s => PyroDistances.NameOf(s.Id) + " ×" + s.Batches));
            var idx = _planGrid.Rows.Add(
                number++,
                route,
                trip.Batches + "/" + capacity,
                Gm(trip.Distance),
                (trip.Distance / trip.Batches).ToString("N1", CultureInfo.InvariantCulture));
            _planGrid.Rows[idx].Cells[2].Style.BackColor = trip.Batches >= capacity ? DoneGreen : PartialYellow;   // green = full ship
        }
    }

    static string Gm(double v) => v.ToString("N1", CultureInfo.InvariantCulture) + " Gm";
}
