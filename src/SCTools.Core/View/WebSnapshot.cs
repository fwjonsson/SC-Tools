using System.Globalization;
using System.Text.Json;

namespace SCTools.View;

/// <summary>How the snapshot is made: light or dark, and how wide a text is in the bold table font (the program measures it; without that it is estimated).</summary>
public sealed class WebSnapshotOptions
{
    public bool Dark { get; init; } = true;
    public Func<string, int>? MeasureBold { get; init; }

    /// <summary>Where the Payment tick of a money transfer on the Debts tab goes (the server's address): the one who sent the money ticks it. The program has none.</summary>
    public string? DebtsPay { get; init; }

    /// <summary>Where the admin's Lost Cargo pick in the Cargo runs table goes (the server's address; Frank, 29 Oct 2026). The program has none.</summary>
    public string? RunsLost { get; init; }

    /// <summary>Where the SCU a hauler types for a drop-off of his own trip on the Trip Planner goes (the server's address; Frank, 29 Oct 2026). The program has none.</summary>
    public string? TripScu { get; init; }

    /// <summary>Where the Submit button of a worker's Current Route goes (the server's address; Frank, 30 Oct 2026): his run on the way ends, counted as it is. The program has none.</summary>
    public string? TripEnd { get; init; }
}

/// <summary>
/// The live web view as data (data.json): every tab with the same tables, texts and colours as the program shows, made from the tracker alone (no window needed).
/// The program serves it on its own computer, the server for everybody.
/// </summary>
public sealed partial class WebSnapshot
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly Tracker _t;
    readonly TrackerState _s;
    readonly ViewPalette _p;
    readonly WebSnapshotOptions _o;

    WebSnapshot(Tracker tracker, WebSnapshotOptions options)
    {
        _t = tracker;
        _s = tracker.State;
        _o = options;
        _p = ViewPalette.Of(options.Dark);
    }

    /// <summary>data.json: the whole web view.</summary>
    public static string Json(Tracker tracker, WebSnapshotOptions options) => JsonSerializer.Serialize(new WebSnapshot(tracker, options).Build(), JsonOptions);

    /// <summary>The web view as an object (for the tests).</summary>
    public static object Build(Tracker tracker, WebSnapshotOptions options) => new WebSnapshot(tracker, options).Build();

    object Build()
    {
        var runs = RunsTables();
        return new
        {
            updated = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            session = _s.Current == null ? "" : BoardTexts.DescribeSession(_s.Current),
            theme = new
            {
                bg = ViewPalette.Hex(_p.Window),
                fg = ViewPalette.Hex(_p.Text),
                header = ViewPalette.Hex(_p.Header),
                line = ViewPalette.Hex(_p.Lines),
                cell = ViewPalette.Hex(_p.Cell),
            },
            summary = Summary(),
            rewards = Rewards(),
            pickups = Stops(),
            finished = Finished(),
            leaders = SessionLeaders(),
            runs = runs.Full,
            planRuns = runs.Short,   // the short copy under the map of the Trip Planner (#, Worker, Value, Started, Completed, Time, In flight, aUEC/h)
            runsLive = runs.Live,    // runs on the way: when each started (UTC), the page counts its Time up from that by its own clock (null: not running)
            runKeys = runs.Keys,     // the runs' keys, for the admin's Lost Cargo pick (RunsLost)
            runsLost = _o.RunsLost,
            planner = Planner(_t.Plan),
            debts = Debts(),
            debtsPay = _o.DebtsPay,
            history = History(),
            parties = Parties(),
        };
    }

    /// <summary>
    /// The parties in the game now (see AutoParty) with somebody of the party table in them: their name ("zzoega's party"), leader and players; then the
    /// parties that hauled in the session shown and are not playing now (its Groups: their payouts went apart), so their part can still be looked at. With
    /// more than one, each has the Session tab of its own (view: its stops, contracts, session, leaderboard and runs: the missions one of them has, and their
    /// runs); the page shows the viewer his own party's under Session (everybody's together to somebody in none), and lets him pick one on the Trip Planner.
    /// Empty where the logs do not say.
    /// </summary>
    object[] Parties()
    {
        var playing = _s.Parties?.Groups.Where(g => g.Names.Any(n => _s.Data.Party.Any(m => SameName(m, n)))).ToList();
        if (playing == null) return Array.Empty<object>();   // the program: the logs do not say
        var groups = playing.Select(g => (Group: g, Playing: true)).ToList();
        var hauled = _s.Current != null && _s.Data.Sessions.TryGetValue(_s.Current.Key, out var stored) ? stored.Groups : new List<List<string>>();
        foreach (var names in hauled)
        {
            if (names.Count == 0 || groups.Any(x => x.Group.Names.Any(n => names.Any(m => SameName(n, m))))) continue;
            groups.Add((new PartyGroup(names[0], names), false));
        }
        if (groups.Count == 0) return Array.Empty<object>();
        return groups.Select(x =>
        {
            var g = x.Group;
            if (groups.Count == 1) return (object)new { title = g.Title, leader = g.Leader, names = g.Names, playing = x.Playing };   // the whole session is theirs
            using var focus = _t.Focus(g.Names);
            var runs = RunsTables();
            return new
            {
                title = g.Title,
                leader = g.Leader,
                names = g.Names,
                playing = x.Playing,
                view = new
                {
                    session = _s.Current == null ? "" : BoardTexts.DescribeSession(_s.Current),
                    summary = Summary(),
                    rewards = Rewards(),
                    pickups = Stops(),
                    finished = Finished(),
                    leaders = SessionLeaders(),
                    runs = runs.Full,
                    runsLive = runs.Live,
                },
            };
        }).ToArray();
    }

    /// <summary>The colour of a worker (see ViewPalette.WorkerColor).</summary>
    int WorkerColor(string name) =>
        ViewPalette.WorkerColor(name, _t.Crew.PickedColor(name), _s.Data.Party.FindIndex(x => SameName(x, name)));

    // ------------------------------------------------------------------ Session tab

    /// <summary>The Session table: start, session time (counting up while the clocks run), batches, missions, earned and per hour.</summary>
    /// <summary>A time for the page's clocks ("2026-10-30T20:15:00.0000000Z"): UTC, so the browser's clock can count up from it.</summary>
    static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    object[] Summary()
    {
        // a clock that runs says when it started (since, UTC): the page counts up from it by its own clock, like the Session tool (Frank, 30 Oct 2026)
        object Item(string k, string v, string? since = null) => new { k, v, live = since != null, since };
        if (_s.Current == null || _s.Inputs == null || _s.Summary == null)
            return new[] { "Start Time", "Session Time", "Batches Delivered", "Active Missions", "Missions Completed", "Total aUEC Earned", "aUEC/Hour" }.Select(k => Item(k, "")).ToArray();
        var elapsed = _t.Figures.Elapsed();
        var income = _t.Earnings.Earned();
        return new[]
        {
            Item("Start Time", _s.Inputs.Start ?? FormatTime(_s.Summary.Start)),
            Item("Session Time", elapsed.HasValue ? LongDuration(elapsed.Value) : "", elapsed.HasValue && !_s.Current.Stop.HasValue && _s.ClocksRunning ? Iso(DateTime.UtcNow - elapsed.Value) : null),
            Item("Batches Delivered", _t.Figures.BatchesText()),
            Item("Active Missions", _t.Figures.ActiveText()),
            Item("Missions Completed", _t.Figures.CompletedText()),
            Item("Total aUEC Earned", income == 0 ? "" : income.ToString("N0", CultureInfo.CurrentCulture)),
            Item("aUEC/Hour", SessionFigures.HourlyText(elapsed, income)),
        };
    }

    /// <summary>
    /// The Contracts table: the kinds of contract the session has (by their names as the game shows them, as the Mission Library has them; a contract
    /// without a name by its contract line), each with its payout (what the library says, see Earnings.KindReward), how many are still going (Active) and
    /// done (Completed), and what they pay together; and the Total. The to-Ruin contracts first, then the from-Ruin ones, by name.
    /// </summary>
    ViewTable Rewards()
    {
        var table = new ViewTable(_p,
            new ViewColumn("Contract", "left"), new ViewColumn("Mission Reward", "right"), new ViewColumn("Active", "right"), new ViewColumn("Completed", "right"),
            new ViewColumn("Total", "right"));
        if (_s.Current == null || _s.Inputs == null) return table;
        var accepted = _s.Current.Missions.Select(m => (Mission: m, Out: false));
        var waste = _t.Earnings.SessionWaste(_s.Current).Select(m => (Mission: m, Out: true));
        decimal revenue = 0;
        int active = 0, completed = 0;
        static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        static string KindName(Mission m) => SCTools.Missions.MissionKinds.KindTitle(m.Title) ?? Contracts.ContractName(m);
        decimal Pays((Mission Mission, bool Out) x) => x.Out ? _t.Earnings.WasteRewardOf(x.Mission) : _t.Earnings.RewardOf(x.Mission);
        foreach (var g in accepted.Concat(waste).GroupBy(x => (x.Out, Name: KindName(x.Mission))).OrderBy(g => g.Key.Out).ThenBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase))
        {
            var missions = g.ToList();
            var reward = Pays(missions[0]);   // one kind, one reward
            var done = missions.Count(x => x.Mission.Completed);
            var total = missions.Sum(Pays);
            revenue += total;
            active += missions.Count - done;
            completed += done;
            table.Add(g.Key.Name, reward.ToString("N0", CultureInfo.CurrentCulture), N(missions.Count - done), N(done), total.ToString("N0", CultureInfo.CurrentCulture));
        }
        if (table.Rows.Count > 0) table.Add("Total", "", N(active), N(completed), revenue == 0 ? "" : revenue.ToString("N0", CultureInfo.CurrentCulture)).Bold = true;   // nothing: no Total of zeros
        return table;
    }

    Dictionary<string, HaulJob>? _jobsById;

    /// <summary>The hauling jobs of the party going on, by mission id (HaulJobs.Open, made once per snapshot): their places as the map has them, for the boards.</summary>
    Dictionary<string, HaulJob> JobsById() => _jobsById ??= HaulJobs.Open(_s).GroupBy(j => j.Mission.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The Pickup Location cell of a row: the location's name; for a job of another kind (stage 4) the place as the map has it, else what the game said.</summary>
    string StopName(LocationRow r)
    {
        if (!r.Job) return _t.Places.LocationName(r.Zone);
        foreach (var m in r.StopMissions)
        {
            if (JobsById().TryGetValue(m.Id, out var job) && job.Pickups.FirstOrDefault(p => p.Zone == r.Zone) is { } stop) return stop.Shown;
        }
        return r.PickupName ?? _t.Places.LocationName(r.Zone);
    }

    /// <summary>The Drop-off Location cell of a row of a contract to a station or a job (stage 4): the places as the map has them, else what the game said; null when nothing says.</summary>
    string? DropText(LocationRow r)
    {
        if (r.Job)
        {
            foreach (var m in r.StopMissions)
            {
                if (JobsById().TryGetValue(m.Id, out var job) && job.Drops.Count > 0) return string.Join(", ", job.Drops.Select(d => d.Shown).Distinct());
            }
        }
        return r.DropName;
    }

    /// <summary>
    /// The Stops board, the most valuable row on top: every pickup location with its batches, its cargo, its value and Ruin as the drop-off, and every
    /// station that from-Ruin drop-offs go to as a row of its own, read the same way (Frank, 29 Oct 2026): Ruin as the pickup, how many drop-offs, the
    /// cargo with its SCU in containers, their value, and the station as the drop-off. The SCU and the Boxes are two columns (Frank, 29 Oct 2026). No From
    /// Ruin and Notes columns since 29 Oct 2026 (Frank; the program's board still has them).
    /// </summary>
    ViewTable Stops()
    {
        var table = new ViewTable(_p,
            new ViewColumn("#"), new ViewColumn("Pickup Location", "left"), new ViewColumn("Batches"), new ViewColumn("SCU", "right"), new ViewColumn("Boxes", "left"),
            new ViewColumn("Value", "right"), new ViewColumn("Drop-off Location", "left"));
        if (_s.Current == null || _s.Inputs == null) return table;
        var ruin = PyroDistances.NameOf(PyroDistances.Ruin);
        var lines = new List<(decimal Value, string[] Cells, bool AllPicked, bool Waste)>();
        foreach (var stop in _t.Earnings.StopsBoard(_t.DropOffs.OpenDrops()))
        {
            if (stop.Row is { } r)
            {
                var value = _t.Earnings.StopValue(r);
                var waiting = CargoRuns.WaitingScu(r);   // a guess (mission SCU / its stops), nothing when everything there is picked up
                // a row of a contract to another station says that station (stage 1a of docs/planner-any-hauling.md): the game's name for it, else the position's;
                // a job of another kind (stage 4) its places as the map has them
                var to = r.ToRuin ? ruin : DropText(r) ?? (r.DropZone != null ? _t.Places.LocationName(r.DropZone) : "another station");
                lines.Add((value, new[] { "", StopName(r), BoardTexts.MissionsText(r), PlanText.ScuSum(waiting), waiting >= 1 ? PlanText.ScuGuess(waiting) : "", ValueText(value), to }, r.Loaded >= r.Missions, false));
            }
            if (stop.Drops.Count > 0)
            {
                var value = _t.Earnings.WasteValue(stop.Drops);
                var scu = stop.Drops.Sum(d => d.Scu);
                var cargo = string.Join(", ", stop.Drops.Select(d => d.Mission.OutCargo ?? "Cargo").Distinct()) + ": " + (scu > 0 ? DropOffs.ContainersText(scu) : "?");
                lines.Add((value, new[] { "", ruin, stop.Drops.Count.ToString(CultureInfo.InvariantCulture), scu > 0 ? scu.ToString(CultureInfo.InvariantCulture) : "?", cargo, ValueText(value), _t.Places.LocationName(stop.Zone) }, false, true));
            }
        }
        foreach (var l in lines.OrderByDescending(l => l.Value))
        {
            var row = table.Add(l.Cells);
            if (l.AllPicked) row.Color(2, _p.Green);   // everything there is picked up
            if (l.Waste) row.Color(6, _p.Gold, 0x000000);   // a drop-off to bring out
        }
        return table.Number();
    }

    /// <summary>Finished Stops: the latest hand-in on top, with the SCU and boxes delivered (a guess: mission SCU / its stops) and who carried from there.</summary>
    ViewTable Finished()
    {
        var table = new ViewTable(_p,
            new ViewColumn("#"), new ViewColumn("Pickup Location", "left"), new ViewColumn("Delivered"), new ViewColumn("SCU", "right"), new ViewColumn("Boxes", "left"),
            new ViewColumn("Worker"), new ViewColumn("Value", "right"), new ViewColumn("Handed In"));
        if (_s.Current == null || _s.Inputs == null) return table;
        foreach (var r in _s.DoneRows.OrderByDescending(x => x.DeliveredTime ?? x.LoadedTime ?? DateTime.MinValue))
        {
            var carriers = string.Join(", ", _s.Trips.Where(t => t.Pickups.Any(p => p.Zone == r.Zone)).Select(t => t.Worker).Distinct(StringComparer.OrdinalIgnoreCase));
            var delivered = r.Missions > 0 ? r.StopMissions.Sum(CargoRuns.StopScu) * r.Delivered / r.Missions : 0;   // the stops handed in, in SCU (a guess)
            var row = table.Add(
                "",
                StopName(r),
                r.Delivered + "/" + r.Missions,
                PlanText.ScuSum(delivered),
                delivered >= 1 ? PlanText.ScuGuess(delivered) : "",
                carriers,
                _t.Earnings.StopValueText(r),
                BoardTexts.DeliveredText(r));
            row.Color(2, _p.Green);
            if (r.Missing > 0) row.Color(7, _p.Orange);   // cargo is missing from a mission that has this stop
        }
        return table.Number();
    }

    /// <summary>A leaderboard (session or all time): the highest earner on top (a star and a gold row), the Total row last.</summary>
    ViewTable Board(List<StatLine> lines, bool withSessions, bool numbered, int totalSessions, double totalSeconds)
    {
        var columns = new List<ViewColumn>();
        if (numbered) columns.Add(new ViewColumn("#"));
        columns.Add(new ViewColumn("Worker"));
        if (withSessions) columns.Add(new ViewColumn("Sessions"));
        columns.AddRange(new[]
        {
            new ViewColumn("Completed Runs"), new ViewColumn("Perfect Runs"), new ViewColumn("Delivered Batches"), new ViewColumn("Avg Batches/Run"),
            new ViewColumn("Avg Run Time"), new ViewColumn("Distance Travelled", "right"), new ViewColumn("Gm / Hour", "right"), new ViewColumn("Total Run Value", "right"),
            new ViewColumn("Total Earned aUEC", "right"), new ViewColumn("Lost Value", "right"), new ViewColumn("Share of Run Value"), new ViewColumn("Run Value / Hour", "right"),
        });
        var table = new ViewTable(_p, columns.ToArray());
        foreach (var b in Leaderboard.Board(lines, totalSessions, totalSeconds, _s.Data.CombinedAccounts))
        {
            var cells = new List<string>();
            if (numbered) cells.Add("");
            cells.Add(b.Name);
            if (withSessions) cells.Add(b.Sessions);
            cells.AddRange(new[] { b.Runs, b.Perfect, b.Delivered, b.AvgBatches, b.AvgTime, b.Distance, b.GmPerHour, b.RunValue, b.Earned, b.Lost, b.Share, b.ValuePerHour });
            var row = table.Add(cells.ToArray());
            if (b.Top) row.RowColor(_p.Gold);
            if (b.Total) row.Bold = true;
            if (b.Combined) row.Color(numbered ? 1 : 0, _p.Cell, _p.Muted);   // one player's accounts added up: in grey, after the players, not numbered
        }
        if (numbered) table.Number(0, lines.Count);   // not the Total row
        return table;
    }

    /// <summary>The leaderboard of the session shown.</summary>
    ViewTable SessionLeaders()
    {
        if (_s.Inputs == null || _s.Current == null) return Board(new List<StatLine>(), false, true, 1, 0);
        var lines = _t.Leaderboard.SessionLines(out var seconds);
        return Board(lines, false, true, 1, seconds);
    }

    /// <summary>Cargo Runs (latest activity on top), the short copy of it under the map of the Trip Planner, and which runs are on the way.</summary>
    (ViewTable Full, ViewTable Short, string?[] Live, string[] Keys) RunsTables()
    {
        var full = new ViewTable(_p,
            new ViewColumn("#"), new ViewColumn("Worker"), new ViewColumn("Pickup Location & Time", "left"), new ViewColumn("Batches"), new ViewColumn("Value", "right"),
            new ViewColumn("Started"), new ViewColumn("Completed"), new ViewColumn("Time"), new ViewColumn("In flight"), new ViewColumn("aUEC/h", "right"),
            new ViewColumn("Lost Cargo"));
        var brief = new ViewTable(_p,
            new ViewColumn("#"), new ViewColumn("Worker"), new ViewColumn("Value", "right"), new ViewColumn("Started"), new ViewColumn("Completed"), new ViewColumn("Time"),
            new ViewColumn("In flight"), new ViewColumn("aUEC/h", "right"));
        var live = new List<string?>();
        var keys = new List<string>();
        if (_s.Current == null || _s.Inputs == null || _s.Log == null) return (full, brief, live.ToArray(), keys.ToArray());
        var runs = _t.Runs;
        foreach (var t in _s.Trips)
        {
            var level = runs.LostLevel(t.Key);
            var number = t.Number.ToString(CultureInfo.InvariantCulture);
            var value = _t.Earnings.RunValueText(t);
            var started = FormatTime(t.Start);
            var completed = t.End.HasValue ? FormatTime(t.End.Value) : "";
            var duration = runs.RunDuration(t);
            var flight = runs.RunFlightText(t);
            var perHour = runs.RunHourlyText(t);
            var color = _p.RunColor(t.Manual ? "All" : level, t.End.HasValue);
            full.Add(number, t.Worker, runs.RunStepsText(t), runs.BatchesText(t), value, started, completed, duration, flight, perHour, level).RowColor(color);
            brief.Add(number, t.Worker, value, started, completed, duration, flight, perHour).RowColor(color);
            live.Add(!t.End.HasValue && duration.Length > 0 ? Iso(t.Start) : null);
            keys.Add(t.Key);
        }
        return (full, brief, live.ToArray(), keys.ToArray());
    }
}
