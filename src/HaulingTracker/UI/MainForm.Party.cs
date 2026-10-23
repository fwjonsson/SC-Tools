namespace HaulingTracker.UI;

// The Party table on the front page (top right): who is in the party, who is active, and since when.
// Every change of Active is saved with its time, so someone can join or leave in the middle of a session and the
// Debts tab still knows who was there when a mission paid out.
public sealed partial class MainForm
{
    readonly DataGridView _partyGrid = new();
    readonly TextBox _partyName = new();
    readonly Button _partyAddBtn = new();
    readonly Button _partyRemoveBtn = new();
    bool _partyUpdating;

    // ------------------------------------------------------------------ the panel

    Control BuildPartyPanel()
    {
        _data.PartyTrackingSince ??= DateTime.UtcNow;

        var p = _partyPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
        };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));   // the title: Party
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));   // the table, as high as its members (RefreshPartyPanel)
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, PartyRowHeight));   // the entry row: it looks like the next row of the table

        StyleGrid(_partyGrid);
        _partyGrid.Dock = DockStyle.Fill;
        _partyGrid.Margin = new Padding(0, 0, 0, 0);
        _partyGrid.AllowUserToAddRows = false;
        _partyGrid.AllowUserToDeleteRows = false;
        _partyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _partyGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _partyGrid.Columns.Add(Col(PartyCol.Member, "Member", PartyColMember, true, DataGridViewContentAlignment.MiddleLeft));
        _partyGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = PartyCol.Active, HeaderText = "Active", Width = PartyColActive, SortMode = DataGridViewColumnSortMode.NotSortable });
        _partyGrid.Columns.Add(Col(PartyCol.Batches, "Batches", PartyColBatches, false));
        var shipCol = new DataGridViewComboBoxColumn { Name = PartyCol.Ship, HeaderText = "Ship", Width = PartyColShip, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
        shipCol.Items.Add("");
        foreach (var s in Ships.All) shipCol.Items.Add(s.Label);
        _partyGrid.Columns.Add(shipCol);
        var driveCol = new DataGridViewComboBoxColumn { Name = PartyCol.Drive, HeaderText = "QT Drive", Width = PartyColDrive, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
        driveCol.Items.Add("");
        foreach (var d in QuantumDrives.All.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Size)) driveCol.Items.Add(d.Label);
        _partyGrid.Columns.Add(driveCol);
        _partyGrid.Columns.Add(Col(PartyCol.Color, "Color", PartyColColor, true));   // click it to pick the colour of this worker on the Trip Planner Overview
        foreach (DataGridViewColumn c in _partyGrid.Columns)
        {
            c.FillWeight = c.Width;
            c.MinimumWidth = 40;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
        _partyGrid.ScrollBars = ScrollBars.Vertical;
        _partyGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_partyGrid.IsCurrentCellDirty && _partyGrid.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
                _partyGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _partyGrid.CellValueChanged += PartyGrid_CellValueChanged;
        _partyGrid.CellFormatting += (_, e) =>
        {
            // the names are shown in capitals (only shown: the name itself stays as it is, the logs and the saved data use it)
            if (e.RowIndex >= 0 && e.Value is string name && _partyGrid.ColumnName(e.ColumnIndex) == PartyCol.Member)
            {
                e.Value = name.ToUpperInvariant();
                e.FormattingApplied = true;
            }
        };
        _partyGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _partyGrid.ColumnName(e.ColumnIndex) != PartyCol.Color) return;
            var name = Convert.ToString(_partyGrid.Rows[e.RowIndex].Cells[PartyCol.Member].Value) ?? "";
            if (name.Length == 0) return;
            using var dlg = new ColorDialog { Color = WorkerColor(name), FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _crew.SetColor(name, dlg.Color.ToArgb());
            PartyChanged();
        };
        _partyGrid.DataError += (_, e) => e.ThrowException = false;

        // The entry row: the name box is a cell under Member / Active, Add sits under Batches and Remove under Ship, QT Drive and Color
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, PartyColMember + PartyColActive));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, PartyColBatches));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, PartyColShip + PartyColDrive + PartyColColor));
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, PartyRowHeight));
        _partyName.BorderStyle = BorderStyle.None;
        _partyName.Dock = DockStyle.Fill;
        _partyName.PlaceholderText = "New member";
        _partyName.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AddPartyMember();
        };
        foreach (var (b, text) in new[] { (_partyAddBtn, "Add"), (_partyRemoveBtn, "Remove") })
        {
            b.Text = text;
            b.AutoSize = false;
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0);
            b.Padding = new Padding(0);
        }
        _partyAddBtn.Click += (_, _) => AddPartyMember();
        _partyRemoveBtn.Click += (_, _) => RemovePartyMember();

        // the name box looks like a cell of the table: a bordered cell with the text a little in from the edge
        var cell = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(6, 4, 4, 0), Tag = "input" };
        cell.Controls.Add(_partyName);
        cell.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Lines);
            e.Graphics.DrawRectangle(pen, 0, 0, cell.Width - 1, cell.Height - 1);
        };
        bar.Controls.Add(cell, 0, 0);
        bar.Controls.Add(_partyAddBtn, 1, 0);
        bar.Controls.Add(_partyRemoveBtn, 2, 0);

        p.Controls.Add(SectionTitle("Party"), 0, 0);
        p.Controls.Add(_partyGrid, 0, 1);
        p.Controls.Add(bar, 0, 2);
        return p;
    }

    // the party table: one row per member (up to PartyMaxRows, then it scrolls) with the entry row right under the last one
    const int PartyRowHeight = 26;
    const int PartyMaxRows = 6;
    // proportions of the columns (the entry row under the table uses the same ones)
    const int PartyColMember = 110, PartyColActive = 64, PartyColBatches = 78, PartyColShip = 170, PartyColDrive = 116, PartyColColor = 60;
    TableLayoutPanel? _partyPanel;
    int _partyHeight = PartyMaxHeight;   // as drawn now, the party table and its entry row

    /// <summary>The height of the party table with all its rows, and of its entry row.</summary>
    const int PartyMaxHeight = TitleHeight + 28 + PartyMaxRows * PartyRowHeight + 2 + PartyRowHeight;   // with its title

    void FitPartyGrid()
    {
        if (_partyPanel == null) return;
        var rows = Math.Clamp(_partyGrid.Rows.Count, 1, PartyMaxRows);
        var grid = _partyGrid.ColumnHeadersHeight + rows * _partyGrid.RowTemplate.Height + 2;
        _partyPanel.RowStyles[0].Height = TitleHeight;
        _partyPanel.RowStyles[1].Height = grid;
        _partyPanel.RowStyles[2].Height = PartyRowHeight;
        _partyHeight = TitleHeight + grid + PartyRowHeight;
    }

    void RefreshPartyPanel()
    {
        if (_partyGrid.IsCurrentCellInEditMode) return;
        _partyUpdating = true;
        try
        {
            var selected = _partyGrid.CurrentRow?.Cells[PartyCol.Member].Value as string;
            _partyGrid.Rows.Clear();
            foreach (var name in _data.Party)
            {
                var active = _crew.IsActiveAt(name, DateTime.UtcNow);
                var row = _partyGrid.AddRow(
                    (PartyCol.Member, name),
                    (PartyCol.Active, active),
                    (PartyCol.Batches, Math.Max(1, _crew.ShipCapacityOf(name))),
                    (PartyCol.Ship, _crew.ShipOf(name)?.Label ?? ""),
                    (PartyCol.Drive, _crew.DriveOf(name)?.Label ?? ""),
                    (PartyCol.Color, ""));
                if (!active) row.DefaultCellStyle.ForeColor = Theme.Muted;
                var swatch = row.Cells[PartyCol.Color].Style;
                swatch.BackColor = swatch.SelectionBackColor = WorkerColor(name);
            }
            if (selected != null)
            {
                foreach (DataGridViewRow r in _partyGrid.Rows)
                {
                    if (SameName(Convert.ToString(r.Cells[PartyCol.Member].Value) ?? "", selected)) r.Selected = true;
                }
            }
        }
        finally
        {
            _partyUpdating = false;
        }
        FitPartyGrid();
        FitTopArea();
    }

    void PartyChanged()
    {
        _state.Save();
        BeginInvoke(new Action(() =>
        {
            _renderedVersion = -1;
            RefreshFromLog();
            RefreshPartyPanel();
            RefreshDebts();
            if (_planBody.Visible && _planBody.IsHandleCreated) RefreshPlanner();
        }));
    }

    void PartyGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_partyUpdating || e.RowIndex < 0) return;
        var row = _partyGrid.Rows[e.RowIndex];
        var name = Convert.ToString(row.Cells[PartyCol.Member].Value) ?? "";
        if (name.Length == 0) return;

        var column = _partyGrid.ColumnName(e.ColumnIndex);
        if (column == PartyCol.Active)
        {
            var active = row.Cells[PartyCol.Active].Value is true;
            if (active == _crew.IsActiveAt(name, DateTime.UtcNow)) return;
            _crew.SetMemberActive(name, active);
            SetStatus(name + (active ? " joined the party." : " left the party.") + " Mission payouts from now on are split between the active members.");
        }
        else if (column == PartyCol.Batches) _crew.SetShipCapacity(name, Convert.ToString(row.Cells[PartyCol.Batches].Value) ?? "");
        else if (column == PartyCol.Ship) _crew.SetShip(name, Convert.ToString(row.Cells[PartyCol.Ship].Value));
        else if (column == PartyCol.Drive) _crew.SetDrive(name, Convert.ToString(row.Cells[PartyCol.Drive].Value));
        PartyChanged();
    }

    void AddPartyMember()
    {
        var name = _partyName.Text.Trim();
        if (name.Length == 0) return;
        if (!_crew.Join(name))
        {
            SetStatus(_data.Party.First(x => SameName(x, name)) + " is already in the party.");
            _partyName.Clear();
            return;
        }
        _partyName.Clear();
        SetStatus(name + " joined the party.");
        PartyChanged();
    }

    void RemovePartyMember()
    {
        var name = _partyGrid.CurrentRow?.Cells[PartyCol.Member].Value as string;
        if (string.IsNullOrEmpty(name))
        {
            SetStatus("Click a name in the party table first.");
            return;
        }
        var answer = MessageBox.Show(this,
            "Remove " + name + " from the party?\n\nHis earlier work and debts stay in the Debts tab, he just stops getting a share of new payouts.",
            "Remove from Party", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        _crew.Remove(name);
        SetStatus(name + " was removed from the party.");
        PartyChanged();
    }
}
