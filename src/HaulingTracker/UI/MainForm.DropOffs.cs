namespace HaulingTracker.UI;

// From-Ruin contracts ("Single to Multi", and the direct ones): the cargo is loaded at Ruin and dropped off at 2 to 4 stations (a direct one: at one). Alpha: every drop-off objective is one batch.
public sealed partial class MainForm
{
    /// <summary>
    /// Asks the SCU of drop-offs the game did not tell the size of (the "? SCU" of the Cargo column): type what the contract says, and the program works out the
    /// containers. Saved with your data; the game's own number wins once it writes it. 0 clears it.
    /// </summary>
    void AskScu(List<string> keys)
    {
        using var f = new Form
        {
            Text = "SCU of the load",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        var intro = new Label
        {
            Text = "The game has not said how many SCU this is. Type the SCU from the contract, and the program works out the containers.",
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 0, 0, 10),
        };
        table.Controls.Add(intro, 0, 0);
        table.SetColumnSpan(intro, 2);
        var inputs = new List<NumericUpDown>();
        for (var i = 0; i < keys.Count; i++)
        {
            table.Controls.Add(new Label { Text = _dropOffs.DropLabel(keys[i]) + ":", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) }, 0, i + 1);
            var input = new NumericUpDown { Minimum = 0, Maximum = 10000, Width = 90, Value = _data.TypedScu.GetValueOrDefault(keys[i]) };
            inputs.Add(input);
            table.Controls.Add(input, 1, i + 1);
        }
        var ok = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        table.Controls.Add(bar, 0, keys.Count + 1);
        table.SetColumnSpan(bar, 2);
        f.Controls.Add(table);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        ApplyTheme(f);
        if (f.ShowDialog(this) != DialogResult.OK) return;

        for (var i = 0; i < keys.Count; i++) _dropOffs.SetTypedScu(keys[i], (int)inputs[i].Value);
        _state.Save();
        BeginInvoke(new Action(() =>
        {
            _renderedVersion = -1;   // the logs are merged again, with the SCU you typed
            _planShown = "";
            _plan.Invalidate();
            RefreshFromLog();
            RefreshPlanner();
        }));
    }

    /// <summary>The Drop-off cell of a stop of the Stops board: what has to be brought out to that station (see Earnings.StopsBoard).</summary>
    void ShowDropOffs(DataGridViewRow row, IReadOnlyList<DropBatch> drops)
    {
        if (drops.Count == 0) return;
        var cell = row.Cells[StopCol.DropOff];
        cell.Value = DropOffs.StationText(drops);
        cell.Style.BackColor = Theme.Gold;
        cell.Style.ForeColor = Color.Black;
    }
}
