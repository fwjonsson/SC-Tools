namespace HaulingTracker;

// The dark mode switch: every colour comes from Theme, ApplyTheme walks all controls.
public sealed partial class MainForm
{
    readonly Button _themeBtn = new();

    static string ThemeButtonText() => Theme.Dark ? "Dark Mode: On" : "Dark Mode: Off";

    void ToggleDark()
    {
        Theme.Dark = !Theme.Dark;
        _data.DarkMode = Theme.Dark;
        Storage.Save(_data);
        _themeBtn.Text = ThemeButtonText();

        SuspendLayout();
        ApplyTheme(this);
        ResumeLayout(true);

        // rows carry their own colours: draw them again
        _renderedVersion = -1;
        if (_log != null) RefreshFromLog();
        RefreshDebts();
        FitTopArea();
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.TitleBar(this);
    }

    void ApplyTheme(Control c)
    {
        switch (c)
        {
            case Form f:
                f.BackColor = Theme.Window;
                f.ForeColor = Theme.Text;
                if (f.IsHandleCreated) Theme.TitleBar(f);
                else f.HandleCreated += (_, _) => Theme.TitleBar(f);
                break;
            case DataGridView g:
                ThemeGrid(g);
                return;
            case ThemedTabControl tc:
                tc.BackColor = Theme.Window;
                tc.ForeColor = Theme.Text;
                tc.RefreshStyle();
                break;
            case Button b:
                if (Theme.Dark)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.UseVisualStyleBackColor = false;
                    b.BackColor = Theme.Button;
                    b.ForeColor = Theme.Text;
                    b.FlatAppearance.BorderColor = Theme.Lines;
                    b.FlatAppearance.MouseOverBackColor = Color.FromArgb(78, 78, 86);
                }
                else
                {
                    b.FlatStyle = FlatStyle.Standard;
                    b.UseVisualStyleBackColor = true;
                    b.BackColor = SystemColors.Control;
                    b.ForeColor = SystemColors.ControlText;
                }
                break;
            case ComboBox cb:
                cb.FlatStyle = Theme.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                cb.BackColor = Theme.Dark ? Theme.Input : SystemColors.Window;
                cb.ForeColor = Theme.Dark ? Theme.Text : SystemColors.WindowText;
                Theme.Native(cb);
                break;
            case TextBox tb:
                tb.BackColor = Theme.Input;
                tb.ForeColor = Theme.Text;
                Theme.Native(tb);
                break;
            case ListBox lb:
                lb.BackColor = Theme.Input;
                lb.ForeColor = Theme.Text;
                Theme.Native(lb);
                break;
            case Label l:
                if ((l.Tag as string) == "calc")
                {
                    l.BackColor = Theme.Calc;
                    l.ForeColor = Theme.Text;
                }
                else
                {
                    l.ForeColor = (l.Tag as string) == "muted" ? Theme.Muted : Theme.Text;
                }
                break;
            default:
                c.BackColor = (c.Tag as string) == "input" ? Theme.Input : Theme.Window;
                c.ForeColor = Theme.Text;
                break;
        }

        foreach (Control child in c.Controls) ApplyTheme(child);
    }

    void ThemeGrid(DataGridView g)
    {
        StyleGrid(g);
        foreach (DataGridViewColumn col in g.Columns)
        {
            // columns filled in from the log are tinted, the ones you type in follow the grid
            if (col.DefaultCellStyle.BackColor != Color.Empty) col.DefaultCellStyle.BackColor = Theme.Calc;
        }
        Theme.Native(g);
        g.Invalidate();
    }
}
