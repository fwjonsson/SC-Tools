using System.Runtime.InteropServices;

namespace HaulingTracker;

/// <summary>The colours of the light and the dark look. The light values are the original colours of the program.</summary>
static class Theme
{
    public static bool Dark;

    static Color P(Color light, Color dark) => Dark ? dark : light;
    static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    public static Color Window => P(Color.White, C(30, 30, 32));
    public static Color Cell => P(Color.White, C(37, 37, 40));
    public static Color Input => P(Color.White, C(52, 52, 57));
    public static Color Text => P(Color.Black, C(232, 232, 232));
    public static Color Muted => P(Color.DimGray, C(152, 152, 160));
    public static Color Header => P(C(54, 92, 138), C(38, 64, 100));
    public static Color Calc => P(C(243, 248, 253), C(45, 54, 67));
    public static Color Lines => P(Color.Black, C(78, 78, 86));
    public static Color Selection => P(C(200, 220, 245), C(62, 96, 146));
    public static Color SelectionText => P(Color.Black, Color.White);

    public static Color Red => P(C(253, 224, 224), C(100, 46, 46));
    public static Color Green => P(C(226, 244, 226), C(38, 72, 50));
    public static Color Orange => P(C(255, 222, 178), C(110, 74, 30));
    public static Color Yellow => P(C(255, 244, 204), C(96, 84, 30));
    public static Color Gold => P(C(255, 240, 184), C(104, 88, 26));

    public static Color Button => C(60, 60, 66);
    public static Color ButtonOff => C(44, 44, 48);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    static bool _nativeUsed;

    /// <summary>Dark title bar (Windows 10 1809 and later).</summary>
    public static void TitleBar(Form f)
    {
        try
        {
            if (!f.IsHandleCreated) return;
            var v = Dark ? 1 : 0;
            DwmSetWindowAttribute(f.Handle, 20, ref v, sizeof(int));
            DwmSetWindowAttribute(f.Handle, 19, ref v, sizeof(int));
        }
        catch
        {
            // an older Windows: the title bar stays light
        }
    }

    /// <summary>Dark scroll bars and drop-down lists.</summary>
    public static void Native(Control c)
    {
        if (!Dark && !_nativeUsed) return;
        _nativeUsed = true;
        One(c);
        foreach (Control child in c.Controls) One(child);
    }

    static void One(Control c)
    {
        try
        {
            if (!c.IsHandleCreated)
            {
                // the handle comes later (for example the scroll bars of a grid): style it then
                c.HandleCreated += (_, _) => Set(c);
                return;
            }
            Set(c);
        }
        catch
        {
            // not supported: keep the normal look
        }
    }

    static void Set(Control c)
    {
        try
        {
            SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch
        {
            // not supported: keep the normal look
        }
    }
}

/// <summary>A tab control that draws itself in the dark look. In the light look it is the normal tab control.</summary>
sealed class ThemedTabControl : TabControl
{
    public void RefreshStyle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizeDoubleBuffer | ControlStyles.ResizeRedraw, Theme.Dark);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Theme.Dark)
        {
            base.OnPaint(e);
            return;
        }

        var g = e.Graphics;
        g.Clear(Theme.Window);
        for (var i = 0; i < TabCount; i++)
        {
            var r = GetTabRect(i);
            var selected = i == SelectedIndex;
            using (var back = new SolidBrush(selected ? Theme.Header : Theme.Cell)) g.FillRectangle(back, r);
            TextRenderer.DrawText(g, TabPages[i].Text, Font, r, selected ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        using var pen = new Pen(Theme.Header, 2);
        var bottom = TabCount > 0 ? GetTabRect(0).Bottom : 0;
        g.DrawLine(pen, 0, bottom, Width, bottom);
    }
}
