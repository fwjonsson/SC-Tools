using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace HaulingTracker.UI.Controls;

/// <summary>
/// A top-down map of Pyro with the routes of trips drawn as lines with arrows. On the leg of every solid route that goes from where the worker is to his next stop
/// (the first leg, or the leg after the stops of a run that are done) darker waves run in the direction of the arrow.
/// </summary>
sealed class PyroMapView : Control
{
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<MapRoute> Routes { get; set; } = new();

    const float LegendRow = 16f;
    static readonly Color Space = Color.FromArgb(8, 8, 12);

    // the waves: darker bands that run along the leg, from the station where it starts to the station where it ends
    const float WaveLength = 28f;   // pixels from one dark band to the next
    const float WaveSpeed = 20f;    // pixels per second
    const float WaveDark = 0.18f;   // the bands have the colour of the route times this
    const int WaveAlpha = 215;      // how dark the middle of a band is (of 255)
    const float WaveInset = 8f;     // the waves stay clear of the stations at both ends of the leg
    static readonly Stopwatch WaveClock = Stopwatch.StartNew();   // one clock for every map: a map that is built anew goes on with the waves where the old one was

    /// <summary>The leg that has waves: where it is, and what is needed to draw the waves on it.</summary>
    sealed class WaveLeg
    {
        public PointF A, B;   // the ends of the leg, moved to the side like the line itself
        public float Ux, Uy;   // the direction of flight
        public float Length;   // of the leg
        public float Width;
        public PointF[] Head = Array.Empty<PointF>();   // the arrow head, which is drawn again on top of the waves
        public Color Color;
        public ColorBlend Blend = new();   // the shape of one wave: from clear, to dark, to clear
        public Rectangle Bounds;   // the part of the map that is painted again for every frame
    }

    readonly List<WaveLeg> _legs = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    Bitmap? _scene;   // the map without the waves: it is painted again only when the size or the routes change
    string _sceneKey = "";

    public PyroMapView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        MinimumSize = new Size(320, 320);
        _timer.Tick += (_, _) =>
        {
            if (!IsHandleCreated) return;
            foreach (var leg in _legs) Invalidate(leg.Bounds);   // only the waves move, so only their part of the map is painted again
        };
    }

    // the waves only run while the map is on screen
    void UpdateTimer() => _timer.Enabled = Visible && _legs.Count > 0;

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateTimer();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _scene?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>What the map without the waves looks like: if it is the same as the last time, the picture of that time is used again.</summary>
    string SceneKey(Graphics g)
    {
        var sb = new StringBuilder();
        sb.Append(Width).Append('x').Append(Height).Append('@').Append(g.DpiX).Append('|');
        foreach (var r in Routes)
        {
            sb.Append(r.Label).Append('~').Append(r.Color.ToArgb()).Append('~').Append(r.From).Append('~').Append(r.Bold ? 1 : 0).Append(r.Dashed ? 1 : 0).Append('~').Append(r.Passed).Append('~');
            sb.AppendJoin(',', r.Stops).Append('|');
        }
        return sb.ToString();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Width < 1 || Height < 1) return;
        var key = SceneKey(g);
        if (_scene == null || key != _sceneKey)
        {
            _scene?.Dispose();
            _scene = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
            _scene.SetResolution(g.DpiX, g.DpiY);   // the text gets the same size as on the screen
            _legs.Clear();
            using (var sg = Graphics.FromImage(_scene)) DrawScene(sg);
            _sceneKey = key;
            UpdateTimer();
        }
        g.DrawImage(_scene, new Rectangle(0, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel);
        DrawWaves(g);
    }

    /// <summary>The waves of this moment: dark bands over the line of each leg that has them, moving in the direction of the arrow.</summary>
    void DrawWaves(Graphics g)
    {
        if (_legs.Count == 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var phase = (float)(WaveClock.Elapsed.TotalSeconds * WaveSpeed % WaveLength);   // how far the bands have moved from the start of the leg
        foreach (var leg in _legs)
        {
            if (leg.Length <= 2 * WaveInset + 2) continue;   // a leg that short is hardly more than its two stations
            var a = new PointF(leg.A.X + leg.Ux * WaveInset, leg.A.Y + leg.Uy * WaveInset);
            var b = new PointF(leg.B.X - leg.Ux * WaveInset, leg.B.Y - leg.Uy * WaveInset);
            // one wave goes from p0 to p1 along the leg, it repeats all along it, and p0 moves on towards the end of the leg
            var p0 = new PointF(leg.A.X + leg.Ux * phase, leg.A.Y + leg.Uy * phase);
            var p1 = new PointF(p0.X + leg.Ux * WaveLength, p0.Y + leg.Uy * WaveLength);
            using var brush = new LinearGradientBrush(p0, p1, Color.Black, Color.Black) { WrapMode = WrapMode.Tile, InterpolationColors = leg.Blend };
            using var pen = new Pen(brush, leg.Width) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
            g.DrawLine(pen, a, b);
            using var headBrush = new SolidBrush(leg.Color);
            g.FillPolygon(headBrush, leg.Head);   // the arrow head itself has no waves
        }
    }

    static WaveLeg NewLeg(PointF a, PointF b, float ux, float uy, float length, float width, PointF[] head, Color color)
    {
        var shade = Color.FromArgb((int)(color.R * WaveDark), (int)(color.G * WaveDark), (int)(color.B * WaveDark));
        const int steps = 9;
        var blend = new ColorBlend(steps);
        for (var i = 0; i < steps; i++)
        {
            var t = i / (float)(steps - 1);
            blend.Positions[i] = t;
            blend.Colors[i] = Color.FromArgb((int)(WaveAlpha * (1 - Math.Cos(2 * Math.PI * t)) / 2), shade);   // clear, dark in the middle, clear again
        }
        var left = (int)Math.Floor(Math.Min(a.X, b.X)) - 14;
        var top = (int)Math.Floor(Math.Min(a.Y, b.Y)) - 14;
        var right = (int)Math.Ceiling(Math.Max(a.X, b.X)) + 14;
        var bottom = (int)Math.Ceiling(Math.Max(a.Y, b.Y)) + 14;
        return new WaveLeg
        {
            A = a, B = b, Ux = ux, Uy = uy, Length = length, Width = width, Head = head, Color = color, Blend = blend,
            Bounds = Rectangle.FromLTRB(left, top, right, bottom),
        };
    }

    /// <summary>Paints the map without the waves (and notes which legs get waves).</summary>
    void DrawScene(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Space);

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
        PointF Px(double x, double y) => new(ox + (float)((x - minX) * scale), oy + (float)((y - minY) * scale));

        // the backdrop: orbits, planets and the sun of the Pyro map (no names)
        var sun = Px(PyroDistances.Sun.X, PyroDistances.Sun.Y);
        using (var ring = new Pen(Color.FromArgb(95, 235, 235, 245), 1f))
        {
            foreach (var radius in PyroDistances.OrbitRadii)
            {
                var rr = (float)(radius * scale);
                g.DrawEllipse(ring, sun.X - rr, sun.Y - rr, rr * 2, rr * 2);
            }
        }
        using (var planet = new SolidBrush(Color.FromArgb(140, 85, 20)))
        {
            foreach (var (px, py) in PyroDistances.Planets)
            {
                var c = Px(px, py);
                var pr = Math.Max(3.5f, (float)(10 * scale));
                g.FillEllipse(planet, c.X - pr, c.Y - pr, pr * 2, pr * 2);
            }
        }
        var sr = Math.Max(7f, (float)(PyroDistances.SunRadius * scale));
        using (var halo = new SolidBrush(Color.FromArgb(55, 255, 70, 30)))
            g.FillEllipse(halo, sun.X - sr * 1.9f, sun.Y - sr * 1.9f, sr * 3.8f, sr * 3.8f);
        using (var disc = new SolidBrush(Color.FromArgb(255, 58, 24)))
            g.FillEllipse(disc, sun.X - sr, sun.Y - sr, sr * 2, sr * 2);

        // routes: the bold ones on top, every route a little to the side of the others so overlapping legs stay visible
        var count = Routes.Count;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < count; i++)
            {
                var route = Routes[i];
                if (route.Bold != (pass == 1)) continue;
                var ids = new List<string> { route.From ?? PyroDistances.Ruin };
                ids.AddRange(route.Stops);
                ids.Add(PyroDistances.Ruin);
                var shift = (i - (count - 1) / 2f) * 3.5f;
                using var pen = route.Dashed
                    ? new Pen(Color.FromArgb(235, route.Color), route.Bold ? 3.5f : 2.5f) { DashStyle = DashStyle.Dot }
                    : new Pen(Color.FromArgb(route.Bold ? 255 : 170, route.Color), route.Bold ? 3.5f : 2f);
                using var fill = new SolidBrush(route.Color);
                var waved = false;   // the waves run on one leg of a solid route only: from where the worker is to his next stop
                for (var s = 0; s + 1 < ids.Count; s++)
                {
                    if (ids[s] == ids[s + 1]) continue;
                    if (route.Bold && !route.Dashed && s < route.Passed) continue;   // a leg to a stop that is done is gone: the run starts at the leg he is flying now
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
                    var head = new[]
                    {
                        new PointF(c.X + ux * size, c.Y + uy * size),
                        new PointF(c.X - ux * size * 0.7f + nx * size * 0.6f, c.Y - uy * size * 0.7f + ny * size * 0.6f),
                        new PointF(c.X - ux * size * 0.7f - nx * size * 0.6f, c.Y - uy * size * 0.7f - ny * size * 0.6f),
                    };
                    g.FillPolygon(fill, head);
                    if (route.Bold && !route.Dashed && !waved && s >= route.Passed)
                    {
                        waved = true;
                        _legs.Add(NewLeg(a, b, ux, uy, len, 3.5f, head, route.Color));
                    }
                }
            }
        }

        // stations: those on a route are filled, the rest are faint
        var used = Routes.SelectMany(r => r.Stops).ToHashSet();
        using var textBrush = new SolidBrush(Color.FromArgb(236, 236, 242));      // the map is always dark, whatever the theme
        using var faintBrush = new SolidBrush(Color.FromArgb(150, 150, 160));
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
            if (legend[i].Dashed) pen.DashStyle = DashStyle.Dot;
            g.DrawLine(pen, x, y + 8, x + 16, y + 8);
            g.DrawString(legend[i].Label, font, textBrush, x + 20, y);
        }
    }
}
