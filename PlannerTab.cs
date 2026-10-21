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

    /// <summary>A trip that is not the next one: the worker's colour, half faded into the table colour.</summary>
    static Color Faded(Color c) => Color.FromArgb(
        (c.R * 55 + Theme.Cell.R * 45) / 100, (c.G * 55 + Theme.Cell.G * 45) / 100, (c.B * 55 + Theme.Cell.B * 45) / 100);
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
        public int BatchCount;           // batches picked up or planned
        public double Gm;
        public double Seconds;           // a run: the time it really took; a planned trip: the estimated flight time
        public string Level = "plan";    // done, little, all, active, plan
        public List<string> Stops = new();   // station ids in flying order
        public PlannedTrip? Trip;
        public bool Locked;              // a planned trip that is locked to its worker (his Current)
        public bool First;               // the trip the worker is flying right now (his run in progress, or his first planned trip when he has none): the whole row has his colour
        public decimal Value;            // a run: what its batches are worth; a planned trip: the batches at each stop times the average value of a batch there
        public bool OnMap;               // a run in progress that is drawn on the map by itself (Ruin, its pickups, back to Ruin)
        public bool Hidden;              // a planned trip that is shown inside the Current row (the continuation of the run in progress), not as a row of its own
        public double Flight;            // the estimated flying time of the whole route (Ruin, the stops, back to Ruin) with the drive of the worker
        public bool Solid;               // a planned trip that is flown first (solid line on the map); the later ones are dotted
        public string? From;             // a trip that continues a run in progress starts at this station
        public List<StopLine> Lines = new();   // the route one stop at a time (the Current Route table)
        public int Here = -1;            // the line where the worker is now (his Current location): the worker colour follows it
        public bool IsPlan => Level == "plan";
    }

    /// <summary>One line of the Current Route table: a stop of the route, what is done there, and the leg that leads to it.</summary>
    sealed class StopLine
    {
        public string Id = "";           // station id ("" = a place that is not in the distance table)
        public string Name = "";
        public List<string> Actions = new();   // collect ×2, drop Waste, collected ×1 ✓ ...
        public bool Stop = true;         // false: the start or the end at Ruin (or where he starts from), which has no number
        public bool Done;                // collected already (it is on board)
        public int Batches;              // batches collected, or still to collect, here
        public decimal Value;
        public double Gm;                // the leg from the line before it
        public double Seconds;           // the estimated flying time of that leg
        public string Text => Actions.Count == 0 ? Name : Name + " (" + string.Join(" | ", Actions) + ")";
    }

    sealed class PlanWorkerView
    {
        public string Name = "";
        public int Capacity;
        public string DriveNote = "";    // only when no drive is set
        public string Drive = "";        // the quantum drive, or that none is set
        public Color Color;
        public int RunsSoFar;            // all his runs of the session, also the finished ones that are no longer listed in his table
        public string LocationId = PyroDistances.Ruin;   // his Current location (station id): where he is now
        public bool LocationByHand;      // set in the drop-down (the logs take over again at his next pickup, delivery or hand-in)
        public string AutoLocationId = PyroDistances.Ruin;   // where the logs say he is
        public List<PlanRow> Rows = new();
        public List<PlanRow> Planned => Rows.Where(r => r.IsPlan).ToList();
        public List<PlanRow> MapRows => Rows.Where(r => r.IsPlan || r.OnMap).ToList();   // the lines on the map: the planned trips, and the run in progress when the plan adds nothing to it
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
        public HashSet<string> Settled = new();   // workers whose next trip has no pickup stop to lock (drop-off first, or nothing to do): no reason to plan again just for them
    }

    // the button sits in the top bar of the window, level with Options and Stop Session, while the Trip Planner tab is shown
    readonly ToolStripButton _lockBtn = new() { DisplayStyle = ToolStripItemDisplayStyle.Text, Text = "Next Stops: Unlocked", Padding = new Padding(8, 0, 8, 0), Visible = false };
    readonly Label _planNote = new();
    readonly Panel _planBody = new();              // the page: the map and the tables (rebuilt when the plan changes)
    string _planShown = "";                        // what the pages show now, so they are only rebuilt when it changes
    PlanCore? _planCore;

    Control BuildPlannerPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // a warning, only when there is one
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 62));     // the map and the tables
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));   // Cargo Runs
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 38));     // the copy of Cargo Runs

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
        t.Controls.Add(SectionTitle("Cargo Runs"), 0, 2);
        BuildPlanRunsGrid();
        t.Controls.Add(_planRunsGrid, 0, 3);
        return t;
    }

    /// <summary>The first stop of a trip that only has a drop-off there (or nothing to pick up) is not a pickup, so there is nothing to lock.</summary>
    static bool NothingToLock(PlannedTrip t) => t.Stops.Count == 0 || (t.Drops.Count > 0 && t.Stops[0].Id != t.Drops[0].Id);

    /// <summary>The text of the button follows the session: Unlocked (the plan is free) or Locked (the next stop of everybody is kept for him).</summary>
    void UpdateLockButton()
    {
        var on = _inputs?.NextStopsLocked == true;
        _lockBtn.Text = on ? "Next Stops: Locked" : "Next Stops: Unlocked";
        if (_lockBtn.Font.Bold != on) _lockBtn.Font = new Font(_lockBtn.Font, on ? FontStyle.Bold : FontStyle.Regular);
    }

    /// <summary>
    /// Next Stops: Locked / Unlocked. Unlocked (the start) the plan is free. Pressing it works the plan out and locks the next stop of everybody to him:
    /// planning again later only changes the other trips, so someone who joins gets his share of the rest. Pressing it again takes the locks away.
    /// </summary>
    void ToggleLocks()
    {
        if (_inputs == null)
        {
            SetStatus("There is no session to plan.");
            return;
        }
        var lockedNow = _inputs.NextStopsLocked;
        var answer = MessageBox.Show(this,
            lockedNow
                ? "Unlock the next stops?\n\nThe plan is free again, so it can change while people are on their way to a stop."
                : "Lock the next stops?\n\nThe plan is worked out now and the next stop of everybody is locked to him. Planning again later only changes the other trips.",
            "Next Stops", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        _planCore = null;
        _planShown = "";
        if (lockedNow)
        {
            _inputs.NextStopsLocked = false;
            _inputs.LockedTrips.Clear();
            Storage.Save(_data);
            SetStatus("The next stops are unlocked: the plan is free.");
            UpdateLockButton();
            RefreshPlanner();
            return;
        }

        _inputs.LockedTrips.Clear();
        GetPlan();   // nothing is locked yet, so this is the free plan
        var core = _planCore;
        _inputs.NextStopsLocked = true;
        var locked = 0;
        if (core != null)
        {
            var seen = new HashSet<string>();
            foreach (var trip in core.Trips)
            {
                if (!seen.Add(trip.Worker)) continue;   // the first trip of a worker is his Current
                if (HasActiveRun(trip.Worker))
                {
                    // he is flying a run now: the next stop of its continuation gets locked as soon as the plan is shown (see CoreFor)
                    if (LockKey(trip.Worker) == null) _inputs.LockedTrips[trip.Worker] = new List<LockedStop>();
                    continue;
                }
                if (NothingToLock(trip)) continue;
                _inputs.LockedTrips[LockKey(trip.Worker) ?? trip.Worker] = new List<LockedStop> { new() { Id = trip.Stops[0].Id, Batches = trip.Stops[0].Batches } };   // only his next stop
                locked++;
            }
        }
        _planCore = null;
        Storage.Save(_data);
        SetStatus("The next stops are locked" + (locked > 0 ? " (" + locked + (locked == 1 ? " worker)." : " workers).") : "."));
        UpdateLockButton();
        RefreshPlanner();
    }

    // ------------------------------------------------------------------ locked trips and who gets the credit

    /// <summary>The name a worker has in the locked trips of this session, or null when he has none.</summary>
    string? LockKey(string name) => _inputs?.LockedTrips.Keys.FirstOrDefault(k => SameName(k, name));

    /// <summary>
    /// A pickup that no log names (a member without a log) is credited to somebody once, and the credit is saved, so it never changes again (not when
    /// the program restarts either). Who gets it: the worker whose locked stop it is, then the worker whose planned trip needed a batch at that station
    /// (in the order of the plan, from before the batch was picked up), and else the only member without a log (a member with a log would have shown
    /// up in it). Nobody gets more batches than his ship holds. What the logs show always wins.
    /// </summary>
    void ClaimNewPickups()
    {
        if (_current == null || _inputs == null || _log == null) return;
        var since = _data.PlanCreditSince ??= DateTime.UtcNow;

        var fresh = new List<(string Key, string Station, DateTime Time)>();
        foreach (var m in _current.Missions)
        {
            foreach (var kv in m.ObjZone)
            {
                if (!WorkerTripBuilder.IsPickupStop(kv.Key)) continue;
                if (!m.Events.TryGetValue(kv.Key, out var ev) || ev.Count == 0) continue;
                var first = ev.Min(e => e.Time);
                if (first < since) continue;
                var key = m.Id + "|" + kv.Key;
                if (_inputs.PickupClaims.ContainsKey(key)) continue;
                if (WorkerTripBuilder.ActorAt(_log, true, first) != null) continue;   // a log shows who it was
                var station = StationOf(kv.Value);
                if (station == null) continue;
                fresh.Add((key, station, first));
            }
        }
        if (fresh.Count == 0) return;

        var crew = ActiveCrew().Where(n => ShipCapacityOf(n) > 0).ToList();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // claimed just now
        bool HasRoom(string n) => ShipCapacityOf(n) - (ActiveRunOf(n)?.Loaded ?? 0) - used.GetValueOrDefault(n) > 0;
        var logged = new HashSet<string>(_log.UnstowByPlayer.Keys.Concat(_log.PlatformByPlayer.Keys), StringComparer.OrdinalIgnoreCase);
        var core = _planCore;
        var changed = false;

        foreach (var group in fresh.GroupBy(f => f.Station))
        {
            // 1. whose locked stop this is (one entry for every batch of the lock)
            var locked = new List<string>();
            foreach (var n in crew)
            {
                var lk = LockKey(n);
                if (lk == null) continue;
                var stored = _inputs.LockedTrips[lk];
                if (stored.Count == 0 || stored[0].Id != group.Key) continue;
                for (var i = 0; i < stored[0].Batches; i++) locked.Add(n);
            }
            // 2. whose planned trip expects a batch here, in the order of the plan
            var queue = new List<string>();
            if (core != null)
            {
                foreach (var trip in core.Trips)
                    foreach (var (id, n) in trip.Stops)
                    {
                        if (id != group.Key) continue;
                        for (var i = 0; i < n; i++) queue.Add(trip.Worker);
                    }
            }
            // 3. members without a log: his pickups are the ones no log names
            var noLog = crew.Where(n => !logged.Any(l => SameName(l, n))).ToList();

            foreach (var f in group.OrderBy(x => x.Time))
            {
                string? who = null;
                foreach (var list in new[] { locked, queue })
                {
                    var i = list.FindIndex(n => HasRoom(n));
                    if (i < 0) continue;
                    who = list[i];
                    list.RemoveAt(i);
                    break;
                }
                if (who == null)
                {
                    var able = noLog.Where(HasRoom).ToList();
                    if (able.Count == 1) who = able[0];
                }
                if (who == null) continue;
                _inputs.PickupClaims[f.Key] = who;
                used[who] = used.GetValueOrDefault(who) + 1;
                changed = true;
            }
        }
        if (changed) Storage.Save(_data);
    }

    /// <summary>A copy of Cargo Runs for the Trip Planner tab, so the runs are in view while you plan. It cannot be edited (do that on the Session tab).</summary>
    void BuildPlanRunsGrid()
    {
        var g = _planRunsGrid;
        g.Dock = DockStyle.Fill;
        g.Margin = new Padding(0, 3, 0, 0);
        StyleGrid(g);
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.ReadOnly = true;
        g.Columns.Add(Col("Worker", 190, true));
        var run = Col("#", 36, true);
        run.Name = "num";
        g.Columns.Add(run);
        run.DisplayIndex = 0;
        g.Columns.Add(Col("Pickup Location & Time", 420, true, DataGridViewContentAlignment.MiddleLeft, true));
        g.Columns.Add(Col("Batches", 90, true));
        g.Columns.Add(Col("Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        g.Columns.Add(Col("Started", 76, true));
        g.Columns.Add(Col("Completed", 108, true));
        g.Columns.Add(Col("Time", 90, true));
        g.Columns.Add(Col("Flight time", 90, true));
        g.Columns.Add(Col("Lost Cargo", 116, true));
        FitColumns(g);
        g.ScrollBars = ScrollBars.Vertical;
    }

    int ShipCapacityOf(string name) =>
        _data.ShipCapacity.TryGetValue(name, out var c) ? Math.Clamp(c, 0, MaxShipCapacity) : DefaultShipCapacity;

    List<string> ActiveCrew() => _data.Party.Where(n => !_data.InactiveParty.Any(x => SameName(x, n))).ToList();

    /// <summary>
    /// The route of a planned trip as a short tag for the Queued routes table, with Ruin as the first and the last stop:
    /// <c>Ruin (collect) → Rod's Fuel (drop | collect ×2) → Endgame (collect ×1) → Ruin (drop ×3)</c> (the last one is the batches handed in).
    /// A trip that continues from a station (<c>From</c>) has no Ruin at the start. The details (cargo, SCU, containers) are in the Current Route table.
    /// </summary>
    static string RouteText(PlannedTrip trip)
    {
        var parts = new List<string>();
        var hasDrop = trip.Drops.Count > 0;
        var drop = hasDrop ? trip.Drops[0] : default;
        var firstStop = trip.Stops.Count > 0 ? trip.Stops[0].Id : null;
        if (trip.From != null)
        {
            if (firstStop != trip.From) parts.Add("From " + PyroDistances.NameOf(trip.From));
        }
        else parts.Add(hasDrop ? "Ruin (collect)" : "Ruin");   // the load for the first stop is collected at Ruin

        var dropShown = !hasDrop;
        if (hasDrop && firstStop != drop.Id)
        {
            parts.Add(PyroDistances.NameOf(drop.Id) + " (drop)");   // a stop with only the drop-off
            dropShown = true;
        }
        foreach (var (id, batches) in trip.Stops)
        {
            var actions = new List<string>();
            if (!dropShown && id == drop.Id)
            {
                actions.Add("drop");   // the load is unloaded first, then the pickups at that station are collected
                dropShown = true;
            }
            if (batches > 0) actions.Add("collect \u00d7" + batches);
            parts.Add(StopWithActions(id, actions));
        }
        parts.Add(trip.Batches > 0 ? "Ruin (drop \u00d7" + trip.Batches + ")" : "Ruin");
        return string.Join("  \u2192  ", parts);
    }

    /// <summary>
    /// The stops of a planned trip one by one, for the Current Route table: where it starts (Ruin, collecting the load from Ruin if there is one, or the station he is at),
    /// what is done at every stop, and the way back to Ruin. Every line has the leg that leads to it, so the legs add up to the whole route.
    /// </summary>
    List<StopLine> LinesOf(PlannedTrip trip, string worker, Func<string, decimal> perBatch)
    {
        var drive = QtOf(worker);
        var lines = new List<StopLine>();
        var prev = trip.From ?? PyroDistances.Ruin;
        var hasDrop = trip.Drops.Count > 0;
        var drop = hasDrop ? trip.Drops[0] : default;
        var cargo = trip.DropCargo ?? "cargo";
        var firstStop = trip.Stops.Count > 0 ? trip.Stops[0].Id : null;

        StopLine Add(string id, string name, bool stop, IEnumerable<string> actions, int batches = 0, decimal value = 0)
        {
            var gm = PyroDistances.Between(prev, id);
            var line = new StopLine { Id = id, Name = name, Stop = stop, Batches = batches, Value = value, Gm = gm, Seconds = gm > 0 ? drive.Seconds(gm) : 0 };
            line.Actions.AddRange(actions);
            lines.Add(line);
            prev = id;
            return line;
        }

        if (trip.From == null)
        {
            // the load for the first stop is collected at Ruin
            var collect = hasDrop
                ? new[] { "collect " + cargo + (drop.Batches > 1 ? " \u00d7" + drop.Batches : "") + (trip.DropScu > 0 ? ", " + ScuText(trip.DropScu) : "") }
                : Array.Empty<string>();
            Add(PyroDistances.Ruin, "Ruin", false, collect);
        }
        else if (firstStop != trip.From) Add(trip.From, PyroDistances.NameOf(trip.From), false, Array.Empty<string>());   // where he is now

        var dropShown = !hasDrop;
        if (hasDrop && firstStop != drop.Id)
        {
            Add(drop.Id, PyroDistances.NameOf(drop.Id), true, new[] { "drop " + cargo });   // a stop with only the drop-off
            dropShown = true;
        }
        foreach (var (id, batches) in trip.Stops)
        {
            var actions = new List<string>();
            if (!dropShown && id == drop.Id)
            {
                actions.Add("drop " + cargo);
                dropShown = true;
            }
            if (batches > 0) actions.Add("collect \u00d7" + batches);
            Add(id, PyroDistances.NameOf(id), true, actions, batches, batches * perBatch(id));
        }
        Add(PyroDistances.Ruin, "Ruin", false, trip.Batches > 0 ? new[] { "drop \u00d7" + trip.Batches } : Array.Empty<string>());
        return lines;
    }

    /// <summary>Puts what the plan still collects at a station after the lines of a run in progress (at the station where the last pickup was it joins that line).</summary>
    void AddCollectLine(List<StopLine> lines, string worker, string id, int batches, decimal perBatch)
    {
        var last = lines.Count > 0 ? lines[^1] : null;
        if (last != null && last.Id == id)
        {
            last.Actions.Add("collect \u00d7" + batches);
            last.Batches += batches;
            last.Value += batches * perBatch;
            return;
        }
        var prev = lines.LastOrDefault(l => l.Id.Length > 0)?.Id ?? PyroDistances.Ruin;
        var gm = PyroDistances.Between(prev, id);
        var line = new StopLine { Id = id, Name = PyroDistances.NameOf(id), Batches = batches, Value = batches * perBatch, Gm = gm, Seconds = gm > 0 ? QtOf(worker).Seconds(gm) : 0 };
        line.Actions.Add("collect \u00d7" + batches);
        lines.Add(line);
    }

    /// <summary>The last line of a run in progress: back at Ruin, where everything on board is handed in.</summary>
    void AddRuinLine(List<StopLine> lines, string worker, int batches)
    {
        var prev = lines.LastOrDefault(l => l.Id.Length > 0)?.Id ?? PyroDistances.Ruin;
        var gm = PyroDistances.Between(prev, PyroDistances.Ruin);
        var line = new StopLine { Id = PyroDistances.Ruin, Name = "Ruin", Stop = false, Gm = gm, Seconds = gm > 0 ? QtOf(worker).Seconds(gm) : 0 };
        if (batches > 0) line.Actions.Add("drop \u00d7" + batches);
        lines.Add(line);
    }

    /// <summary>
    /// The line of the Current Route where the worker is: the station of his Current location. A run in progress: the last stop he collected at (or the stop
    /// he set by hand); a trip that has not started: the line it starts from.
    /// </summary>
    static int HereLine(List<StopLine> lines, string location, bool byHand)
    {
        if (lines.Count == 0) return -1;
        var running = lines.Any(l => l.Done);
        if (byHand)
        {
            var at = running ? lines.FindLastIndex(l => l.Id == location) : lines.FindIndex(l => l.Id == location);
            if (at >= 0) return at;
        }
        var lastDone = lines.FindLastIndex(l => l.Done);
        return lastDone >= 0 ? lastDone : 0;
    }

    /// <summary>What the batches of one pickup are worth: every mission pays its reward split over its stops (the same sum as the value of a run).</summary>
    decimal PickupValue(TripPickup p)
    {
        if (_current == null || _inputs == null) return 0;
        decimal total = 0;
        foreach (var id in p.MissionIds)
        {
            var m = _current.Missions.FirstOrDefault(x => x.Id == id) ?? _current.Failed.FirstOrDefault(x => x.Id == id);
            if (m == null || m.Multi == 0) continue;
            total += RewardOf(m, _inputs) / m.Multi;
        }
        return total;
    }

    static string StopWithActions(string id, IReadOnlyList<string> actions) =>
        PyroDistances.NameOf(id) + (actions.Count == 0 ? "" : " (" + string.Join(" | ", actions) + ")");

    /// <summary>The latest time the logs show the worker picking up, handing in or delivering a load from Ruin (the minimum time = nothing yet).</summary>
    DateTime LastMoveOf(string name)
    {
        var last = DateTime.MinValue;
        foreach (var t in _trips.Where(t => SameName(t.Worker, name)))
        {
            if (t.End.HasValue && t.End.Value > last) last = t.End.Value;
            foreach (var p in t.Pickups)
            {
                if (p.Time > last) last = p.Time;
            }
        }
        var drop = LastDropOf(name);
        return drop != null && drop.Value.Time > last ? drop.Value.Time : last;
    }

    /// <summary>
    /// Where the logs say the worker is: at the station of his last pickup (a run in progress), or of the last load of a from-Ruin mission he delivered, whichever
    /// is later; else at Ruin Station (nothing yet, or his run is handed in).
    /// </summary>
    string AutoLocationOf(string name)
    {
        var run = ActiveRunOf(name);
        var drop = DropStartOf(name);
        if (run?.From != null && (drop == null || run.Value.Last >= drop.Value.Time)) return run.Value.From!;
        return drop?.Station ?? PyroDistances.Ruin;
    }

    /// <summary>
    /// The Current location of a worker: where he is now, and where the plan starts his next trip. The logs keep it up to date (AutoLocationOf: every pickup, every
    /// delivery from Ruin, every hand-in). A station set by hand (for example at the start of a session, or when the logs miss something) holds until the logs
    /// show him doing something after that, then the logs take over again.
    /// </summary>
    (string Id, bool ByHand) LocationOf(string name)
    {
        var kv = _data.StartPoints.FirstOrDefault(k => SameName(k.Key, name));
        if (kv.Value != null)
        {
            if (LastMoveOf(name) > kv.Value.Since)
            {
                _data.StartPoints.Remove(kv.Key);   // he has picked up, delivered or handed in since: the logs know better
                Storage.Save(_data);
            }
            else if (PyroDistances.Points.Any(s => s.Id == kv.Value.Id))
            {
                return (kv.Value.Id, true);
            }
        }
        return (AutoLocationOf(name), false);
    }

    /// <summary>Sets (or, with null, clears) where a worker is by hand. His locked next stop is released, so the plan is free to start with a stop near him.</summary>
    void SetLocation(string name, string? stationId)
    {
        foreach (var k in _data.StartPoints.Keys.Where(k => SameName(k, name)).ToList()) _data.StartPoints.Remove(k);
        // the same station as the logs say is no choice at all; Ruin Station by hand is one (the station of his last delivery from Ruin does not take over then)
        if (stationId != null && stationId != AutoLocationOf(name)) _data.StartPoints[name] = new StartPoint { Id = stationId, Since = DateTime.UtcNow };
        var lockKey = LockKey(name);
        if (lockKey != null) _inputs!.LockedTrips.Remove(lockKey);
        Storage.Save(_data);
        BeginInvoke(new Action(() =>
        {
            _planCore = null;
            _planShown = "";
            GetPlan();
            RefreshPlanner();
        }));
    }

    /// <summary>The title of a worker's table: just his name.</summary>
    static string PlanTitle(PlanWorkerView w) => w.Name;

    /// <summary>The Total line of the Queued routes table: the number of stops, batches, value, distance and flight time of the trips above it.</summary>
    static string[] PlanTotals(List<PlanRow> rows)
    {
        var stops = rows.Sum(r => r.Stops.Count);
        var gm = rows.Sum(r => r.Gm);
        var flight = rows.Sum(r => r.Flight);
        return new[]
        {
            "",
            "Total: " + stops + (stops == 1 ? " stop" : " stops"),
            rows.Sum(r => r.BatchCount).ToString(CultureInfo.InvariantCulture),
            ValueText(rows.Sum(r => r.Value)),
            gm > 0 ? Gm(gm) : "",
            flight > 0 ? "~" + FlightSpan(flight) : "",
        };
    }

    /// <summary>The cells of the Current Route table: the stops one by one (the numbers count the stops, not the Ruin lines).</summary>
    static List<string[]> LineCells(List<StopLine> lines)
    {
        var cells = new List<string[]>();
        var n = 0;
        foreach (var l in lines)
        {
            cells.Add(new[]
            {
                l.Stop ? (++n).ToString(CultureInfo.InvariantCulture) : "",
                l.Text,
                l.Batches > 0 ? l.Batches.ToString(CultureInfo.InvariantCulture) : "",
                ValueText(l.Value),
                l.Gm > 0 ? Gm(l.Gm) : "",
                l.Seconds > 0 ? "~" + FlightSpan(l.Seconds) : "",
            });
        }
        return cells;
    }

    /// <summary>The Total line of the Current Route table.</summary>
    static string[] LineTotals(List<StopLine> lines)
    {
        var stops = lines.Count(l => l.Stop);
        var gm = lines.Sum(l => l.Gm);
        var flight = lines.Sum(l => l.Seconds);
        return new[]
        {
            "",
            "Total: " + stops + (stops == 1 ? " stop" : " stops"),
            lines.Sum(l => l.Batches).ToString(CultureInfo.InvariantCulture),
            ValueText(lines.Sum(l => l.Value)),
            gm > 0 ? Gm(gm) : "",
            flight > 0 ? "~" + FlightSpan(flight) : "",
        };
    }

    /// <summary>The cells of a trip in the Queued routes table: its number, the route, the batches, the value, the distance and the flight time.</summary>
    static string[] QueuedCells(PlanRow r) => new[]
    {
        RowLabel(r),
        r.Route,
        r.BatchCount.ToString(CultureInfo.InvariantCulture),
        ValueText(r.Value),
        r.Gm > 0 ? Gm(r.Gm) : "",
        FlightText(r),
    };

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
        foreach (var t in _trips.Where(x => SameName(x.Worker, name) && !x.End.HasValue).OrderBy(RunStartedAt))   // finished runs are only on Cargo Runs
        {
            var level = t.Manual ? "All" : LostLevel(t.Key);
            var stops = new List<string>();
            var lines = new List<StopLine>();   // what is on board, stop by stop (pickups at the same place in a row are one line)
            var picked = 0;
            var drive = QtOf(name);
            var from = PyroDistances.Ruin;
            foreach (var p in t.Pickups.OrderBy(x => x.Time))
            {
                var n = p.MissionIds.Count > 0 ? p.MissionIds.Count : p.Missions;
                picked += n;
                var shown = LocationName(p.Zone);
                var id = StationOf(p.Zone);
                if (id != null) stops.Add(id);
                if (lines.Count > 0 && lines[^1].Name == shown)
                {
                    var same = lines[^1];
                    same.Batches += n;
                    same.Value += PickupValue(p);
                    same.Actions[0] = "collected \u00d7" + same.Batches + (t.End.HasValue ? "" : " \u2713");
                    continue;
                }
                var gm = id != null ? PyroDistances.Between(from, id) : 0;
                if (id != null) from = id;
                var line = new StopLine { Id = id ?? "", Name = shown, Done = true, Batches = n, Value = PickupValue(p), Gm = gm, Seconds = gm > 0 ? drive.Seconds(gm) : 0 };
                line.Actions.Add("collected \u00d7" + n + (t.End.HasValue ? "" : " \u2713"));   // the run in progress: what is already on board says so
                lines.Add(line);
            }
            rows.Add(new PlanRow
            {
                Label = "Run #" + t.Number,
                Route = string.Join("  \u2192  ", lines.Select(x => x.Text)),
                Lines = lines,
                Batches = level == "All" ? "failed" : t.Left ? "left" : t.End.HasValue ? t.HandedIn.ToString(CultureInfo.InvariantCulture) : picked.ToString(CultureInfo.InvariantCulture),
                BatchCount = picked,
                Gm = stops.Count > 0 ? PyroDistances.Run(stops) : 0,
                Seconds = t.End.HasValue ? Math.Max(0, (t.End.Value - t.Start).TotalSeconds) : 0,
                Level = level == "All" ? "all" : level == "Little" ? "little" : t.End.HasValue ? "done" : "active",
                Stops = stops,
                Value = RunValue(t),
                Flight = FlightOf(name, stops),
            });
        }
        return rows;
    }

    /// <summary>The estimated flying time of Ruin, these stations and back to Ruin with the drive of the worker.</summary>
    double FlightOf(string name, IEnumerable<string> stops)
    {
        var drive = QtOf(name);
        var from = PyroDistances.Ruin;
        double seconds = 0;
        foreach (var id in stops)
        {
            seconds += drive.Seconds(PyroDistances.Between(from, id));
            from = id;
        }
        return seconds + drive.Seconds(PyroDistances.Between(from, PyroDistances.Ruin));
    }

    /// <summary>The estimated flying time of a run on the Cargo Runs board: Ruin, its pickup stations in order, and back to Ruin.</summary>
    string RunFlightText(WorkerTrip t)
    {
        var stops = t.Pickups.Select(p => StationOf(p.Zone)).Where(id => id != null).Select(id => id!).ToList();
        var seconds = stops.Count > 0 ? FlightOf(t.Worker, stops) : 0;
        return seconds <= 0 ? "" : "~" + FlightSpan(seconds);
    }

    /// <summary>The run the worker is flying (picked up, not handed in yet): the batches on board and the station of the last pickup, or null.</summary>
    (int Loaded, string? From, DateTime Last)? ActiveRunOf(string name)
    {
        var loaded = 0;
        string? from = null;
        DateTime? latest = null;
        var any = false;
        foreach (var t in _trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue))
        {
            any = true;
            foreach (var p in t.Pickups)
            {
                loaded += p.Missions;
                if (latest == null || p.Time > latest)
                {
                    latest = p.Time;
                    from = StationOf(p.Zone);
                }
            }
        }
        return any ? (loaded, from, latest ?? DateTime.MinValue) : null;
    }

    /// <summary>True when the worker has a run that has pickups but is not handed in yet.</summary>
    bool HasActiveRun(string name) => ActiveRunOf(name) != null;

    /// <summary>False when he has picked up somewhere else since the lock was set (he did not go where the lock said); true while he is still on his way, or after he picked up at the locked stop.</summary>
    bool FollowsLock(string name, string lockedId, DateTime? since)
    {
        // only the pickups after the lock was set count: a lock made while he was already flying a run is for the next pickup
        var picked = false;
        foreach (var t in _trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue))
        {
            foreach (var p in t.Pickups.Where(p => since == null || p.Time > since))
            {
                picked = true;
                if (StationOf(p.Zone) == lockedId) return true;
            }
        }
        return !picked;   // no pickup since the lock: he is still on his way
    }

    /// <summary>
    /// The trips for the open batches. Only the next stop of a worker is locked, and only while he is on his way to it: its batches are taken
    /// out first and his first trip goes there first, but the rest of that trip is planned freely, so a better stop can still be put after it.
    /// A worker who is flying a run has it counted in his ship (room left, and the trip continues from where he is) and the plan adds to it;
    /// while there is room, the next stop of that continuation is locked too (it is where he is heading). A lock ends when he picks up somewhere else, when its batches are picked up, and when he leaves the party; when his run
    /// is handed in, the first stop of his next planned trip is locked. Only worked out again when the batches, the crew or the locks change.
    /// </summary>
    PlanCore CoreFor(List<(string Name, int Capacity, QtDrive Drive)> crew, Dictionary<string, int> open, List<string> unknown, Dictionary<string, List<(string[] Workers, int Batches)>> limits, IReadOnlyList<DropChunk> chunks)
    {
        var settled = new HashSet<string>();
        for (var pass = 0; ; pass++)
        {
            var remaining = new Dictionary<string, int>(open);
            var starts = new List<WorkerStart>();
            var waiting = new List<string>();   // on his way to nothing locked: the first stop of his next planned trip gets locked once it is planned
            if (_inputs != null)
            {
                var changed = false;
                var locking = _inputs.NextStopsLocked;
                if (!locking && _inputs.LockedTrips.Count > 0)
                {
                    _inputs.LockedTrips.Clear();   // the button says Unlocked: the plan is free
                    changed = true;
                }
                // somebody who left the party (or does not fly) has no locked stop: his batches go back to the others
                foreach (var gone in _inputs.LockedTrips.Keys.Where(k => !crew.Any(c => c.Capacity > 0 && SameName(c.Name, k))).ToList())
                {
                    _inputs.LockedTrips.Remove(gone);
                    changed = true;
                }
                foreach (var c in crew.Where(x => x.Capacity > 0))
                {
                    var run = ActiveRunOf(c.Name);
                    var loc = LocationOf(c.Name).Id;   // where he is: the station of his last pickup or delivery (the logs), or the one set by hand
                    var start = new WorkerStart { Worker = c.Name, Loaded = run?.Loaded ?? 0, From = loc == PyroDistances.Ruin ? null : loc };
                    var lockKey = LockKey(c.Name);
                    if (lockKey == null && locking)
                    {
                        // with the button on Locked everybody who flies has his next stop kept for him: the first stop of his planned trip is where he is heading
                        _inputs.LockedTrips[c.Name] = new List<LockedStop>();
                        lockKey = c.Name;
                        changed = true;
                    }
                    if (lockKey != null)
                    {
                        var stored = _inputs.LockedTrips[lockKey];
                        if (stored.Count > 0 && run != null && !FollowsLock(c.Name, stored[0].Id, stored[0].Since))
                        {
                            _inputs.LockedTrips[lockKey] = stored = new List<LockedStop>();   // he picked up somewhere else: his trips are free again
                            changed = true;
                        }
                        if (stored.Count > 0)
                        {
                            var take = Math.Min(Math.Min(stored[0].Batches, remaining.GetValueOrDefault(stored[0].Id)), Math.Max(0, c.Capacity - start.Loaded));
                            if (take > 0)
                            {
                                start.FirstId = stored[0].Id;
                                start.FirstBatches = take;
                                remaining[stored[0].Id] -= take;
                            }
                            else
                            {
                                _inputs.LockedTrips[lockKey] = new List<LockedStop>();   // picked up completely
                                changed = true;
                            }
                        }
                        if (start.FirstId == null && (run == null || run.Value.Loaded < c.Capacity)) waiting.Add(c.Name);   // a run with room left locks the next stop of its continuation too
                    }
                    if (start.FirstId == null && run == null && start.From != null && remaining.GetValueOrDefault(start.From) > 0)
                    {
                        // he said where he is: what is left at that station is right there, so his first trip takes it first (what only the others have is left to them)
                        var here = start.From;
                        var onlyOthers = limits.TryGetValue(here, out var gl) ? gl.Where(x => !x.Workers.Contains(c.Name)).Sum(x => x.Batches) : 0;
                        var take = Math.Min(Math.Min(remaining[here] - onlyOthers, remaining[here]), c.Capacity);
                        if (take > 0)
                        {
                            start.FirstId = here;
                            start.FirstBatches = take;
                            remaining[here] -= take;
                        }
                    }
                    if (start.FirstId != null || start.Loaded > 0 || start.From != null) starts.Add(start);
                }
                if (changed) Storage.Save(_data);
            }

            var key = string.Join("|", crew.Select(c => c.Name + ":" + c.Capacity + ":" + c.Drive.Key))
                + "#" + string.Join("|", remaining.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value))
                + "#" + string.Join("|", unknown)
                + "#" + string.Join("|", chunks.Select(x => x.Station + ":" + x.Batches + ":" + x.Scu + ":" + x.Cargo + ":" + string.Join(",", x.Allowed)))
                + "#" + string.Join("|", limits.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + string.Join(";", kv.Value.Select(g => string.Join(",", g.Workers) + "x" + g.Batches))))
                + "#" + string.Join("|", starts.Select(t => t.Worker + "=" + t.FirstId + "x" + t.FirstBatches + "/" + t.Loaded + "@" + t.From));
            if (_planCore != null && _planCore.Key == key && waiting.All(n => _planCore.Settled.Contains(n))) return _planCore;

            var note = unknown.Count > 0 ? "Not in the distance table, so left out: " + string.Join(", ", unknown) + "." : "";
            var only = limits.SelectMany(kv => kv.Value.Select(g => PyroDistances.NameOf(kv.Key) + " ×" + g.Batches + " only " + string.Join(" / ", g.Workers))).ToList();
            if (only.Count > 0) note = (note.Length > 0 ? note + "  " : "") + "Not everybody has every mission: " + string.Join(", ", only) + ".";
            var core = new PlanCore
            {
                Key = key,
                Trips = TripPlanner.Plan(remaining, crew, starts, limits, chunks),
                Batches = remaining.Where(kv => kv.Value > 0).Sum(kv => kv.Value) + starts.Sum(t => t.FirstBatches),
                Note = note,
                Settled = settled,
            };
            _planCore = core;
            if (waiting.Count == 0 || pass > 0 || _inputs == null) return core;

            // his run is handed in (or his lock was used up): the first stop of the trip planned for him is where he is going, so lock it (nothing planned = nothing to lock)
            foreach (var name in waiting)
            {
                var lockKey = LockKey(name);
                if (lockKey == null) continue;
                var next = core.Trips.FirstOrDefault(t => t.Worker == name);
                var flying = ActiveRunOf(name);
                if (next == null || NothingToLock(next))
                {
                    settled.Add(name);
                    if (flying == null) _inputs.LockedTrips.Remove(lockKey);   // with a run in progress the marker stays: his next trip is locked once the run is handed in
                }
                else _inputs.LockedTrips[lockKey] = new List<LockedStop> { new() { Id = next.Stops[0].Id, Batches = next.Stops[0].Batches, Since = flying?.Last } };
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
        var worth = new Dictionary<string, (decimal Value, int Batches)>();   // station -> what its batches are worth together (the value of a stop is reward / stops of its mission)
        var limits = new Dictionary<string, List<(string[] Workers, int Batches)>>();   // station -> batches that only some of the crew can take (they have those missions running, the others do not)
        var flyers = crew.Where(c => c.Capacity > 0).Select(c => c.Name).ToList();
        if (_inputs != null)
        {
            foreach (var r in _openRows)
            {
                var left = r.Missions - r.Loaded;   // batches not picked up yet
                if (left <= 0) continue;
                var id = StationOf(r.Zone);
                if (id != null && r.Missions > 0)
                {
                    var was = worth.GetValueOrDefault(id);
                    worth[id] = (was.Value + StopValue(r), was.Batches + r.Missions);
                }
                if (id == null)
                {
                    unknown.Add(LocationName(r.Zone) + " (" + left + ")");
                    continue;
                }
                remaining[id] = remaining.GetValueOrDefault(id) + left;
                foreach (var m in r.OpenMissions)
                {
                    var can = flyers.Where(n => HasMission(m, n)).ToArray();   // the Contracts tab: who has this mission
                    if (can.Length == 0 || can.Length == flyers.Count) continue;
                    if (!limits.TryGetValue(id, out var gl)) limits[id] = gl = new List<(string[] Workers, int Batches)>();
                    var same = gl.FindIndex(x => x.Workers.SequenceEqual(can));
                    if (same >= 0) gl[same] = (can, gl[same].Batches + 1);
                    else gl.Add((can, 1));
                }
            }
        }

        // cargo that goes out from Ruin (from-Ruin contracts): the planner gives every worker the loads of his missions, each in a trip of its own that unloads at its first stop
        var drops = OpenDrops();
        var chunks = new List<DropChunk>();
        var dropNote = "";
        if (drops.Count > 0)
        {
            var cap = flyers.Count == 0 ? 0 : crew.Where(c => c.Capacity > 0).Min(c => c.Capacity);
            chunks = DropChunks(drops, flyers, cap);
            var lost = drops.Count(d => d.Station == null);
            if (lost > 0) dropNote = "From Ruin: " + lost + (lost == 1 ? " drop-off is" : " drop-offs are") + " to a place that is not in the distance table, so left out.";
        }
        var core = CoreFor(crew, remaining, unknown, limits, chunks);
        var dropTrips = core.Trips;
        decimal PerBatch(string id) => worth.TryGetValue(id, out var x) && x.Batches > 0 ? x.Value / x.Batches : 0;   // the average batch at that station

        var plan = new PlanView { Note = (core.Note + "  " + dropNote).Trim() };
        var flying = crew.Where(c => c.Capacity > 0).ToList();
        for (var i = 0; i < flying.Count; i++)
        {
            var c = flying[i];
            var drive = DriveOf(c.Name);
            var w = new PlanWorkerView
            {
                Name = c.Name,
                Capacity = c.Capacity,
                DriveNote = drive == null ? "no QT drive set (assuming " + QuantumDrives.Assumed.Key + ")" : "",
                Drive = drive == null ? "no QT drive set (assuming " + QuantumDrives.Assumed.Key + ")" : drive.Key + ", " + (drive.Speed / 1e6).ToString("0", CultureInfo.InvariantCulture) + " Mm/s",
                Color = WorkerColor(c.Name),
            };
            w.RunsSoFar = _trips.Count(x => SameName(x.Worker, c.Name));
            var standing = LocationOf(c.Name);
            w.LocationId = standing.Id;
            w.LocationByHand = standing.ByHand;
            w.AutoLocationId = AutoLocationOf(c.Name);
            w.Rows.AddRange(RunRows(c.Name));
            // the run in progress is his Current trip (a locked stop is marked in the Route cell of a planned trip)
            var runRow = w.Rows.LastOrDefault(r => r.Level == "active");
            if (runRow != null)
            {
                runRow.Label = "Current";
                runRow.First = true;
            }
            var mine = dropTrips.Where(t => t.Worker == c.Name).ToList();
            for (var k = 0; k < mine.Count; k++)
            {
                w.Rows.Add(new PlanRow
                {
                    Label = k > 0 ? "Plan " + (k + 1) : runRow == null ? "Current" : mine[k].Special ? "Continue" : "Next",
                    First = k == 0 && runRow == null,
                    Solid = k == 0 && (runRow == null || mine[k].Special),
                    Locked = mine[k].Locked,
                    Route = RouteText(mine[k]),
                    Batches = mine[k].Batches.ToString(CultureInfo.InvariantCulture),
                    BatchCount = mine[k].Batches,
                    Value = mine[k].Stops.Sum(s => s.Batches * PerBatch(s.Id)),
                    Gm = mine[k].Distance,
                    Seconds = mine[k].Seconds,
                    Flight = mine[k].Seconds,
                    Level = "plan",
                    Stops = mine[k].Drops.Select(d => d.Id).Concat(mine[k].Stops.Select(s => s.Id)).Aggregate(new List<string>(), (l, id) => { if (l.Count == 0 || l[^1] != id) l.Add(id); return l; }),
                    From = mine[k].From,
                    Trip = mine[k],
                    Lines = LinesOf(mine[k], c.Name, PerBatch),
                });
            }
            // what the plan adds to the run in progress is part of his Current row: the stops already picked up, then the ones still to pick up
            if (runRow != null && mine.Count > 0 && mine[0].Special && mine[0].Stops.Count > 0)
            {
                var cont = mine[0];
                runRow.BatchCount += cont.Batches;
                runRow.Value += cont.Stops.Sum(s => s.Batches * PerBatch(s.Id));
                runRow.Batches = runRow.BatchCount.ToString(CultureInfo.InvariantCulture);
                runRow.Stops = runRow.Stops.Concat(cont.Stops.Select(s => s.Id)).ToList();
                runRow.Gm = PyroDistances.Run(runRow.Stops);
                runRow.Flight = FlightOf(c.Name, runRow.Stops);   // the whole run, Ruin to Ruin
                foreach (var (stopId, stopBatches) in cont.Stops) AddCollectLine(runRow.Lines, c.Name, stopId, stopBatches, PerBatch(stopId));
                w.Rows.First(r => r.Trip == cont).Hidden = true;
            }
            else if (runRow != null && runRow.Stops.Count > 0)
            {
                runRow.OnMap = true;   // nothing is added to it (full ship): the run itself is his solid line
                runRow.Solid = true;
            }
            if (runRow != null && runRow.Lines.Count > 0)
            {
                AddRuinLine(runRow.Lines, c.Name, runRow.BatchCount);   // the run ends at Ruin
                runRow.Route = string.Join("  \u2192  ", runRow.Lines.Select(l => l.Text));
            }
            // the first line is his Current trip, the others are numbered 2, 3, ...
            var number = 0;
            foreach (var r in w.Rows.Where(x => !x.Hidden))
            {
                number++;
                r.Label = number == 1 ? "Current" : number.ToString(CultureInfo.InvariantCulture);
            }
            // the worker colour is on the line of the Current Route where he is
            var currentRow = w.Rows.FirstOrDefault(x => !x.Hidden);
            if (currentRow != null) currentRow.Here = HereLine(currentRow.Lines, w.LocationId, w.LocationByHand);
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

        plan.Signature = plan.Summary + "¦" + (Theme.Dark ? "dark" : "light") + "¦" + string.Join("|", plan.Workers.Select(x => x.Name + "@" + x.LocationId + (x.LocationByHand ? "!" : "") + ">" + x.AutoLocationId)) + "¦" + string.Join("¦", plan.Workers.SelectMany(w =>
            w.Rows.Select(r => w.Name + "|" + w.Capacity + "|" + r.Label + "|" + r.Hidden + "|" + r.Locked + "|" + r.First + "|" + r.Solid + "|" + r.Route + "|" + r.Batches + "|" + r.Value.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Level + "|" + r.Gm.ToString("F1", CultureInfo.InvariantCulture) + "|" + r.Flight.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Seconds.ToString("F0", CultureInfo.InvariantCulture) + "|" + r.Here + "|" + string.Join(";", r.Lines.Select(l => l.Text + "~" + l.Batches + "~" + l.Value.ToString("F0", CultureInfo.InvariantCulture) + "~" + l.Gm.ToString("F1", CultureInfo.InvariantCulture) + "~" + l.Seconds.ToString("F0", CultureInfo.InvariantCulture))) + "|" + w.Drive + "|" + w.RunsSoFar)));
        return plan;
    }

    /// <summary>Shows the plan: the map on the left, the tables on the right. It only runs while the Trip Planner tab is on screen.</summary>
    void RefreshPlanner()
    {
        UpdateLockButton();
        PlanView plan;
        try
        {
            plan = GetPlan();
        }
        catch (Exception ex)
        {
            // the plan could not be worked out: say so (and try again on the next update), instead of leaving an old or empty page
            _planNote.Text = "The plan could not be worked out: " + ex.Message;
            _planNote.Visible = true;
            _planShown = "";
            _planCore = null;
            return;
        }
        _planNote.Text = plan.Note;
        _planNote.Visible = plan.Note.Length > 0;
        if (plan.Signature == _planShown) return;   // nothing changed, so nothing flickers

        _planBody.SuspendLayout();
        try
        {
            var page = PlanPage(plan);
            foreach (var old in _planBody.Controls.Cast<Control>().ToList())
            {
                _planBody.Controls.Remove(old);
                old.Dispose();
            }
            _planBody.Controls.Add(page);
            _planShown = plan.Signature;   // only when the page really got built, so a failed build is tried again
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
        if (IsHandleCreated) BeginInvoke(new Action(RefitPlanGrids));   // the page is on screen now: the rows are measured at the widths they really have
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

        var map = new PyroMapView { Dock = DockStyle.Top, Margin = new Padding(0, 0, 12, 0) };
        var host = new PlanHost { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0) };
        host.Resize += (_, _) => FitPlanBlocks(host);

        // one colour per worker, his current trip in bold and solid, the later ones dotted
        foreach (var w in plan.Workers)
        {
            var k = 0;
            foreach (var r in w.MapRows)
            {
                map.Routes.Add(new MapRoute { Label = k == 0 ? w.Name : "", Color = w.Color, Stops = r.Stops, From = r.From, Bold = r.Solid, Dashed = !r.Solid, Tag = r });
                k++;
            }
        }
        foreach (var w in plan.Workers) host.Controls.Add(PlanBlock(w));
        FitPlanBlocks(host);

        // the map is a square: as high as the page, but at most half of its width (the tables keep the rest)
        void FitMap()
        {
            var room = Math.Max(320, t.ClientSize.Height - t.Padding.Vertical);
            var side = Math.Max(320, Math.Min(room, (int)(t.ClientSize.Width * 0.4)));
            t.ColumnStyles[0].Width = side + map.Margin.Horizontal;
            map.Height = side;
        }
        t.Resize += (_, _) => FitMap();
        FitMap();
        t.Controls.Add(map, 0, 0);
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

    /// <summary>A title and the two tables of one crew member: the Current Route, and to the right of it the Queued routes (under it when the window is too narrow for both).</summary>
    Control PlanBlock(PlanWorkerView w)
    {
        var block = new Panel { Margin = new Padding(0, 0, 0, 14) };
        // the title: his name, and right of it where he is now (his Current location: the logs keep it up to date, and here it can be set by hand)
        var label = new Panel { Dock = DockStyle.Top, Height = 34 };
        var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        var nameWidth = TextRenderer.MeasureText(PlanTitle(w), titleFont).Width;
        label.Controls.Add(new Label { Text = PlanTitle(w), AutoSize = true, Font = titleFont, ForeColor = Theme.Text, Location = new Point(0, 6) });
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
            Text = w.LocationByHand ? "set by you, until his next pickup, delivery or hand-in" : "follows his pickups, deliveries and hand-ins",
            AutoSize = true, Font = new Font("Segoe UI", 9f), ForeColor = Theme.Muted, Location = new Point(comboX + combo.Width + 12, 10),
        });
        var height = label.Height;

        var visible = w.Rows.Where(x => !x.Hidden).ToList();
        if (visible.Count > 0)
        {
            var current = visible[0];
            var queued = visible.Skip(1).ToList();
            var boldFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            var currentGrid = CurrentRouteGrid(w, current, boldFont);
            var queuedGrid = QueuedRoutesGrid(queued, boldFont);

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

            // the Current Route on the left, the Queued routes on the right, each as wide as its text needs (the rest of the room is shared in that proportion);
            // a window too narrow for both puts the Queued routes under the Current Route instead
            var textFont = currentGrid.DefaultCellStyle.Font ?? currentGrid.Font;
            int Need(IEnumerable<string> texts, int cap) => Math.Min(cap, texts.Select(t => TextRenderer.MeasureText(t, textFont).Width).DefaultIfEmpty(0).Max() + 14);
            var needCurrent = PlanFixedWidth + Math.Max(150, Need(current.Lines.Select(l => l.Text), 300));
            var needQueued = PlanFixedWidth + Math.Max(150, Need(queued.Select(r => r.Route), 520));
            var fitting = false;
            void FitPair()
            {
                if (fitting) return;   // setting the sizes below changes the heights of the tables, which comes back here
                fitting = true;
                try
                {
                    var room = Math.Max(PlanMinTable, pair.ClientSize.Width);
                    var side = room >= 2 * PlanMinTable + PlanGap;
                    var widthCurrent = room;
                    var widthQueued = room;
                    if (side)
                    {
                        var shared = room - PlanGap;
                        widthCurrent = Math.Clamp(shared * needCurrent / (needCurrent + needQueued), PlanMinTable, shared - PlanMinTable);
                        widthQueued = shared - widthCurrent;
                    }
                    currentBox.Width = widthCurrent;
                    queuedBox.Width = widthQueued;
                    // the tables are as high as their rows (a long route takes two lines), the boxes follow them, and the block follows the boxes
                    currentBox.Height = PlanTitleHeight + currentGrid.Height;
                    queuedBox.Height = PlanTitleHeight + queuedGrid.Height;
                    currentBox.Location = new Point(0, 0);
                    queuedBox.Location = side ? new Point(widthCurrent + PlanGap, 0) : new Point(0, currentBox.Height + PlanGap);
                    var tall = side ? Math.Max(currentBox.Height, queuedBox.Height) : queuedBox.Bottom;
                    if (pair.Height != tall) pair.Height = tall;
                    if (block.Height != label.Height + tall) block.Height = label.Height + tall;
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
        block.Height = height;
        return block;
    }

    const int PlanTitleHeight = 26;
    const int PlanFixedWidth = 40 + 68 + 86 + 80 + 88;   // the narrow columns of the tables: #, Batches, Value, Distance, Flight time
    const int PlanMinTable = PlanFixedWidth + 110;       // a table is never narrower than this (the text column keeps at least 110)
    const int PlanGap = 14;                              // between the Current Route and the Queued routes

    /// <summary>The small title above one of the two tables of a worker.</summary>
    static Label PlanTableTitle(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = PlanTitleHeight,
        AutoSize = false,
        TextAlign = ContentAlignment.BottomLeft,
        Padding = new Padding(0, 0, 0, 2),
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        ForeColor = Theme.Text,
    };

    /// <summary>
    /// A table of the Trip Planner: # and a text column (the stops, or the route) that takes the room that is left and wraps onto a second line when it has to,
    /// then the narrow columns Batches, Value, Distance and Flight time.
    /// </summary>
    PlanGridView NewPlanGrid(string textHeader)
    {
        var grid = new PlanGridView();
        StyleGrid(grid);
        grid.Dock = DockStyle.Top;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ScrollBars = ScrollBars.None;
        grid.Columns.Add(Col("#", 40, true));
        var text = Col(textHeader, 300, true, DataGridViewContentAlignment.MiddleLeft, true);
        text.MinimumWidth = 110;
        text.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.Columns.Add(text);
        grid.Columns.Add(Col("Batches", 68, true));
        grid.Columns.Add(Col("Value", 86, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("Distance", 80, true, DataGridViewContentAlignment.MiddleRight));
        grid.Columns.Add(Col("Flight time", 88, true, DataGridViewContentAlignment.MiddleRight));
        foreach (DataGridViewColumn c in grid.Columns)
        {
            if (c == text) continue;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            c.MinimumWidth = 30;
        }
        return grid;
    }

    /// <summary>The Current Route of a worker: the run he is flying (or the trip he starts with), one stop per line. The worker colour is on the line where he is.</summary>
    PlanGridView CurrentRouteGrid(PlanWorkerView w, PlanRow current, Font boldFont)
    {
        var grid = NewPlanGrid("Stops");
        var cells = LineCells(current.Lines);
        for (var i = 0; i < cells.Count; i++)
        {
            var row = grid.Rows[grid.Rows.Add(cells[i])];
            if (i != current.Here) continue;
            var st = row.DefaultCellStyle;
            st.Font = boldFont;
            st.BackColor = w.Color;
            st.ForeColor = TextOn(w.Color);
            st.SelectionBackColor = w.Color;
            st.SelectionForeColor = TextOn(w.Color);
        }
        AddTotalRow(grid, LineTotals(current.Lines), boldFont);
        grid.FitHeight();
        return grid;
    }

    /// <summary>The Queued routes of a worker: the trips after his current one, each with a short route tag.</summary>
    PlanGridView QueuedRoutesGrid(List<PlanRow> queued, Font boldFont)
    {
        var grid = NewPlanGrid("Route");
        if (queued.Count == 0)
        {
            var none = grid.Rows[grid.Rows.Add("", "Nothing queued", "", "", "", "")];
            none.DefaultCellStyle.ForeColor = Theme.Muted;
        }
        foreach (var r in queued)
        {
            var row = grid.Rows[grid.Rows.Add(QueuedCells(r))];
            if (RowColor(r) is { } color) row.DefaultCellStyle.BackColor = color;
        }
        if (queued.Count > 0) AddTotalRow(grid, PlanTotals(queued), boldFont);
        grid.FitHeight();
        return grid;
    }

    /// <summary>The Total line at the bottom of a table.</summary>
    static void AddTotalRow(DataGridView grid, string[] cells, Font boldFont)
    {
        var total = grid.Rows[grid.Rows.Add(cells)];
        total.DefaultCellStyle.Font = boldFont;
        total.DefaultCellStyle.BackColor = Theme.Calc;
        total.DefaultCellStyle.ForeColor = Theme.Text;
        total.DefaultCellStyle.SelectionBackColor = Theme.Calc;
        total.DefaultCellStyle.SelectionForeColor = Theme.Text;
    }

    /// <summary>A table that never scrolls: it is as high as its rows, and a row with wrapped text (a long route) is as high as that text needs at the width the table has.</summary>
    sealed class PlanGridView : DataGridView
    {
        bool _fitting;

        public void FitHeight()
        {
            if (_fitting) return;
            _fitting = true;
            try
            {
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

        protected override void OnColumnWidthChanged(DataGridViewColumnEventArgs e)
        {
            base.OnColumnWidthChanged(e);
            FitHeight();
        }
    }

    /// <summary>The text of the # cell of a queued trip: its number.</summary>
    static string RowLabel(PlanRow r) => r.Label;

    /// <summary>Black or white, whichever reads better on this colour.</summary>
    static Color TextOn(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140 ? Color.Black : Color.White;

    static string Gm(double v) => v.ToString("N1", CultureInfo.InvariantCulture) + " Gm";

    /// <summary>m:ss or h:mm:ss.</summary>
    static string FlightSpan(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? (int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
    }

    /// <summary>The value with spaces between the thousands, or nothing when there is none.</summary>
    static string ValueText(decimal v) => v <= 0 ? "" : Math.Round(v).ToString("N0");

    /// <summary>The estimated flying time of the route, spool time and jumps with the drive of the worker, no loading.</summary>
    static string FlightText(PlanRow r) => r.Flight <= 0 ? "" : "~" + FlightSpan(r.Flight);
}
