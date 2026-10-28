namespace SCTools.View;

public sealed partial class WebSnapshot
{
    /// <summary>The Trip Planner tab: the stations for the map and, for every crew member, his Current Route and Queued routes tables and his planned routes.</summary>
    object Planner()
    {
        var plan = _t.Plan.GetPlan();
        var widest = 0;   // the Deliver/Collect column as wide as its longest text needs (like in the program), so a line does not wrap

        // a tick stays with the word before it (" ✓" never starts a line of its own)
        static string[] Keep(string[] cells) => cells.Select(c => c.Replace(" ✓", " ✓")).ToArray();
        ViewTable Table(IReadOnlyList<(string Header, string Align)> columns) => new(_p, columns.Select(c => new ViewColumn(c.Header, c.Align)).ToArray());

        // the Current Route: one stop per line, the worker colour on the line where he is
        ViewTable CurrentGrid(PlanWorkerView w, PlanRow current)
        {
            var table = Table(PlanText.CurrentColumns);
            var cells = PlanText.LineCells(current.Lines);
            var color = WorkerColor(w.Name);
            for (var i = 0; i < cells.Count; i++)
            {
                var here = i == current.Here;
                var row = table.Add(Keep(cells[i])).RowColor(here ? color : _p.Cell, here ? ViewPalette.TextOn(color) : _p.Text);
                row.Bold = here;
            }
            table.Add(Keep(PlanText.LineTotals(current.Lines))).RowColor(_p.Calc, _p.Text).Bold = true;
            foreach (var l in current.Lines) widest = Math.Max(widest, Measure(l.ActionText));
            return table;
        }

        // the Queued routes: the trips after it
        ViewTable QueuedGrid(List<PlanRow> queued)
        {
            var table = Table(PlanText.QueuedColumns);
            if (queued.Count == 0) table.Add("", "none", "", "", "", "", "").RowColor(_p.Cell, _p.Muted);
            foreach (var r in queued) table.Add(Keep(PlanText.QueuedCells(r))).RowColor(_p.PlanRowColor(r.Level) ?? _p.Cell, _p.Text);
            if (queued.Count > 0) table.Add(Keep(PlanText.PlanTotals(queued))).RowColor(_p.Calc, _p.Text).Bold = true;
            return table;
        }

        var workers = plan.Workers.Select(w =>
        {
            var shown = w.Rows.Where(r => !r.Hidden).ToList();
            return new
            {
                name = w.Name,
                color = ViewPalette.Hex(WorkerColor(w.Name)),
                heading = w.Name.ToUpperInvariant(),   // his name in capitals, shown in his colour
                title = "   –   Current location: " + PyroDistances.NameOf(w.LocationId) + (w.LocationByHand ? " (set by hand)" : ""),
                current = shown.Count > 0 ? CurrentGrid(w, shown[0]) : null,
                queued = shown.Count > 0 ? QueuedGrid(shown.Skip(1).ToList()) : null,
                routes = w.MapRows.Select(r => new
                {
                    label = (r.Label.Length > 0 && char.IsDigit(r.Label[0]) ? "Trip " : "") + r.Label + "  " + Gm(r.Gm),
                    stops = r.Stops,
                    from = r.From,
                    passed = r.Passed,
                    solid = r.Solid,
                }).ToList(),
            };
        }).ToList();

        return new
        {
            summary = plan.Summary,
            note = plan.Note,
            status = _t.Status.Text,
            statusKey = _t.Status.Seq,
            palette = ViewPalette.MapPalette.Select(ViewPalette.Hex).ToArray(),
            stations = PyroDistances.Points.Select(p => new { id = p.Id, name = p.Name, x = p.X, y = p.Y }).ToList(),
            backdrop = new
            {
                sun = new[] { PyroDistances.Sun.X, PyroDistances.Sun.Y },
                sunRadius = PyroDistances.SunRadius,
                orbits = PyroDistances.OrbitRadii,
                planets = PyroDistances.Planets.Select(q => new[] { q.X, q.Y }).ToList(),
            },
            workers,
            actionWidth = widest + 16,
        };
    }

    /// <summary>How wide a text is in the bold table font: measured by the program, else about 7.5 pixels a character (Segoe UI 10, bold).</summary>
    int Measure(string text) => _o.MeasureBold?.Invoke(text) ?? (int)Math.Ceiling(text.Length * 7.5);
}
