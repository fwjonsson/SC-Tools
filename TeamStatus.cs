using System.Globalization;

namespace HaulingTracker;

// The small team status line under the map of the Trip Planner (and the same line on the web page): what the crew is doing right now, taken from the logs
// (who sets course for which station, who arrives, loads, delivers, hands in, dies), mixed with some stats and a bit of fun. It only talks about what the logs show:
// a member without a log of his own is only in the lines that come from the runs.
public sealed partial class MainForm
{
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
    StatusLine? _statusLine;              // the line on the Trip Planner page, when it is on screen

    static readonly TimeSpan NewsWindow = TimeSpan.FromSeconds(45);   // an event is news for this long

    static Func<string> Const(string text) => () => text;

    static string Pick(string key, params string[] options) => options[(int)((uint)key.GetHashCode() % (uint)options.Length)];

    /// <summary>"23m 12s", "1h 05m 12s".</summary>
    static string StatusSpan(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalHours >= 1) return (int)d.TotalHours + "h " + d.Minutes.ToString("00") + "m " + d.Seconds.ToString("00") + "s";
        if (d.TotalMinutes >= 1) return d.Minutes + "m " + d.Seconds.ToString("00") + "s";
        return d.Seconds + "s";
    }

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

    // ------------------------------------------------------------------ quantum travel of a worker (only when his own log is read)

    /// <summary>The quantum travel steps in the log of this worker, or null when there is no log of him.</summary>
    List<QtEvent>? TravelOf(string name)
    {
        if (_log == null) return null;
        foreach (var kv in _log.QtByPlayer)
        {
            if (SameName(kv.Key, name)) return kv.Value;
        }
        return null;
    }

    /// <summary>
    /// True while a worker whose log shows his quantum travel has taken cargo out (or delivered it) at a place and has not selected the next destination yet: he is still
    /// loading. A worker without a log of his own is never loading, and neither is one whose log has no quantum travel before this moment (the game did not write it).
    /// </summary>
    bool StillLoading(string name, DateTime since)
    {
        if (since == default || _log == null) return false;
        var travel = TravelOf(name);
        if (travel == null || travel.Count == 0) return false;
        if (!travel.Any(e => e.Time < since)) return false;
        if (travel.Any(e => e.Kind == QtKind.Selected && e.Time > since)) return false;
        var now = _live ? DateTime.UtcNow : _log.LastTime;
        return now - since < TimeSpan.FromMinutes(15);   // somebody who stopped playing is not loading for hours
    }

    /// <summary>
    /// The station a worker is flying to, or has arrived at and is loading at: the latest quantum destination in his own log, when he set it after his last pickup,
    /// delivery or hand-in. Null when there is no log of him, when it is Ruin or a place that is not in the distance table, or when it was set long ago.
    /// </summary>
    string? HeadingFor(string name)
    {
        if (_log == null) return null;
        var travel = TravelOf(name);
        var last = travel?.Where(e => e.Kind == QtKind.Selected).OrderBy(e => e.Time).LastOrDefault();
        if (last == null || last.Time <= LastMoveOf(name)) return null;
        var now = _live ? DateTime.UtcNow : _log.LastTime;
        if (now - last.Time > TimeSpan.FromHours(1)) return null;   // somebody who stopped playing is not on his way any more
        var id = PyroDistances.IdOfQuantumTarget(last.Destination);
        return id == null || id == PyroDistances.Ruin ? null : id;
    }

    // ------------------------------------------------------------------ what is happening now

    /// <summary>The members of the crew the line can talk about.</summary>
    List<string> StatusWorkers()
    {
        var names = new List<string>();
        IEnumerable<string> logged = _log != null ? _log.Players : new HashSet<string>();
        foreach (var n in ActiveCrew().Concat(_trips.Select(t => t.Worker)).Concat(logged))
        {
            if (n.Length == 0 || n == "Unassigned" || names.Any(x => SameName(x, n))) continue;
            names.Add(n);
        }
        return names;
    }

    /// <summary>True while the clocks of the runs are counting: watching live, the newest session, with contracts still going.</summary>
    bool LiveRunning()
    {
        var latest = _sessions.Count > 0 && _current != null && _current.Key == _sessions[^1].Key;
        return _live && latest && HasOpenContracts();
    }

    /// <summary>The run a worker is flying now (not handed in). A run with only waste in it counts while it is recent.</summary>
    WorkerTrip? OpenRunOf(string name) =>
        _trips.Where(t => SameName(t.Worker, name) && !t.End.HasValue
                && (t.Pickups.Count > 0 || (t.Drops.Count > 0 && _log != null && _log.LastTime - RunLastActivity(t) <= StaleLeg)))
            .OrderBy(t => t.Start).LastOrDefault();

    /// <summary>The events of the last moments that the line announces, newest last. Every event has a key, so it is only shown once.</summary>
    List<StatusItem> StatusNews(DateTime now)
    {
        var news = new List<StatusItem>();
        if (_log == null) return news;
        bool Fresh(DateTime time) => now - time <= NewsWindow && now - time >= TimeSpan.FromSeconds(-10);

        // quantum travel: he sets course, his drive arrives
        foreach (var kv in _log.QtByPlayer)
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
        foreach (var run in _trips)
        {
            var who = run.Worker;
            if (who == "Unassigned") continue;
            foreach (var pick in run.Pickups)
            {
                if (!Fresh(pick.Time)) continue;
                var key = "pk|" + who + "|" + run.Number + "|" + pick.Zone;
                var cargo = pick.MissionIds.Select(id => _log.Missions.TryGetValue(id, out var m) ? m.Cargo : null).FirstOrDefault(c => c != null) ?? "cargo";
                var place = LocationName(pick.Zone);
                news.Add(new StatusItem
                {
                    Key = key,
                    Text = Const(Pick(key, who + " is loading " + cargo + " at " + place, who + " pulled " + cargo + " out of the elevator at " + place, who + " is wrestling " + cargo + " crates at " + place)),
                });
            }
            foreach (var cluster in Clusters(run.Collects, x => x.Time, x => x.Cargo))
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
            foreach (var cluster in Clusters(run.Drops, x => x.Time, x => x.Cargo + "|" + x.Zone))
            {
                if (!Fresh(cluster[^1].Time)) continue;
                var key = "wd|" + who + "|" + cluster[0].Time.Ticks;
                var scu = cluster.Sum(x => x.Scu);
                var spot = LocationName(cluster[0].Zone);
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
        foreach (var kv in _log.DeathsByPlayer)
        {
            var who = kv.Key;
            var since = _current?.Start ?? DateTime.MinValue;
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
        foreach (var tr in _log.Transfers)
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
        if (_log == null) return fun;
        var running = LiveRunning();
        var since = _current?.Start ?? DateTime.MinValue;

        var board = _inputs != null ? ComputeStats(_trips, _inputs, StationOf) : new Dictionary<string, WorkerStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in StatusWorkers())
        {
            var name = member;

            // on a trip: the clock counts on while the line is shown
            var open = OpenRunOf(name);
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
                var ends = _trips.Where(t => SameName(t.Worker, name) && t.End.HasValue && !t.Manual && !t.Left).Select(t => t.End ?? DateTime.MinValue).ToList();
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
        var finished = _trips.Where(t => t.End.HasValue && !t.Manual && !t.Left && t.Pickups.Count > 0 && (_inputs == null || !RunFailed(t, _inputs))).ToList();
        if (finished.Count >= 3)
        {
            var quick = finished.OrderBy(t => (t.End ?? t.Start) - t.Start).First();
            var slow = finished.OrderByDescending(t => (t.End ?? t.Start) - t.Start).First();
            stats.Add(Const("Fastest run so far: " + quick.Worker + ", " + StatusSpan((quick.End ?? quick.Start) - quick.Start)));
            stats.Add(Const("Longest run so far: " + slow.Worker + ", " + StatusSpan((slow.End ?? slow.Start) - slow.Start)));
        }
        if (running && since != DateTime.MinValue) stats.Add(() => "The session has been going for " + StatusSpan(DateTime.UtcNow - since));

        // quantum travel and ships (workers with a log of their own)
        foreach (var kv in _log.QtByPlayer)
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
        var deaths = _log.DeathsByPlayer.Select(kv => (Name: kv.Key, Count: kv.Value.Count(d => d >= since))).Where(x => x.Count > 0).OrderByDescending(x => x.Count).ToList();
        if (deaths.Count > 0) stats.Add(Const("Deaths this session: " + string.Join(", ", deaths.Select(x => x.Name + " " + x.Count))));

        // fun, two at a time (they change every few minutes)
        string[] jokes =
        {
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
            foreach (var run in _trips)
            {
                if (run.End.HasValue) times.Add(run.End.Value);
                if (run.LastEvent.HasValue) times.Add(run.LastEvent.Value);
            }
            foreach (var steps in _log.QtByPlayer.Values) times.AddRange(steps.Select(e => e.Time));
            if (times.Count > 0)
            {
                var latest = times.Max();
                if (now - latest >= TimeSpan.FromMinutes(5) && now - latest < TimeSpan.FromHours(2))
                    fun.Add(() => "Quiet for " + StatusSpan(DateTime.UtcNow - latest) + ". Everybody still alive out there?");
            }
        }
        return Interleave(live, stats, fun);
    }

    // ------------------------------------------------------------------ the line itself

    /// <summary>Every second: picks up fresh events, switches to the next line when it is time, and keeps a clock in the line counting. It never gets in the way of anything else.</summary>
    void AdvanceStatus()
    {
        try
        {
            if (_log == null) return;
            var now = DateTime.UtcNow;
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
                    _statusLine?.ShowText(text, false);
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
        _statusLine?.ShowText(text, true);
    }

    /// <summary>The line under the map: the text of the team status in small muted letters, fading over to the next text when it changes.</summary>
    sealed class StatusLine : Control
    {
        string _text = "";
        string _next = "";
        int _alpha = 255;
        int _dir;   // -1 = fading out, 1 = fading in, 0 = still
        readonly System.Windows.Forms.Timer _fade = new() { Interval = 40 };

        public StatusLine()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 11f, FontStyle.Italic);
            Height = 30;
            TabStop = false;
            _fade.Tick += (_, _) => Step();
        }

        /// <summary>Shows a text: at once, or fading the old text out and the new one in.</summary>
        public void ShowText(string text, bool fade)
        {
            if (!fade || !IsHandleCreated || _text.Length == 0 || text.Length == 0)
            {
                _text = text;
                _next = text;
                _alpha = 255;
                _dir = 0;
                _fade.Stop();
                Invalidate();
                return;
            }
            _next = text;
            if (_dir == 0)
            {
                _dir = -1;
                _fade.Start();
            }
            else if (_dir > 0)
            {
                _text = text;   // it was fading in: just carry on with the new text
            }
        }

        void Step()
        {
            if (_dir < 0)
            {
                _alpha -= 64;
                if (_alpha <= 0)
                {
                    _alpha = 0;
                    _text = _next;
                    _dir = 1;
                }
            }
            else if (_dir > 0)
            {
                _alpha += 30;
                if (_alpha >= 255)
                {
                    _alpha = 255;
                    _dir = 0;
                    _fade.Stop();
                }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_text.Length == 0) return;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(_alpha, 0, 255), Theme.Muted));
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString(_text, Font, brush, ClientRectangle, format);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _fade.Dispose();
            base.Dispose(disposing);
        }
    }
}
