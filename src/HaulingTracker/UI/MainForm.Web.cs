using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HaulingTracker.UI;

public sealed partial class MainForm
{
    LiveWebServer? _web;
    readonly System.Windows.Forms.Timer _webTimer = new();
    readonly ToolStripMenuItem _webBtn = new();
    int _webTick;
    string _webLastDuration = "";
    bool _webClockRunning;

    string WebButtonText() => _web != null ? "Live Web View: On (port " + _web.Port + ")" : "Live Web View: Off";

    void InitWebView()
    {
        _webTimer.Interval = 2000;
        _webTimer.Tick += (_, _) => PublishWeb();
        FormClosing += (_, _) => StopWeb();
        if (_data.WebView) StartWeb(false);
    }

    void ToggleWeb()
    {
        if (_web != null)
        {
            StopWeb();
            _data.WebView = false;
            _state.Save();
            SetStatus("The live web view is off.");
            return;
        }
        if (StartWeb(true))
        {
            _data.WebView = true;
            _state.Save();
        }
    }

    bool StartWeb(bool announce)
    {
        var port = _data.WebViewPort is >= 1024 and <= 65535 ? _data.WebViewPort : 8765;
        try
        {
            var web = new LiveWebServer(port);
            web.Start();
            _web = web;
        }
        catch (Exception ex)
        {
            _web = null;
            _webBtn.Text = WebButtonText();
            SetStatus("Could not start the live web view on port " + port + ": " + ex.Message);
            return false;
        }
        _webBtn.Text = WebButtonText();
        _webTick = 0;
        _webTimer.Start();
        PublishWeb();
        SetStatus("Live web view on http://localhost:" + port + " (read only).");
        return true;
    }

    void StopWeb()
    {
        _webTimer.Stop();
        _web?.Dispose();
        _web = null;
        _webBtn.Text = WebButtonText();
    }

    void PublishWeb()
    {
        if (_web == null || IsDisposed) return;
        try
        {
            // the Debts and History tables are filled when you open those tabs: keep them current, without touching them while you look at them
            if (_webTick++ % 5 == 0 && !_balanceGrid.Visible && !_balanceGrid.IsCurrentCellInEditMode) RefreshDebts();
            // the session clock is running when its text changed since the last update
            var duration = _durationVal.Text;
            _webClockRunning = duration.Length > 0 && duration != _webLastDuration;
            _webLastDuration = duration;
            _web.Json = BuildWebSnapshot();
        }
        catch (Exception ex)
        {
            SetStatus("Live web view: " + ex.Message);
        }
    }

    /// <summary>The tabs as data: the same tables, with the colours they have on the screen.</summary>
    string BuildWebSnapshot()
    {
        var snapshot = new
        {
            updated = DateTime.Now.ToString("HH:mm:ss"),
            session = _sessionBox.Text,
            theme = new
            {
                bg = WebColor(Theme.Window),
                fg = WebColor(Theme.Text),
                header = WebColor(Theme.Header),
                line = WebColor(Theme.Lines),
                cell = WebColor(Theme.Cell),
            },
            summary = new[]
            {
                new { k = "Start Time", v = _startBox.Text, live = false },
                new { k = "Session Time", v = _durationVal.Text, live = _webClockRunning },   // the page counts it up between updates
                new { k = "Batches Delivered", v = _missionsVal.Text, live = false },
                new { k = "Active Missions", v = _missionsActiveVal.Text, live = false },
                new { k = "Missions Completed", v = _missionsDoneVal.Text, live = false },
                new { k = "Total aUEC Earned", v = _revenueVal.Text, live = false },
                new { k = "aUEC/Hour", v = _hourlyVal.Text, live = false },
            },
            rewards = WebGrid(_rewards),
            party = WebParty(),
            pickups = WebGrid(_logGrid),
            finished = WebGrid(_doneGrid),
            leaders = WebGrid(_statsGrid),
            runs = WebGrid(_tripGrid),
            planRuns = WebGrid(_planRunsGrid),   // the short copy under the map of the Trip Planner (#, Worker, Value, Started, Completed, Time, In flight)
            runsLive = _tripGrid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
                .Select(r => r.Tag is WorkerTrip t && !t.End.HasValue && (Convert.ToString(r.Cells[RunCol.Time].Value) ?? "").Length > 0).ToArray(),   // runs on the way: the page counts their Run Time up
            planner = WebPlan(_plan.GetPlan()),
            debts = WebDebts(),
            history = new { allTime = WebGrid(_allTimeGrid), best = _bestLine.Text, saved = WebGrid(_historyGrid) },
        };
        return JsonSerializer.Serialize(snapshot);
    }

    /// <summary>The Debts tab for everybody: earnings, the balance of every member with the others, and all money transfers.</summary>
    object WebDebts()
    {
        var party = _payouts.DebtPartyNames();
        var nets = DebtCalc.Nets(_data.Sessions.Values, _data.Transfers);
        var settle = DebtCalc.Settle(nets);
        var headers = new[] { ("Party Member", "left"), ("Balance", "right"), ("Meaning", "left") };
        var balances = party.Select(me => new
        {
            name = me,
            grid = WebSimple(headers, party.Where(o => !SameName(o, me)).Select(o =>
            {
                var shown = DebtCalc.Between(settle, me, o) + _payouts.GetAdj(me, o);
                return new[] { o, Signed(shown), Payouts.Meaning(me, o, shown) };
            })),
        }).ToList();
        var transfers = WebSimple(
            new[] { ("Time", "center"), ("From", "center"), ("To", "center"), ("Amount", "right"), ("Payment", "center") },
            _data.Transfers.OrderByDescending(t => t.Time).Select(t => new[]
            {
                t.Time.ToLocalTime().ToString("d MMM HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                t.From,
                t.To,
                t.Amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                t.Payment ? "yes" : "",
            }));
        var payouts = WebSimple(
            new[] { ("Time", "center"), ("Mission", "left"), ("Payout", "right"), ("Shared Between", "left"), ("Each", "right") },
            _data.Payouts.OrderByDescending(r => r.Time).Select(r => new[]
            {
                r.Time.ToLocalTime().ToString("d MMM HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                r.Contract,
                r.Payout.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                string.Join(", ", r.Members),
                r.Each.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            }));
        return new { earnings = WebGrid(_earnGrid), balances, transfers, payouts, pos = WebColor(DoneGreen), neg = WebColor(LostRed) };
    }

    /// <summary>The party: no colour column, the whole row of a member has his colour (a member who is not active, a faded one).</summary>
    object WebParty()
    {
        var headers = new[] { ("Member", "left"), ("Active", "center"), ("Batches", "center"), ("QT Drive", "left") };
        return new
        {
            headers = headers.Select(h => new { text = h.Item1, align = h.Item2 }).ToList(),
            rows = _data.Party.Select(n =>
            {
                var active = _crew.IsActiveAt(n, DateTime.UtcNow);
                var color = WorkerColor(n);
                var back = active ? color : Faded(color);
                var cells = new[]
                {
                    n.ToUpperInvariant(),
                    active ? "Yes" : "No",
                    Math.Max(1, _crew.ShipCapacityOf(n)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _crew.DriveOf(n)?.Label ?? "",
                };
                return new
                {
                    cells,
                    back = cells.Select(_ => WebColor(back)).ToArray(),
                    fore = cells.Select(_ => WebColor(TextOn(back))).ToArray(),
                    bold = false,
                };
            }).ToList(),
        };
    }

    /// <summary>A plain table made from text: no special colours.</summary>
    static object WebSimple((string Text, string Align)[] headers, IEnumerable<string[]> rows)
    {
        var cell = WebColor(Theme.Cell);
        var text = WebColor(Theme.Text);
        return new
        {
            headers = headers.Select(h => new { text = h.Text, align = h.Align }).ToList(),
            rows = rows.Select(r => new
            {
                cells = r,
                back = r.Select(_ => cell).ToArray(),
                fore = r.Select(_ => text).ToArray(),
                bold = false,
            }).ToList(),
        };
    }

    /// <summary>The Trip Planner tab as data: the stations for the map and, for every crew member, his Current Route and Queued routes tables and his planned routes.</summary>
    object WebPlan(PlanView plan)
    {
        // the Current Route: what is done at every stop; the Queued routes: the trips after it (the same columns as in the program)
        object CurrentHeaders() => PlanText.CurrentColumns.Select(c => new { text = c.Header, align = c.Align }).ToArray();
        object QueuedHeaders() => PlanText.QueuedColumns.Select(c => new { text = c.Header, align = c.Align }).ToArray();
        var cell = WebColor(Theme.Cell);
        var text = WebColor(Theme.Text);
        var calc = WebColor(Theme.Calc);

        // a tick stays with the word before it (" ✓" never starts a line of its own)
        object PlainRow(string[] cells, string back, string fore, bool bold) => new
        {
            cells = cells.Select(c => c.Replace(" \u2713", "\u00a0\u2713")).ToArray(),
            back = Enumerable.Repeat(back, cells.Length).ToArray(),
            fore = Enumerable.Repeat(fore, cells.Length).ToArray(),
            bold,
        };

        // the Current Route: one stop per line, the worker colour on the line where he is
        object CurrentGrid(PlanWorkerView w, PlanRow current)
        {
            var cells = PlanText.LineCells(current.Lines);
            var rows = new List<object>();
            for (var i = 0; i < cells.Count; i++)
            {
                var here = i == current.Here;
                rows.Add(PlainRow(cells[i], here ? WebColor(WorkerColor(w.Name)) : cell, here ? WebColor(TextOn(WorkerColor(w.Name))) : text, here));
            }
            rows.Add(PlainRow(PlanText.LineTotals(current.Lines), calc, text, true));
            return new { headers = CurrentHeaders(), rows };
        }

        // the Queued routes: the trips after it, each with a short route tag
        object QueuedGrid(List<PlanRow> queued)
        {
            var rows = new List<object>();
            if (queued.Count == 0) rows.Add(PlainRow(new[] { "", "none", "", "", "", "", "" }, cell, WebColor(Theme.Muted), false));
            foreach (var r in queued) rows.Add(PlainRow(PlanText.QueuedCells(r), RowColor(r) is { } c ? WebColor(c) : cell, text, false));
            if (queued.Count > 0) rows.Add(PlainRow(PlanText.PlanTotals(queued), calc, text, true));
            return new { headers = QueuedHeaders(), rows };
        }

        var workers = plan.Workers.Select(w => new
        {
            name = w.Name,
            color = WebColor(WorkerColor(w.Name)),
            heading = PlanTitle(w),   // his name in capitals, shown in his colour
            title = "   \u2013   Current location: " + PyroDistances.NameOf(w.LocationId) + (w.LocationByHand ? " (set by hand)" : ""),   // where he is now: the logs keep it up to date
            current = w.Rows.FirstOrDefault(r => !r.Hidden) is { } first ? CurrentGrid(w, first) : null,
            queued = w.Rows.Any(r => !r.Hidden) ? QueuedGrid(w.Rows.Where(r => !r.Hidden).Skip(1).ToList()) : null,
            routes = w.MapRows.Select(r => new { label = (r.Label.Length > 0 && char.IsDigit(r.Label[0]) ? "Trip " : "") + r.Label + "  " + Gm(r.Gm), stops = r.Stops, from = r.From, passed = r.Passed, solid = r.Solid }).ToList(),
        }).ToList();
        return new
        {
            summary = plan.Summary,
            note = plan.Note,
            status = _feed.Text,
            statusKey = _feed.Seq,
            palette = MapPalette.Select(WebColor).ToArray(),
            stations = PyroDistances.Points.Select(p => new { id = p.Id, name = p.Name, x = p.X, y = p.Y }).ToList(),
            backdrop = new
            {
                sun = new[] { PyroDistances.Sun.X, PyroDistances.Sun.Y },
                sunRadius = PyroDistances.SunRadius,
                orbits = PyroDistances.OrbitRadii,
                planets = PyroDistances.Planets.Select(q => new[] { q.X, q.Y }).ToList(),
            },
            workers,
            actionWidth = PlanTextWidths(plan).Current + 16,   // the Deliver/Collect column as wide as its longest text needs (like in the program), so a line does not wrap
        };
    }

    static string WebColor(Color c) => "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2");

    static string WebAlign(DataGridViewContentAlignment a)
    {
        var name = a.ToString();
        return name.Contains("Right") ? "right" : name.Contains("Center") ? "center" : "left";
    }

    static Color WebBack(DataGridViewCell c, DataGridView g)
    {
        foreach (var style in new[] { c.Style, c.OwningRow.DefaultCellStyle, c.OwningColumn.DefaultCellStyle, g.DefaultCellStyle })
        {
            if (!style.BackColor.IsEmpty) return style.BackColor;
        }
        return Theme.Cell;
    }

    static Color WebFore(DataGridViewCell c, DataGridView g)
    {
        foreach (var style in new[] { c.Style, c.OwningRow.DefaultCellStyle, c.OwningColumn.DefaultCellStyle, g.DefaultCellStyle })
        {
            if (!style.ForeColor.IsEmpty) return style.ForeColor;
        }
        return Theme.Text;
    }

    static object WebGrid(DataGridView g)
    {
        var columns = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();   // as on screen: the # column is first
        var headers = columns.Select(c => new { text = c.HeaderText, align = WebAlign(c.DefaultCellStyle.Alignment) }).ToList();
        var rows = new List<object>();
        foreach (DataGridViewRow r in g.Rows)
        {
            if (r.IsNewRow) continue;
            var cells = columns.Select(c => r.Cells[c.Index]).ToList();
            rows.Add(new
            {
                cells = cells.Select(c => Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToArray(),
                back = cells.Select(c => WebColor(WebBack(c, g))).ToArray(),
                fore = cells.Select(c => WebColor(WebFore(c, g))).ToArray(),
                bold = (r.DefaultCellStyle.Font ?? g.DefaultCellStyle.Font)?.Bold == true,
            });
        }
        return new { headers, rows };
    }
}
