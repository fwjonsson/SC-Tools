using System.Globalization;

namespace HaulingTracker.UI;

// From-Ruin contracts ("Single to Multi"): the cargo is loaded at Ruin and dropped off at 2 to 4 stations. Alpha: every drop-off objective is one batch.
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
            _planCore = null;
            RefreshFromLog();
            RefreshPlanner();
        }));
    }

    /// <summary>The Drop-off column of the Stops board: what has to be brought out to every station (a station with only drop-offs gets a row of its own).</summary>
    void FillDropOffs()
    {
        if (!_logGrid.Columns.Contains(StopCol.DropOff)) return;
        var drops = _dropOffs.OpenDrops();
        foreach (var byStation in drops.GroupBy(d => d.Station ?? ("zone " + d.Zone)))
        {
            var text = DropOffs.StationText(byStation);
            var zone = byStation.First().Zone;
            DataGridViewRow? row = null;
            foreach (DataGridViewRow r in _logGrid.Rows)
            {
                if (r.Tag is string z && (z == zone || (byStation.First().Station != null && _places.StationOf(z) == byStation.First().Station))) { row = r; break; }
            }
            if (row == null)
            {
                row = _logGrid.AddRow((StopCol.Location, _places.LocationName(zone)), (StopCol.FromRuin, _places.DistanceText(zone)), (StopCol.Batches, ""), (StopCol.Worker, ""), (StopCol.Value, ""), (StopCol.Notes, ""));
                row.Tag = zone;
            }
            var cell = row.Cells[StopCol.DropOff];
            cell.Value = text;
            cell.Style.BackColor = Theme.Gold;
            cell.Style.ForeColor = Color.Black;
        }
    }
}
