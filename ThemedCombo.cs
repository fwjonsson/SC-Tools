namespace HaulingTracker;

/// <summary>A drop-down list that is painted in the button colour in dark mode (the native one stays white).</summary>
public sealed class ThemedCombo : ComboBox
{
    const int WM_PAINT = 0x000F;

    public ThemedCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        Color back, fore;
        if (Theme.Dark)
        {
            back = selected ? Theme.Selection : Theme.Button;
            fore = Theme.Text;
        }
        else
        {
            back = selected ? SystemColors.Highlight : SystemColors.Window;
            fore = selected ? SystemColors.HighlightText : SystemColors.WindowText;
        }
        using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, e.Bounds, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_PAINT || !Theme.Dark || !IsHandleCreated) return;

        // paint the closed box over the native one
        using var g = Graphics.FromHwnd(Handle);
        var r = ClientRectangle;
        using (var b = new SolidBrush(Theme.Button)) g.FillRectangle(b, r);
        using (var pen = new Pen(Theme.Lines)) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);

        var textRect = new Rectangle(4, 0, r.Width - 24, r.Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var cx = r.Width - 12;
        var cy = r.Height / 2;
        using var arrow = new SolidBrush(Theme.Text);
        g.FillPolygon(arrow, new[] { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 2) });
    }
}
