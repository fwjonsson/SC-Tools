using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace HaulingTracker;

/// <summary>One trip on the map: Ruin, the stops in flying order, back to Ruin.</summary>
sealed class MapRoute
{
    public string Label = "";   // shown in the legend (empty = not in the legend)
    public Color Color;
    public List<string> Stops = new();   // station ids
    public bool Bold;
    public object? Tag;
}

/// <summary>A top-down map of Pyro with the routes of trips drawn as lines with arrows.</summary>
sealed class PyroMapView : Control
{
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<MapRoute> Routes { get; set; } = new();

    const float LegendRow = 16f;

    public PyroMapView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        MinimumSize = new Size(320, 320);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Cell);

        var pts = PyroDistances.Points.ToDictionary(p => p.Id);
        var minX = pts.Values.Min(p => p.X);
        var maxX = pts.Values.Max(p => p.X);
        var minY = pts.Values.Min(p => p.Y);
        var maxY = pts.Values.Max(p => p.Y);

        // the legend goes in columns along the top
        var legend = Routes.Where(r => r.Label.Length > 0).ToList();
        var columns = Math.Max(1, (int)(Width / 170f));
        var legendRows = (legend.Count + columns - 1) / columns;
        const float left = 40f, right = 150f, bottom = 24f;
        var top = 14f + legendRows * LegendRow;

        var w = Math.Max(10f, Width - left - right);
        var h = Math.Max(10f, Height - top - bottom);
        var scale = (float)Math.Min(w / (maxX - minX), h / (maxY - minY));
        var ox = left + (w - (float)(maxX - minX) * scale) / 2f;
        var oy = top + (h - (float)(maxY - minY) * scale) / 2f;
        PointF P(string id) => new(ox + (float)((pts[id].X - minX) * scale), oy + (float)((pts[id].Y - minY) * scale));

        // routes: the bold ones on top, every route a little to the side of the others so overlapping legs stay visible
        var count = Routes.Count;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < count; i++)
            {
                var route = Routes[i];
                if (route.Bold != (pass == 1)) continue;
                var ids = new List<string> { PyroDistances.Ruin };
                ids.AddRange(route.Stops);
                ids.Add(PyroDistances.Ruin);
                var shift = (i - (count - 1) / 2f) * 3.5f;
                using var pen = new Pen(Color.FromArgb(route.Bold ? 255 : 170, route.Color), route.Bold ? 3.5f : 2f);
                using var fill = new SolidBrush(route.Color);
                for (var s = 0; s + 1 < ids.Count; s++)
                {
                    if (ids[s] == ids[s + 1]) continue;
                    var a = P(ids[s]);
                    var b = P(ids[s + 1]);
                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    var len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1f) continue;
                    var ux = dx / len;
                    var uy = dy / len;
                    var nx = -uy;
                    var ny = ux;
                    a = new PointF(a.X + nx * shift, a.Y + ny * shift);
                    b = new PointF(b.X + nx * shift, b.Y + ny * shift);
                    g.DrawLine(pen, a, b);
                    var c = new PointF(a.X + (b.X - a.X) * 0.62f, a.Y + (b.Y - a.Y) * 0.62f);   // arrow head: the direction of flight
                    var size = route.Bold ? 8f : 6f;
                    g.FillPolygon(fill, new[]
                    {
                        new PointF(c.X + ux * size, c.Y + uy * size),
                        new PointF(c.X - ux * size * 0.7f + nx * size * 0.6f, c.Y - uy * size * 0.7f + ny * size * 0.6f),
                        new PointF(c.X - ux * size * 0.7f - nx * size * 0.6f, c.Y - uy * size * 0.7f - ny * size * 0.6f),
                    });
                }
            }
        }

        // stations: those on a route are filled, the rest are faint
        var used = Routes.SelectMany(r => r.Stops).ToHashSet();
        using var textBrush = new SolidBrush(Theme.Text);
        using var faintBrush = new SolidBrush(Theme.Muted);
        using var font = new Font("Segoe UI", 8.5f);
        using var bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var farFormat = new StringFormat { Alignment = StringAlignment.Far };
        foreach (var p in pts.Values)
        {
            var at = P(p.Id);
            if (p.Id == PyroDistances.Ruin)
            {
                g.FillRectangle(textBrush, at.X - 6, at.Y - 6, 12, 12);
                g.DrawString(p.Name, bold, textBrush, at.X + 9, at.Y - 9);
                continue;
            }
            var on = used.Contains(p.Id);
            var r = on ? 5f : 3.5f;
            g.FillEllipse(on ? textBrush : faintBrush, at.X - r, at.Y - r, r * 2, r * 2);
            if (p.Id == "73041")   // Orbituary sits almost on top of Starlight: its name goes to the left
                g.DrawString(p.Name, font, on ? textBrush : faintBrush, new RectangleF(at.X - 190, at.Y - 9, 182, 18), farFormat);
            else
                g.DrawString(p.Name, font, on ? textBrush : faintBrush, at.X + 8, at.Y - 8);
        }

        // legend
        for (var i = 0; i < legend.Count; i++)
        {
            var x = 8f + (i % columns) * 170f;
            var y = 6f + (i / columns) * LegendRow;
            using var pen = new Pen(legend[i].Color, 3f);
            g.DrawLine(pen, x, y + 8, x + 16, y + 8);
            g.DrawString(legend[i].Label, font, textBrush, x + 20, y);
        }
    }
}
