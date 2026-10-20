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
        public bool Locked;              // a planned trip that is locked to its worker (his Next)
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
    readonly Button _resetTripsBtn = new();
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
            Text = "Who flies, how many batches each ship holds and which quantum drive each has is set in the Party table on the front page. A run shows the time it really took; a planned trip shows ~ the estimated flying time (spool time + distance / drive speed for every jump, no loading). Green = done, orange = some cargo lost, red = all lost, yellow = on the way. A worker's Next trip is locked once you press Assign Workers to Stops (or give him stops by hand on the front page): planning again leaves it alone and plans the rest around it. Reset Trips takes it all away.",
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
        StyleButton(_resetTripsBtn, "Reset Trips");
        _resetTripsBtn.Margin = new Padding(8, 5, 0, 5);
        _resetTripsBtn.Click += (_, _) => ResetTrips();
        bar.Controls.Add(_planBtn);
        bar.Controls.Add(_assignBtn);
        bar.Controls.Add(_resetTripsBtn);
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
    /// Puts the workers of the plan on the pickup board and locks every worker's Next trip to him. Every stop nobody has picked up from yet
    /// goes to the worker who takes the most batches there in the plan; a stop of somebody's Next trip goes to him first (a tie goes to the
    /// one who flies it first). Stops that are finished, partly or fully picked up keep their worker, so runs on the way and finished runs
    /// are never touched.
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
        // the first trip of every worker is his Next: it is locked to him from now on
        var next = new Dictionary<string, PlannedTrip>();
        foreach (var trip in core.Trips)
        {
            if (!next.ContainsKey(trip.Worker)) next[trip.Worker] = trip;
        }
        foreach (var (worker, trip) in next)
            _inputs.LockedTrips[LockKey(worker) ?? worker] = trip.Stops.Select(s => new LockedStop { Id = s.Id, Batches = s.Batches }).ToList();

        var batches = new Dictionary<string, Dictionary<string, int>>();   // station id -> worker -> weight
        var firstSeen = new Dictionary<string, List<string>>();            // station id -> the workers in the order of the plan
        foreach (var trip in core.Trips)
        {
            var weight = ReferenceEquals(next[trip.Worker], trip) ? 1000 : 1;   // a Next trip wins its stations
            foreach (var (id, n) in trip.Stops)
            {
                if (!batches.TryGetValue(id, out var per)) batches[id] = per = new Dictionary<string, int>();
                per[trip.Worker] = per.GetValueOrDefault(trip.Worker) + n * weight;
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
            if (PickupStarted(r)) continue;   // already picked up from: that worker stays, the runs and the debts keep their history
            var id = StationOf(r.Zone);
            if (id == null || !choice.TryGetValue(id, out var worker)) continue;
            GetRow(r.Zone).Worker = worker;
            assigned++;
        }
        Storage.Save(_data);
        SetStatus("Assigned the workers of the plan to " + assigned + (assigned == 1 ? " stop" : " stops") + " and locked " + next.Count + (next.Count == 1 ? " Next trip." : " Next trips."));
        _renderedVersion = -1;
        RefreshFromLog();
        RefreshPlanner();
    }

    // ------------------------------------------------------------------ locked trips

    /// <summary>The name a worker has in the locked trips of this session, or null when he has none.</summary>
    string? LockKey(string name) => _inputs?.LockedTrips.Keys.FirstOrDefault(k => SameName(k, name));

    /// <summary>The stations that are part of a locked trip of somebody who is flying now. Their workers are left alone when the party changes.</summary>
    HashSet<string> LockedStations()
    {
        var ids = new HashSet<string>();
        if (_inputs == null) return ids;
        foreach (var name in ActiveCrew())
        {
            var key = LockKey(name);
            if (key == null) continue;
            foreach (var s in _inputs.LockedTrips[key]) ids.Add(s.Id);
        }
        return ids;
    }

    /// <summary>
    /// A worker was given a stop, or taken off one, on the front page: his locked trip follows the stops he has there now. The stops that
    /// are still his stay in it, other stops of his are added while his ship has room, and with no stops left the lock is gone.
    /// What you do by hand always wins over the plan.
    /// </summary>
    void RebuildLock(string name, bool create)
    {
        if (_inputs == null || string.IsNullOrWhiteSpace(name)) return;
        if (!create && LockKey(name) == null) return;   // taking him off a stop never starts a lock
        var left = new Dictionary<string, int>();   // station id -> batches not picked up yet at the stops of this worker
        foreach (var r in _pickupRows)
        {
            if (!SameName(GetRow(r.Zone).Worker ?? "", name)) continue;
            var id = StationOf(r.Zone);
            var n = r.Missions - r.Loaded;
            if (id == null || n <= 0) continue;
            left[id] = left.GetValueOrDefault(id) + n;
        }
        var key = LockKey(name);
        var room = ShipCapacityOf(_data.Party.FirstOrDefault(p => SameName(p, name)) ?? name);
        var stops = new List<LockedStop>();
        if (key != null)
        {
            foreach (var s in _inputs.LockedTrips[key])
            {
                var take = Math.Min(Math.Min(s.Batches, left.GetValueOrDefault(s.Id)), room);
                if (take <= 0) continue;
                stops.Add(new LockedStop { Id = s.Id, Batches = take });
                room -= take;
            }
        }
        foreach (var (id, n) in left)
        {
            if (room <= 0) break;
            if (stops.Any(s => s.Id == id)) continue;
            var take = Math.Min(n, room);
            stops.Add(new LockedStop { Id = id, Batches = take });
            room -= take;
        }
        if (stops.Count == 0)
        {
            if (key != null) _inputs.LockedTrips.Remove(key);
        }
        else
        {
            _inputs.LockedTrips[key ?? name] = stops;
        }
    }

    /// <summary>Takes everybody off the planned trips: no locked trips, and no worker on the stops nobody has picked up from yet. Asks first.</summary>
    void ResetTrips()
    {
        if (_inputs == null) return;
        var answer = MessageBox.Show(this,
            "Take every worker off the planned trips?\n\nThe locked Next trips are cleared and the workers are removed from the stops nobody has picked up from yet. Stops that are picked up from, and finished runs, are not touched.",
            "Reset Trips", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        _inputs.LockedTrips.Clear();
        foreach (var r in _pickupRows)
        {
            if (PickupStarted(r)) continue;
            GetRow(r.Zone).Worker = "";
        }
        Storage.Save(_data);
        SetStatus("Reset the trips: nobody is locked or assigned to the open stops.");
        _planCore = null;
        _planShown = "";
        _renderedVersion = -1;
        RefreshFromLog();
        RefreshPlanner();
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

    /// <summary>
    /// The trips for the open batches. Locked trips (a worker's Next) are taken out first, clipped to what is still to pick up, and the rest is
    /// planned around them. When a locked trip has been picked up completely, the worker's next planned trip is locked in its place, so his
    /// Next is always one that does not change. Only worked out again when the batches, the crew or the locks change.
    /// </summary>
    PlanCore CoreFor(List<(string Name, int Capacity, QtDrive Drive)> crew, Dictionary<string, int> open, List<string> unknown)
    {
        for (var pass = 0; ; pass++)
        {
            var remaining = new Dictionary<string, int>(open);
            var locks = new List<PlannedTrip>();
            var emptied = new List<string>();   // workers whose locked trip is picked up completely
            if (_inputs != null)
            {
                foreach (var c in crew.Where(x => x.Capacity > 0))
                {
                    var lockKey = LockKey(c.Name);
                    if (lockKey == null) continue;
                    var kept = new List<(string Id, int Batches)>();
                    var room = c.Capacity;
                    foreach (var s in _inputs.LockedTrips[lockKey])
                    {
                        var take = Math.Min(Math.Min(s.Batches, remaining.GetValueOrDefault(s.Id)), room);
                        if (take <= 0) continue;
                        kept.Add((s.Id, take));
                        room -= take;
                        remaining[s.Id] -= take;
                    }
                    if (kept.Count == 0) emptied.Add(c.Name);
                    else locks.Add(TripPlanner.Fixed(c.Name, kept, c.Drive));
                }
            }

            var key = string.Join("|", crew.Select(c => c.Name + ":" + c.Capacity + ":" + c.Drive.Key))
                + "#" + string.Join("|", remaining.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value))
                + "#" + string.Join("|", unknown)
                + "#" + string.Join("|", locks.Select(t => t.Worker + "=" + string.Join(",", t.Stops.Select(s => s.Id + "x" + s.Batches))));
            if (emptied.Count == 0 && _planCore != null && _planCore.Key == key) return _planCore;

            var note = "Planning every open batch, whoever is assigned to the location now.";
            if (locks.Count > 0) note += " Locked Next trips stay as they are.";
            if (unknown.Count > 0) note += " Not in the distance table, so left out: " + string.Join(", ", unknown) + ".";
            var core = new PlanCore
            {
                Key = key,
                Trips = TripPlanner.Plan(remaining, crew, locks),
                Batches = remaining.Where(kv => kv.Value > 0).Sum(kv => kv.Value) + locks.Sum(t => t.Batches),
                Note = note,
            };
            _planCore = core;
            if (emptied.Count == 0 || pass > 0 || _inputs == null) return core;

            // a locked trip is picked up completely: the worker's next planned trip takes its place as his locked Next (nothing planned = no lock)
            foreach (var name in emptied)
            {
                var lockKey = LockKey(name);
                if (lockKey == null) continue;
                var next = core.Trips.FirstOrDefault(t => t.Worker == name && !t.Locked);
                if (next == null) _inputs.LockedTrips.Remove(lockKey);
                else _inputs.LockedTrips[lockKey] = next.Stops.Select(s => new LockedStop { Id = s.Id, Batches = s.Batches }).ToList();
            }
            Storage.Save(_data);
        }
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

        var core = CoreFor(crew, remaining, unknown);

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
                    Locked = mine[k].Locked,
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
            w.Rows.Select(r => w.Name + "|" + w.Capacity + "|" + r.Label + "|" + r.Locked + "|" + r.Route + "|" + r.Batches + "|" + r.Level + "|" + r.Gm.ToString("F1", CultureInfo.InvariantCulture) + "|" + r.Seconds.ToString("F0", CultureInfo.InvariantCulture) + "|" + w.Drive)));
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
            var side = Math.Max(320, Math.Min(room, (int)((t.ClientSize.Width - t.Padding.Horizontal) * 0.5)));
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
        var width = Math.Max(200, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
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
            grid.Columns.Add(Col("Trip", 110, true));
            grid.Columns.Add(Col("Route (Ruin, then these, then back to Ruin)", 430, true, DataGridViewContentAlignment.MiddleLeft, true));
            grid.Columns.Add(Col("Batches", 80, true));
            grid.Columns.Add(Col("Distance", 110, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Time", 90, true, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(Col("Gm / Batch", 100, true, DataGridViewContentAlignment.MiddleRight));
            FitColumns(grid);   // the columns share the width, so nothing is cut off or has to scroll sideways
            var boldFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            var plannedCount = w.Planned.Count;
            var plannedIndex = 0;
            foreach (var r in w.Rows)
            {
                var idx = grid.Rows.Add(
                    RowLabel(r),
                    r.Route,
                    r.Batches,
                    r.Gm > 0 ? Gm(r.Gm) : "",
                    TimeText(r),
                    r.Gm > 0 && r.BatchCount > 0 ? (r.Gm / r.BatchCount).ToString("N1", CultureInfo.InvariantCulture) : "");
                var row = grid.Rows[idx];
                row.Tag = r;
                if (RowColor(r) is { } color) row.DefaultCellStyle.BackColor = color;
                if (r.Label == "Next") row.DefaultCellStyle.Font = boldFont;
                if (r.Label == "Next" && onSelect == null)
                {
                    // the Overview: the whole Next row of a worker has his colour
                    var st = row.DefaultCellStyle;
                    st.BackColor = w.Color;
                    st.ForeColor = TextOn(w.Color);
                    st.SelectionBackColor = w.Color;
                    st.SelectionForeColor = TextOn(w.Color);
                }
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

    /// <summary>The text of the Trip cell: a locked Next says so.</summary>
    static string RowLabel(PlanRow r) => r.Locked ? r.Label + " (locked)" : r.Label;

    /// <summary>Black or white, whichever reads better on this colour.</summary>
    static Color TextOn(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140 ? Color.Black : Color.White;

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
