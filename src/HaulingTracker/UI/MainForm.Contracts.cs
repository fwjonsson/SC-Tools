namespace HaulingTracker.UI;

// The "Contracts" tab: every mission that is still going, and who of the party has it (it was shared with him) or took it.
public sealed partial class MainForm
{
    readonly DataGridView _contractGrid = new();
    string _contractsShown = "";
    bool _contractsFilling;

    Control BuildContractsPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.Controls.Add(Caption("Contracts", "Every mission that is still going, with the stops it has (✓ = picked up or delivered), so you can tell it from the missions in your game. The from-Ruin contracts (waste) are here too, with their drop-offs and SCU. Click a box to change who has the mission: green ✔ has it, red ✖ does not, gold marks the one who took the contract (his box can be changed too). The Trip Planner only plans a worker for missions he has. A mission that is not in the game any more (the logs did not say so) can be dropped with its Gone button, and brought back with the same button."), 0, 0);

        StyleGrid(_contractGrid);
        _contractGrid.Dock = DockStyle.Fill;
        _contractGrid.Margin = new Padding(0, 3, 0, 3);
        _contractGrid.AllowUserToAddRows = false;
        _contractGrid.AllowUserToDeleteRows = false;
        _contractGrid.CellClick += ContractClicked;   // a click on a member's box turns it on or off
        _contractGrid.DataError += (_, e) => e.ThrowException = false;
        t.Controls.Add(_contractGrid, 0, 1);
        return t;
    }

    void RefreshContracts()
    {
        if (_contractsFilling || _contractGrid.IsCurrentCellInEditMode) return;
        var missions = _contracts.ContractRows();
        var members = _data.Party.ToList();
        var sig = Theme.Dark + "¦" + string.Join("|", members) + "¦" + string.Join("¦", missions.Select(m =>
            m.Id + "|" + m.Multi + "|" + m.Cargo + "|" + m.Taker + "|" + m.Dropped + "|" + _contracts.ContractStopsText(m) + "|" + string.Join(",", members.Select(n => _contracts.HasMission(m, n) ? "1" : "0"))));
        if (sig == _contractsShown && _contractGrid.Columns.Count > 0) return;
        _contractsShown = sig;

        _contractsFilling = true;
        try
        {
            var firstShown = _contractGrid.FirstDisplayedScrollingRowIndex;
            _contractGrid.Rows.Clear();
            _contractGrid.Columns.Clear();
            _contractGrid.Columns.Add(Col(ContractCol.Contract, "Contract", 130, true));
            _contractGrid.Columns.Add(Col(ContractCol.Cargo, "Cargo", 130, true));
            _contractGrid.Columns.Add(Col(ContractCol.Stops, "Stops (\u2713 = picked up / delivered)", 300, true, DataGridViewContentAlignment.MiddleLeft));
            foreach (var n in members) _contractGrid.Columns.Add(Col(ContractCol.Member(n), n, 130, true));
            _contractGrid.Columns.Add(Col(ContractCol.Gone, "Gone", 130, true));
            AddNumberColumn(_contractGrid);
            foreach (DataGridViewColumn c in _contractGrid.Columns)
            {
                if (c.Name == GridCol.Number) continue;
                c.FillWeight = c.Width;
                c.MinimumWidth = 60;
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }

            foreach (var m in missions)
            {
                var contract = Contracts.ContractName(m);   // the same names as the rewards table
                var cells = new List<(string, object?)>
                {
                    (ContractCol.Contract, contract),
                    (ContractCol.Cargo, m.IsOut ? m.OutCargo ?? "Cargo" : string.IsNullOrEmpty(m.Cargo) ? "" : m.Cargo),
                    (ContractCol.Stops, _contracts.ContractStopsText(m)),
                };
                cells.AddRange(members.Select(n => (ContractCol.Member(n), (object?)"")));
                cells.Add((ContractCol.Gone, m.Dropped ? "Bring back" : "Gone"));
                cells.Add((GridCol.Number, ""));
                var row = _contractGrid.AddRow(cells.ToArray());
                row.Tag = m;
                if (m.Dropped)
                {
                    // marked as gone: grey, the boxes do not count
                    row.DefaultCellStyle.ForeColor = Theme.Muted;
                    foreach (var n in members) row.Cells[ContractCol.Member(n)].Value = "";
                }
                else
                {
                    foreach (var n in members) ShowHas(row.Cells[ContractCol.Member(n)], m, n, _contracts.HasMission(m, n));
                }
                var goneCell = row.Cells[ContractCol.Gone];
                goneCell.Style.ForeColor = Theme.Text;
                goneCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                goneCell.ToolTipText = m.Dropped ? "Count this mission again" : "This mission is not in the game any more: do not plan or count it";
            }
            NumberRows(_contractGrid);
            if (firstShown >= 0 && firstShown < _contractGrid.Rows.Count) _contractGrid.FirstDisplayedScrollingRowIndex = firstShown;
        }
        finally
        {
            _contractsFilling = false;
        }
    }

    static readonly Font HasFont = new("Segoe UI", 12f, FontStyle.Bold);

    /// <summary>Colours a box: green with a tick when he has the mission, red with a cross when he has not; gold for the one who took the contract (a tick or a cross).</summary>
    static void ShowHas(DataGridViewCell cell, Mission m, string member, bool on)
    {
        var taker = m.Taker != null && SameName(m.Taker, member);
        cell.Value = (on ? "\u2714" : "\u2716") + (taker ? "  took it" : "");
        var back = taker ? (on ? Theme.Gold : Theme.Orange) : on ? Theme.Green : Theme.Red;
        cell.Style.BackColor = back;
        cell.Style.SelectionBackColor = back;   // a click does not change the colour
        cell.Style.ForeColor = Theme.Text;
        cell.Style.SelectionForeColor = Theme.Text;
        cell.Style.Font = HasFont;
        cell.ToolTipText = taker ? member + " took this contract" : "";
    }

    /// <summary>A box was clicked: the choice is kept (so it wins over the logs) until it is the same as what the logs say.</summary>
    void ContractClicked(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _contractGrid.ColumnName(e.ColumnIndex);
        if (column == ContractCol.Gone && _contractGrid.Rows[e.RowIndex].Tag is Mission gm)
        {
            // drop the mission (or bring it back): it counts as abandoned everywhere, so the planner, the Session tab and the payouts forget it
            _contracts.ToggleGone(gm);
            _state.Save();
            BeginInvoke(new Action(() =>
            {
                _renderedVersion = -1;
                _contractsShown = "";
                _planShown = "";
                _plan.Invalidate();
                RefreshFromLog();
                RefreshContracts();
            }));
            return;
        }
        if (ContractCol.MemberOf(column) is not { } member || _contractGrid.Rows[e.RowIndex].Tag is not Mission m || m.Dropped) return;
        var on = !_contracts.HasMission(m, member);
        _contracts.SetHolder(m, member, on);
        _state.Save();
        ShowHas(_contractGrid.Rows[e.RowIndex].Cells[e.ColumnIndex], m, member, on);
        _contractsShown = "";   // the plan and the payouts use it
    }
}
