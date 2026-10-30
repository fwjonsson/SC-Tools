namespace SCTools.View;

public sealed partial class WebSnapshot
{
    /// <summary>
    /// A Route Planner tab (the old planner's, or the Route Planner (new) tab of the planner without a hub): the map (Pyro, with the stations on it) and, for every
    /// crew member, his Current Route and Queued routes tables and his planned routes.
    /// </summary>
    object Planner(PlanService service)
    {
        var plan = service.GetPlan();
        var widest = 0;   // the Job column as wide as its longest text needs (like in the program), so a line does not wrap

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
            if (queued.Count == 0) table.Add("", "none", "", "", "", "", "", "").RowColor(_p.Cell, _p.Muted);
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
                title = "   –   Current location: " + StarRoutes.NameOf(w.LocationId) + (w.LocationByHand ? " (set by hand)" : ""),
                location = StarRoutes.NameOf(w.LocationId),   // the station alone: the page adds the map's "since" to the title when the map has him there (30 Oct 2026)
                current = shown.Count > 0 ? CurrentGrid(w, shown[0]) : null,
                running = w.Rows.Any(r => r.Level == "active"),   // a run on the way: the Submit button ends it (30 Oct 2026)
                // the lines of the Current Route: the drop-offs on each (their keys), and whether the hauler types the SCU there (a planned trip's drop-off stop)
                lines = shown.Count > 0 ? shown[0].Lines.Select(l => new { keys = l.Keys, typable = l.Typable, scu = l.Parts.Count > 0 ? l.Parts.Sum() : 0 }).ToList() : null,
                queued = shown.Count > 0 ? QueuedGrid(shown.Skip(1).ToList()) : null,
                routes = w.MapRows.Select(r => new
                {
                    label = (r.Label.Length > 0 && char.IsDigit(r.Label[0]) ? "Trip " : "") + r.Label + "  " + Gm(r.Gm),
                    stops = r.Stops,
                    places = r.Places,   // every place of the route in order, the start and the ends included: the map draws these (stage 1b)
                    from = r.From,
                    passed = r.Passed,
                    solid = r.Solid,
                }).ToList(),
            };
        }).ToList();

        return new
        {
            tripScu = _o.TripScu,   // where a typed SCU goes (the site); nothing: the SCU is text
            tripEnd = _o.TripEnd,   // where the Submit button of a Current Route goes (the site); nothing: no button
            summary = plan.Summary,
            note = plan.Note,
            status = _t.Status.Text,
            statusKey = _t.Status.Seq,
            palette = ViewPalette.MapPalette.Select(ViewPalette.Hex).ToArray(),
            // the map: every system as the map engine draws it (the same places as the Map tool), and every place the routes and the workers touch, with its key
            // on the map and its system (stage 2: a route may cross a gateway; the page draws each system's part on that system's tab)
            maps = StarMap.Systems.Select(s => StarRoutes.CurrentAtlas().SystemData(s)).ToList(),
            places = PyroDistances.Points.Select(p => p.Id).Concat(plan.Workers.SelectMany(w => w.MapRows.SelectMany(r => r.Places).Append(w.LocationId))).Distinct()
                .Select(id => new { id, key = StarRoutes.KeyOf(id), name = StarRoutes.NameOf(id), system = StarRoutes.SystemOf(id) }).ToList(),
            workers,
            actionWidth = widest + 16,
        };
    }

    /// <summary>How wide a text is in the bold table font: measured by the program, else about 7.5 pixels a character (Segoe UI 10, bold).</summary>
    int Measure(string text) => _o.MeasureBold?.Invoke(text) ?? (int)Math.Ceiling(text.Length * 7.5);
}
