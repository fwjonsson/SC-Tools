using System.Runtime.InteropServices;

namespace HaulingTracker.UI.Controls;

/// <summary>A tab control that draws itself in the dark look. In the light look it is the normal tab control.</summary>
sealed class ThemedTabControl : TabControl
{
    public void RefreshStyle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, Theme.Dark);
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
