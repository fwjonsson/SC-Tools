using System.Globalization;

namespace HaulingTracker.Tracking;

/// <summary>
/// The small team status line under the map of the Trip Planner (and the same line on the web page): what the crew is doing right now, taken from the logs
/// (who sets course for which station, who arrives, loads, delivers, hands in, dies), mixed with some stats and a bit of fun. It only talks about what the logs show:
/// a member without a log of his own is only in the lines that come from the runs. Advance is called every second; Changed says when the text changes.
/// </summary>
public sealed class StatusFeed
{
    readonly TrackerState _state;
    readonly Places _places;
    readonly Crew _crew;
    readonly Earnings _earnings;
    readonly CargoRuns _runs;

    public StatusFeed(TrackerState state, Places places, Crew crew, Earnings earnings, CargoRuns runs)
    {
        _state = state;
        _places = places;
        _crew = crew;
        _earnings = earnings;
        _runs = runs;
    }

    /// <summary>The text shown now.</summary>
    public string Text => _statusText;

    /// <summary>Goes up when the line switches to another text (the web page fades only then).</summary>
    public int Seq => _statusSeq;

    /// <summary>The text changed: the new text, and whether it fades over (another line) or is just updated (a clock in it counting).</summary>
    public event Action<string, bool>? Changed;

    /// <summary>One thing the status line can say. An event has a time and a key (so it is shown once); the text is made when it is shown, so a clock in it keeps counting.</summary>
    sealed class StatusItem
    {
        public string Key = "";
        public Func<string> Text = () => "";
    }

    readonly Queue<StatusItem> _statusNews = new();
    readonly HashSet<string> _statusSeen = new();
    Func<string> _statusMake = () => "";
    string _statusText = "";
    int _statusSeq;                       // goes up when the line switches to another text (the web page fades only then)
    DateTime _statusShownAt = DateTime.MinValue;
    bool _statusIsNews;
    int _statusCursor;

    static readonly TimeSpan NewsWindow = TimeSpan.FromSeconds(45);   // an event is news for this long

    static Func<string> Const(string text) => () => text;

    static string Pick(string key, params string[] options) => options[(int)((uint)key.GetHashCode() % (uint)options.Length)];

    /// <summary>"CRUS_Starlifter_C2" as "Starlifter C2": the code of the maker in front is left out.</summary>
    static string ShipName(string raw)
    {
        var parts = raw.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 1 && parts[0].Length <= 5 && parts[0].All(char.IsUpper)) parts.RemoveAt(0);
        return string.Join(" ", parts);
    }

    /// <summary>The name of a quantum travel destination of the log, or null when it is not known.</summary>
    static string? QuantumPlace(string target)
    {
        var id = PyroDistances.IdOfQuantumTarget(target);
        if (id == null) return null;
        return id == PyroDistances.Ruin ? "Ruin" : PyroDistances.NameOf(id).Replace(" GW", " Gateway");
    }

    // ------------------------------------------------------------------ what is happening now

    /// <summary>The members of the crew the line can talk about.</summary>
    List<string> StatusWorkers()
    {
        var names = new List<string>();
        IEnumerable<string> logged = _state.Log != null ? _state.Log.Players : new HashSet<string>();
        foreach (var n in _crew.ActiveCrew().Concat(_state.Trips.Select(t => t.Worker)).Concat(logged))
        {
            if (n.Length == 0 || n == "Unassigned" || names.Any(x => SameName(x, n))) continue;
            names.Add(n);
        }
        return names;
    }

    /// <summary>The events of the last moments that the line announces, newest last. Every event has a key, so it is only shown once.</summary>
    List<StatusItem> StatusNews(DateTime now)
    {
        var news = new List<StatusItem>();
        if (_state.Log == null) return news;
        bool Fresh(DateTime time) => now - time <= NewsWindow && now - time >= TimeSpan.FromSeconds(-10);

        // quantum travel: he sets course, his drive arrives
        foreach (var kv in _state.Log.QtByPlayer)
        {
            var who = kv.Key;
            var travel = kv.Value.OrderBy(e => e.Time).ToList();
            for (var i = 0; i < travel.Count; i++)
            {
                var step = travel[i];
                if (!Fresh(step.Time)) continue;
                var key = "qt|" + who + "|" + step.Kind + "|" + step.Time.Ticks;
                string text;
                if (step.Kind == QtKind.Selected)
                {
                    var dest = QuantumPlace(step.Destination);
                    text = dest == null
                        ? Pick(key, who + " picked a new quantum destination", who + " is off to somewhere new")
                        : Pick(key, who + " is traveling towards " + dest, who + " set course for " + dest, who + " is on the way to " + dest,
                            step.From.Length > 0 ? who + " left " + step.From + " for " + dest : who + " is heading for " + dest);
                }
                else
                {
                    QtEvent? chosen = null;
                    for (var j = i - 1; j >= 0; j--)
                    {
                        if (travel[j].Kind != QtKind.Selected) continue;
                        chosen = travel[j];
                        break;
                    }
                    var dest = chosen == null ? null : QuantumPlace(chosen.Destination);
                    var took = chosen != null && step.Time - chosen.Time < TimeSpan.FromMinutes(45) ? " in " + StatusSpan(step.Time - chosen.Time) : "";
                    text = dest == null
                        ? Pick(key, who + " has arrived", who + "'s quantum drive just cooled down")
                        : Pick(key, who + " arrived at " + dest, who + " landed at " + dest + took, who + " made it to " + dest + took);
                }
                news.Add(new StatusItem { Key = key, Text = Const(text) });
            }
        }

        // cargo taken out and delivered, hand-ins
        foreach (var run in _state.Trips)
        {
            var who = run.Worker;
            if (who == "Unassigned") continue;
            foreach (var pick in run.Pickups)
            {
                if (!Fresh(pick.Time)) continue;
                var key = "pk|" + who + "|" + run.Number + "|" + pick.Zone;
                var cargo = pick.MissionIds.Select(id => _state.Log.Missions.TryGetValue(id, out var m) ? m.Cargo : null).FirstOrDefault(c => c != null) ?? "cargo";
                var place = _places.LocationName(pick.Zone);
                news.Add(new StatusItem
                {
                    Key = key,
                    Text = Const(Pick(key, who + " is loading " + cargo + " at " + place, who + " pulled " + cargo + " out of the elevator at " + place, who + " is wrestling " + cargo + " crates at " + place)),
                });
            }
            foreach (var cluster in CargoRuns.Clusters(run.Collects, x => x.Time, x => x.Cargo))
            {
                if (!Fresh(cluster[^1].Time)) continue;
                var key = "wc|" + who + "|" + cluster[0].Time.Ticks;
                var cargo = cluster[0].Cargo;
                news.Add(new StatusItem
                {
                    Key = key,
                    Text = Const(Pick(key, who + " is loading " + cargo + " at Ruin", who + " took " + cargo + " out of the elevator at Ruin")),
                });
            }
            foreach (var cluster in CargoRuns.Clusters(run.Drops, x => x.Time, x => x.Cargo + "|" + x.Zone))
            {
                if (!Fresh(cluster[^1].Time)) continue;
                var key = "wd|" + who + "|" + cluster[0].Time.Ticks;
                var scu = cluster.Sum(x => x.Scu);
                var spot = _places.LocationName(cluster[0].Zone);
                var cargo = cluster[0].Cargo;
                news.Add(new StatusItem
                {
                    Key = key,
                    Text = Const(Pick(key, who + " delivered " + cargo + " to " + spot + (scu > 0 ? " (" + scu + " SCU)" : ""), who + " got rid of the " + cargo + " at " + spot, who + " dropped the " + cargo + " off at " + spot)),
                });
            }
            if (run.End is DateTime end && Fresh(end) && !run.Manual && !run.Left && run.HandedIn > 0)
            {
                var key = "hi|" + who + "|" + end.Ticks;
                var span = StatusSpan(end - run.Start);
                var n = run.HandedIn;
                var missions = n + (n == 1 ? " mission" : " missions");
                news.Add(new StatusItem
                {
                    Key = key,
                    Text = Const(Pick(key, who + " handed in " + missions + " after " + span, who + " is back at Ruin: " + missions + " in " + span, who + " finished run #" + run.Number + " in " + span)),
                });
            }
        }

        // deaths (the game writes no kill lines any more: this is the burst of item recovery lines of the body)
        foreach (var kv in _state.Log.DeathsByPlayer)
        {
            var who = kv.Key;
            var since = _state.Current?.Start ?? DateTime.MinValue;
            foreach (var died in kv.Value)
            {
                if (!Fresh(died)) continue;
                var key = "dead|" + who + "|" + died.Ticks;
                var count = kv.Value.Count(x => x >= since && x <= died);
                var again = count > 1 ? " again (" + count + " times so far)" : "";
                news.Add(new StatusItem
                {
                    Key = key,
                    Text = Const(Pick(key, who + " died" + again, "RIP " + who + (count > 1 ? ", death #" + count : ""), who + " just respawned. The cargo hopes for the best", who + " had a disagreement with something that shoots")),
                });
            }
        }

        // money
        foreach (var tr in _state.Log.Transfers)
        {
            if (!Fresh(tr.Time)) continue;
            var key = "tx|" + tr.From + "|" + tr.To + "|" + tr.Amount + "|" + tr.Time.Ticks;
            var amount = tr.Amount.ToString("N0", CultureInfo.InvariantCulture) + " aUEC";
            news.Add(new StatusItem
            {
                Key = key,
                Text = Const(Pick(key, tr.From + " sent " + tr.To + " " + amount, tr.From + " paid " + tr.To + " " + amount + ". The Debts tab took note")),
            });
        }
        return news;
    }

    // ------------------------------------------------------------------ the rest of the time: clocks, stats and some fun

    static List<Func<string>> Interleave(params List<Func<string>>[] groups)
    {
        var res = new List<Func<string>>();
        for (var i = 0; groups.Any(grp => i < grp.Count); i++)
        {
            foreach (var item in groups)
            {
                if (i < item.Count) res.Add(item[i]);
            }
        }
        return res;
    }

    List<Func<string>> StatusFiller(DateTime now)
    {
        var live = new List<Func<string>>();
        var stats = new List<Func<string>>();
        var fun = new List<Func<string>>();
        if (_state.Log == null) return fun;
        var running = _state.ClocksRunning;
        var since = _state.Current?.Start ?? DateTime.MinValue;

        var board = _state.Inputs != null ? Leaderboard.ComputeStats(_state.Trips, _state.Inputs, _places.StationOf) : new Dictionary<string, WorkerStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in StatusWorkers())
        {
            var name = member;

            // on a trip: the clock counts on while the line is shown
            var open = _runs.OpenRunOf(name);
            if (open != null && running)
            {
                var began = open.Start;
                var variant = (int)((uint)(name + _statusCursor).GetHashCode() % 4u);
                live.Add(() =>
                {
                    var span = StatusSpan(DateTime.UtcNow - began);
                    return variant switch
                    {
                        0 => name + " has spent " + span + " on this trip",
                        1 => name + " is " + span + " into this run",
                        2 => name + " has been out for " + span + " now",
                        _ => "Run clock of " + name + ": " + span,
                    };
                });
            }
            else if (running)
            {
                var ends = _state.Trips.Where(t => SameName(t.Worker, name) && t.End.HasValue && !t.Manual && !t.Left).Select(t => t.End ?? DateTime.MinValue).ToList();
                if (ends.Count > 0)
                {
                    var lastEnd = ends.Max();
                    live.Add(() => name + "'s last hand-in was " + StatusSpan(DateTime.UtcNow - lastEnd) + " ago");
                }
            }

            if (board.TryGetValue(name, out var stat) && stat.Runs > 0)
            {
                var runs = stat.Runs;
                var missions = stat.RunMissions;
                stats.Add(Const(name + " has finished " + runs + (runs == 1 ? " run" : " runs") + " and delivered " + missions + (missions == 1 ? " mission" : " missions")));
                stats.Add(Const(name + " averages " + StatusSpan(TimeSpan.FromSeconds(stat.RunSeconds / runs)) + " per run"));
                if (stat.Distance > 0) stats.Add(Const(name + " has flown " + Dist(stat.Distance) + " of hauling"));
            }
        }

        // the crew
        var totalRuns = board.Values.Sum(s => s.Runs);
        if (totalRuns > 0)
        {
            var totalMissions = board.Values.Sum(s => s.RunMissions);
            stats.Add(Const("The crew has delivered " + totalMissions + " missions in " + totalRuns + " runs so far"));
            var gm = board.Values.Sum(s => s.Distance);
            if (gm > 0) stats.Add(Const("Together the crew has flown " + Dist(gm) + " of hauling"));
        }
        var finished = _state.Trips.Where(t => t.End.HasValue && !t.Manual && !t.Left && t.Pickups.Count > 0 && (_state.Inputs == null || !CargoRuns.RunFailed(t, _state.Inputs))).ToList();
        if (finished.Count >= 3)
        {
            var quick = finished.OrderBy(t => (t.End ?? t.Start) - t.Start).First();
            var slow = finished.OrderByDescending(t => (t.End ?? t.Start) - t.Start).First();
            stats.Add(Const("Fastest run so far: " + quick.Worker + ", " + StatusSpan((quick.End ?? quick.Start) - quick.Start)));
            stats.Add(Const("Longest run so far: " + slow.Worker + ", " + StatusSpan((slow.End ?? slow.Start) - slow.Start)));
        }
        if (running && since != DateTime.MinValue) stats.Add(() => "The session has been going for " + StatusSpan(DateTime.UtcNow - since));

        // quantum travel and ships (workers with a log of their own)
        foreach (var kv in _state.Log.QtByPlayer)
        {
            var who = kv.Key;
            var travel = kv.Value.OrderBy(e => e.Time).ToList();
            var jumps = 0;
            var underway = TimeSpan.Zero;
            DateTime? selected = null;
            foreach (var step in travel)
            {
                if (step.Kind == QtKind.Selected)
                {
                    selected = step.Time;
                    continue;
                }
                if (step.Time < since) { selected = null; continue; }
                jumps++;
                if (selected.HasValue && step.Time - selected.Value < TimeSpan.FromMinutes(30)) underway += step.Time - selected.Value;
                selected = null;
            }
            if (jumps >= 2) stats.Add(Const(who + " has made " + jumps + " quantum jumps this session"));
            if (underway >= TimeSpan.FromMinutes(1)) stats.Add(Const(who + " has spent " + StatusSpan(underway) + " in quantum travel"));
            var ship = travel.LastOrDefault(e => e.Ship.Length > 0)?.Ship;
            if (!string.IsNullOrEmpty(ship)) stats.Add(Const(who + " is flying a " + ShipName(ship)));
        }

        // the deaths
        var deaths = _state.Log.DeathsByPlayer.Select(kv => (Name: kv.Key, Count: kv.Value.Count(d => d >= since))).Where(x => x.Count > 0).OrderByDescending(x => x.Count).ToList();
        if (deaths.Count > 0) stats.Add(Const("Deaths this session: " + string.Join(", ", deaths.Select(x => x.Name + " " + x.Count))));

        // records and odd numbers of this session
        try
        {
            var done = finished.Where(t => t.End.HasValue && t.End.Value >= since).ToList();
            if (done.Count > 0)
            {
                var best = done.OrderByDescending(_earnings.RunValue).First();
                var bestValue = _earnings.RunValue(best);
                if (bestValue > 0) stats.Add(Const("Richest run so far: " + best.Worker + "'s run #" + best.Number + ", " + bestValue.ToString("N0", CultureInfo.InvariantCulture) + " aUEC"));
                var rated = done.Where(t => (t.End!.Value - t.Start) >= TimeSpan.FromMinutes(5))
                    .Select(t => (Run: t, Rate: _earnings.RunValue(t) / (decimal)(t.End!.Value - t.Start).TotalHours)).OrderByDescending(x => x.Rate).FirstOrDefault();
                if (rated.Run != null && rated.Rate > 0)
                    stats.Add(Const("Best rate so far: " + rated.Run.Worker + "'s run #" + rated.Run.Number + " at " + Millions(rated.Rate) + " aUEC/h"));
                var crewValue = _state.Trips.Where(t => t.Start >= since || (t.End ?? DateTime.MaxValue) >= since).Sum(_earnings.RunValue);
                if (crewValue >= 1_000_000) stats.Add(Const("The runs of this session are worth " + Millions(crewValue) + " aUEC so far"));
            }
            var pickups = _state.Trips.Where(t => (t.End ?? DateTime.MaxValue) >= since).SelectMany(t => t.Pickups.Select(p => (t.Worker, Pick: p))).ToList();
            if (pickups.Count >= 3)
            {
                var biggest = pickups.OrderByDescending(x => x.Pick.Missions).First();
                if (biggest.Pick.Missions >= 3) stats.Add(Const("Biggest pickup: " + biggest.Pick.Missions + " batches at " + _places.LocationName(biggest.Pick.Zone) + " (" + biggest.Worker + ")"));
                var favourite = pickups.GroupBy(x => _places.LocationName(x.Pick.Zone)).OrderByDescending(g => g.Count()).First();
                if (favourite.Count() >= 2) stats.Add(Const("Most visited station this session: " + favourite.Key + " (" + favourite.Count() + " landings)"));
            }
            var waste = _state.Trips.Where(t => (t.End ?? DateTime.MaxValue) >= since).SelectMany(t => t.Drops).Sum(d => d.Scu);
            if (waste > 0) stats.Add(Const(waste.ToString("N0", CultureInfo.InvariantCulture) + " SCU of waste delivered this session. Pyro thanks you"));
            var left = _state.OpenRows.Sum(r => Math.Max(0, r.Missions - r.Loaded));
            var at = _state.OpenRows.Count(r => r.Missions - r.Loaded > 0);
            if (left > 0) stats.Add(Const(left + (left == 1 ? " batch is" : " batches are") + " still waiting at " + at + (at == 1 ? " station" : " stations")));
            var flown = board.Values.Sum(st => st.Distance);
            if (flown >= TerminusLap) stats.Add(Const("The crew has flown far enough to go around Terminus's orbit " + (flown / TerminusLap).ToString("0.0", CultureInfo.InvariantCulture) + " times"));
            var local = DateTime.Now;
            if (running && local.Hour >= 1 && local.Hour < 6) fun.Add(Const("It is " + local.ToString("HH:mm", CultureInfo.InvariantCulture) + ". Pyro never sleeps, but you could"));
        }
        catch (Exception)
        {
            // the line is only for fun
        }

        // fun, two at a time (they change every few minutes)
        string[] jokes =
        {
            "Quantum travel: the perfect time to refill your coffee",
            "Every hangar request is a small prayer",
            "If it does not fit, it is probably a 32",
            "A full C2 is a happy C2",
            "Two accounts, one brain, zero quantum time wasted",
            "The freight elevator has seen things",
            "Elevator music not included",
            "Stack them high, fly them home",
            "Somewhere a box of Carbon is wondering if you forgot it",
            "Landing gear down, coffee up",
            "Reminder: the cargo does not tip, but you can",
            "Hydration check: the real you, not the character",
            "Pyro: where \"just one more stop\" is a lifestyle",
            "No cargo was harmed in the making of this run. Probably",
            "The cargo elevator is never in a hurry. Neither should you be",
            "Somewhere out there a pirate wonders where everybody went",
            "Waste hauling: because someone has to",
            "Tip: a quantum drive works better when you point it at the destination",
            "Ruin Station: the name is a promise, not a warning",
        };
        var slot = (int)(now.Ticks / TimeSpan.TicksPerMinute / 3);
        fun.Add(Const(jokes[slot % jokes.Length]));
        fun.Add(Const(jokes[(slot + 3) % jokes.Length]));

        // quiet for a while while the clocks are running
        if (running)
        {
            var times = new List<DateTime>();
            foreach (var run in _state.Trips)
            {
                if (run.End.HasValue) times.Add(run.End.Value);
                if (run.LastEvent.HasValue) times.Add(run.LastEvent.Value);
            }
            foreach (var steps in _state.Log.QtByPlayer.Values) times.AddRange(steps.Select(e => e.Time));
            if (times.Count > 0)
            {
                var latest = times.Max();
                if (now - latest >= TimeSpan.FromMinutes(5) && now - latest < TimeSpan.FromHours(2))
                    fun.Add(() => "Quiet for " + StatusSpan(DateTime.UtcNow - latest) + ". Everybody still alive out there?");
            }
        }
        return Interleave(live, stats, fun);
    }

    /// <summary>Once around the orbit of Terminus, the outermost planet of Pyro (radius about 68.5 Gm), in Gm.</summary>
    const double TerminusLap = 2 * Math.PI * 68.5;

    // ------------------------------------------------------------------ the line itself

    /// <summary>Every second (now = UTC): picks up fresh events, switches to the next line when it is time, and keeps a clock in the line counting. It never gets in the way of anything else.</summary>
    public void Advance(DateTime now)
    {
        try
        {
            if (_state.Log == null) return;
            foreach (var fresh in StatusNews(now))
            {
                if (_statusSeen.Add(fresh.Key)) _statusNews.Enqueue(fresh);
            }
            while (_statusNews.Count > 4) _statusNews.Dequeue();

            var shown = (now - _statusShownAt).TotalSeconds;
            if (_statusNews.Count > 0 && shown >= 3)
            {
                ShowStatus(_statusNews.Dequeue().Text, true, now);
            }
            else if (_statusText.Length == 0 || shown >= (_statusIsNews ? 8 : 10))
            {
                var pool = StatusFiller(now);
                for (var tries = 0; tries < pool.Count; tries++)
                {
                    var make = pool[_statusCursor++ % pool.Count];
                    if (make() == _statusText) continue;
                    ShowStatus(make, false, now);
                    break;
                }
            }
            else
            {
                var text = _statusMake();
                if (text != _statusText)
                {
                    _statusText = text;
                    Changed?.Invoke(text, false);
                }
            }
        }
        catch (Exception)
        {
            // the line is only for fun
        }
    }

    void ShowStatus(Func<string> make, bool news, DateTime now)
    {
        var text = make();
        if (text.Length == 0) return;
        _statusMake = make;
        _statusIsNews = news;
        _statusShownAt = now;
        if (text == _statusText) return;
        _statusText = text;
        _statusSeq++;
        Changed?.Invoke(text, true);
    }
}
