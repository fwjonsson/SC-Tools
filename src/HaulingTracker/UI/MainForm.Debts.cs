using System.Globalization;

namespace HaulingTracker.UI;

// The "Debts" tab: earnings per worker, balance with every other party member and the money transfers found in the logs.
public sealed partial class MainForm
{
    readonly ComboBox _debtMember = new();
    readonly DataGridView _balanceGrid = new();
    readonly DataGridView _earnGrid = new();
    readonly DataGridView _txGrid = new();
    readonly DataGridView _payoutGrid = new();   // Mission Payouts, right of the money transfers
    readonly DataGridView _historyGrid = new();
    readonly Label _bestLine = new();
    string _debtSelected = "";
    bool _debtUpdating;

    /// <summary>A title in bold, with an optional short explanation after it in grey.</summary>
    static Control Caption(string title, string? hint = null)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        p.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Margin = new Padding(0, 8, 10, 0),
        });
        if (!string.IsNullOrEmpty(hint))
        {
            p.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = Color.DimGray, Tag = "muted", Margin = new Padding(0, 11, 0, 0) });
        }
        return p;
    }

    Control BuildDebtsPage()
    {
        // the balances and the earnings side by side, the money transfers under them over the full width
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        bar.Controls.Add(new Label { Text = "Party Member:", AutoSize = true, Margin = new Padding(0, 10, 6, 0) });
        _debtMember.DropDownStyle = ComboBoxStyle.DropDownList;
        _debtMember.Width = 170;
        _debtMember.Margin = new Padding(0, 6, 24, 4);
        _debtMember.SelectedIndexChanged += (_, _) =>
        {
            if (_debtUpdating) return;
            _debtSelected = _debtMember.SelectedItem as string ?? "";
            RefreshDebts();
        };
        bar.Controls.Add(_debtMember);
        var partyBtn = new Button();
        StyleButton(partyBtn, "Session Party…");
        partyBtn.Click += (_, _) => EditSessionParty();
        bar.Controls.Add(partyBtn);
        var fileBtn = new Button();
        StyleButton(fileBtn, "Data File…");
        fileBtn.Click += (_, _) => ChooseDataFile();
        bar.Controls.Add(fileBtn);
        t.Controls.Add(bar, 0, 0);
        t.SetColumnSpan(bar, 2);

        t.Controls.Add(Caption("Balances", "+ they owe the selected member, − the selected member owes them. Type over a number to correct it."), 0, 1);
        StyleGrid(_balanceGrid);
        _balanceGrid.Dock = DockStyle.Fill;
        _balanceGrid.Margin = new Padding(0, 3, 8, 3);
        _balanceGrid.AllowUserToAddRows = false;
        _balanceGrid.AllowUserToDeleteRows = false;
        _balanceGrid.Columns.Add(Col(BalanceCol.Member, "Party Member", 200, true, DataGridViewContentAlignment.MiddleLeft));
        var balanceCol = Col(BalanceCol.Balance, "Balance", 160, false, DataGridViewContentAlignment.MiddleRight);
        balanceCol.DefaultCellStyle.BackColor = CalcBlue;   // typed over like before, but the same colour as the rest of the table
        _balanceGrid.Columns.Add(balanceCol);
        _balanceGrid.Columns.Add(Col(BalanceCol.Meaning, "Meaning", 400, true, DataGridViewContentAlignment.MiddleLeft, true));
        _balanceGrid.CellEndEdit += BalanceEdited;
        t.Controls.Add(_balanceGrid, 0, 2);

        t.Controls.Add(Caption("Earnings", "Value of the pickup locations each worker carried"), 1, 1);
        StyleGrid(_earnGrid);
        _earnGrid.Dock = DockStyle.Fill;
        _earnGrid.Margin = new Padding(8, 3, 0, 3);
        _earnGrid.AllowUserToAddRows = false;
        _earnGrid.AllowUserToDeleteRows = false;
        _earnGrid.Columns.Add(Col(EarnCol.Worker, "Worker", 200, true, DataGridViewContentAlignment.MiddleLeft));
        _earnGrid.Columns.Add(Col(EarnCol.Session, "This Session", 160, true, DataGridViewContentAlignment.MiddleRight));
        _earnGrid.Columns.Add(Col(EarnCol.All, "All Sessions", 160, true, DataGridViewContentAlignment.MiddleRight));
        _earnGrid.Columns.Add(Col(EarnCol.Net, "Net Balance (+ Owed to Them)", 240, true, DataGridViewContentAlignment.MiddleRight, true));
        t.Controls.Add(_earnGrid, 1, 2);

        // the money transfers on the left, the payout of every finished mission on the right
        t.Controls.Add(Caption("Money Transfers", "Of the selected member. Tick Payment when a transfer pays off a debt."), 0, 3);
        t.Controls.Add(Caption("Mission Payouts", "Every finished mission: its payout and the members who had the mission and shared it. Type in the Payout box what a mission really paid (a short delivery pays less); an empty box goes back to the contract's reward."), 1, 3);
        StyleGrid(_txGrid);
        _txGrid.Dock = DockStyle.Fill;
        _txGrid.Margin = new Padding(0, 3, 8, 3);
        _txGrid.AllowUserToAddRows = false;
        _txGrid.AllowUserToDeleteRows = false;
        _txGrid.Columns.Add(Col(TransferCol.Time, "Time", 150, true));
        _txGrid.Columns.Add(Col(TransferCol.From, "From", 160, true));
        _txGrid.Columns.Add(Col(TransferCol.To, "To", 160, true));
        _txGrid.Columns.Add(Col(TransferCol.Amount, "Amount", 170, true, DataGridViewContentAlignment.MiddleRight));
        _txGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = TransferCol.Payment,
            HeaderText = "Payment",
            Width = 100,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = { BackColor = CalcBlue },   // the same colour as the rest of the table
        });
        _txGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_txGrid.IsCurrentCellDirty) _txGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _txGrid.CellValueChanged += TxChanged;
        FitColumns(_txGrid);
        _txGrid.ScrollBars = ScrollBars.Vertical;   // the columns share the width, the rows scroll
        t.Controls.Add(_txGrid, 0, 4);

        StyleGrid(_payoutGrid);
        _payoutGrid.Dock = DockStyle.Fill;
        _payoutGrid.Margin = new Padding(8, 3, 0, 3);
        _payoutGrid.AllowUserToAddRows = false;
        _payoutGrid.AllowUserToDeleteRows = false;
        _payoutGrid.Columns.Add(Col(PayoutCol.Time, "Time", 110, true));
        _payoutGrid.Columns.Add(Col(PayoutCol.Mission, "Mission", 140, true, DataGridViewContentAlignment.MiddleLeft));
        _payoutGrid.Columns.Add(Col(PayoutCol.Payout, "Payout", 110, false, DataGridViewContentAlignment.MiddleRight));   // the only box you can type in: what the mission really paid
        _payoutGrid.Columns.Add(Col(PayoutCol.Members, "Shared Between", 220, true, DataGridViewContentAlignment.MiddleLeft, true));
        _payoutGrid.Columns.Add(Col(PayoutCol.Each, "Each", 110, true, DataGridViewContentAlignment.MiddleRight));
        FitColumns(_payoutGrid);
        _payoutGrid.ScrollBars = ScrollBars.Vertical;
        _payoutGrid.CellEndEdit += PayoutEdited;
        _payoutGrid.DataError += (_, e) => e.ThrowException = false;
        t.Controls.Add(_payoutGrid, 1, 4);

        var note = new Label
        {
            Text = "Every mission payout is split equally between the party members who were active when it paid out (see the Party table on the front page), but everybody is owed the value he carried. Ticked payments reduce the debts. Session Party… sets one fixed party for a whole session instead.",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            Tag = "muted",
            AutoEllipsis = true,
        };
        t.Controls.Add(note, 0, 5);
        t.SetColumnSpan(note, 2);
        return t;
    }

    Control BuildHistoryPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 62));

        t.Controls.Add(Caption("All-Time Leaderboard", "Every saved session"), 0, 0);
        BuildStatColumns(_allTimeGrid, true);
        t.Controls.Add(_allTimeGrid, 0, 1);

        _bestLine.Dock = DockStyle.Fill;
        _bestLine.TextAlign = ContentAlignment.MiddleLeft;
        _bestLine.ForeColor = Color.DimGray;
        _bestLine.Tag = "muted";
        t.Controls.Add(_bestLine, 0, 2);

        t.Controls.Add(Caption("Saved Sessions", "They stay here also when their logs are gone"), 0, 3);
        StyleGrid(_historyGrid);
        _historyGrid.Dock = DockStyle.Fill;
        _historyGrid.AllowUserToAddRows = false;
        _historyGrid.AllowUserToDeleteRows = false;
        _historyGrid.Columns.Add(Col(HistoryCol.Date, "Date", 130, true));
        _historyGrid.Columns.Add(Col(HistoryCol.Time, "Time", 130, true));
        _historyGrid.Columns.Add(Col(HistoryCol.Missions, "Missions", 90, true, DataGridViewContentAlignment.MiddleRight));
        _historyGrid.Columns.Add(Col(HistoryCol.Revenue, "Revenue", 140, true, DataGridViewContentAlignment.MiddleRight));
        _historyGrid.Columns.Add(Col(HistoryCol.Carried, "Carried by Worker", 400, true, DataGridViewContentAlignment.MiddleLeft, true));
        t.Controls.Add(_historyGrid, 0, 4);
        return t;
    }

    void FillHistory()
    {
        FillAllTime();
        _historyGrid.Rows.Clear();
        foreach (var s in _leaders.HistoryRows())
        {
            _historyGrid.AddRow(
                (HistoryCol.Date, s.Date),
                (HistoryCol.Time, s.Time),
                (HistoryCol.Missions, s.Missions),
                (HistoryCol.Revenue, s.Revenue),
                (HistoryCol.Carried, s.Carried));
        }
        // best session: most revenue, and the best aUEC per hour
        _bestLine.Text = _leaders.BestLine();

        var (missions, revenue) = _leaders.HistoryTotals();
        var totalRow = _historyGrid.AddRow((HistoryCol.Date, "Total"), (HistoryCol.Time, ""), (HistoryCol.Missions, missions), (HistoryCol.Revenue, revenue.ToString("N0", CultureInfo.InvariantCulture)), (HistoryCol.Carried, ""));
        totalRow.DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
    }

    // ------------------------------------------------------------------ showing it

    static void SetItems(ComboBox box, List<string> items)
    {
        var same = box.Items.Count == items.Count;
        for (var i = 0; same && i < items.Count; i++) same = (box.Items[i] as string) == items[i];
        if (same) return;
        box.Items.Clear();
        foreach (var x in items) box.Items.Add(x);
    }

    /// <summary>Green when somebody owes the selected member (positive), red when he owes them (negative), no colour when it is even.</summary>
    static void SignColor(DataGridViewCell cell, decimal v)
    {
        if (Math.Abs(v) < 0.5m) return;
        var c = v > 0 ? DoneGreen : LostRed;
        cell.Style.BackColor = c;
        cell.Style.SelectionBackColor = c;
    }

    void RefreshDebts()
    {
        if (_debtUpdating || _balanceGrid.IsCurrentCellInEditMode || _payoutGrid.IsCurrentCellInEditMode) return;
        _debtUpdating = true;
        try
        {
            var party = _payouts.DebtPartyNames();

            SetItems(_debtMember, party);
            var idx = party.FindIndex(p => SameName(p, _debtSelected));
            if (idx < 0) idx = party.Count > 0 ? 0 : -1;
            _debtMember.SelectedIndex = idx;
            _debtSelected = idx >= 0 ? party[idx] : "";

            var nets = DebtCalc.Nets(_data.Sessions.Values, _data.Transfers);
            var pairs = DebtCalc.Pairs(_data.Sessions.Values, _data.Transfers);   // who owes whom, pair by pair

            _balanceGrid.Rows.Clear();
            if (_debtSelected.Length > 0)
            {
                foreach (var o in party)
                {
                    if (SameName(o, _debtSelected)) continue;
                    var calc = DebtCalc.Between(pairs, _debtSelected, o);
                    var shown = calc + _payouts.GetAdj(_debtSelected, o);
                    var row = _balanceGrid.AddRow((BalanceCol.Member, o), (BalanceCol.Balance, Signed(shown)), (BalanceCol.Meaning, Payouts.Meaning(_debtSelected, o, shown)));
                    row.Tag = calc;
                    SignColor(row.Cells[BalanceCol.Balance], shown);
                    SignColor(row.Cells[BalanceCol.Meaning], shown);   // the meaning has the colour of its balance
                }
            }

            _earnGrid.Rows.Clear();
            decimal sumNow = 0;
            decimal sumAll = 0;
            foreach (var p in party)
            {
                var now = _state.Inputs == null ? 0m : Payouts.CarriedBy(_state.Inputs.Carried, p);
                var all = _data.Sessions.Values.Sum(s => Payouts.CarriedBy(s.Carried, p));
                nets.TryGetValue(p, out var net);
                sumNow += now;
                sumAll += all;
                var earnRow = _earnGrid.AddRow((EarnCol.Worker, p), (EarnCol.Session, now.ToString("N0", CultureInfo.InvariantCulture)), (EarnCol.All, all.ToString("N0", CultureInfo.InvariantCulture)), (EarnCol.Net, Signed(net)));
                SignColor(earnRow.Cells[EarnCol.Net], net);
            }
            var earnTotal = _earnGrid.AddRow((EarnCol.Worker, "Total"), (EarnCol.Session, sumNow.ToString("N0", CultureInfo.InvariantCulture)), (EarnCol.All, sumAll.ToString("N0", CultureInfo.InvariantCulture)), (EarnCol.Net, ""));
            earnTotal.DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

            FillHistory();

            _txGrid.Rows.Clear();
            if (_debtSelected.Length > 0)
            {
                foreach (var t in _data.Transfers
                    .Where(x => SameName(x.From, _debtSelected) || SameName(x.To, _debtSelected))
                    .OrderByDescending(x => x.Time))
                {
                    var row = _txGrid.AddRow(
                        (TransferCol.Time, t.Time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture)),
                        (TransferCol.From, t.From),
                        (TransferCol.To, t.To),
                        (TransferCol.Amount, t.Amount.ToString("N0", CultureInfo.InvariantCulture)),
                        (TransferCol.Payment, t.Payment));
                    row.Tag = t;
                }
            }

            _payoutGrid.Rows.Clear();
            foreach (var r in _data.Payouts.OrderByDescending(x => x.Time))
            {
                var row = _payoutGrid.AddRow(
                    (PayoutCol.Time, r.Time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture)),
                    (PayoutCol.Mission, r.Contract),
                    (PayoutCol.Payout, r.Payout.ToString("N0", CultureInfo.InvariantCulture)),
                    (PayoutCol.Members, string.Join(", ", r.Members)),
                    (PayoutCol.Each, r.Each.ToString("N0", CultureInfo.InvariantCulture)));
                row.Tag = r;
                if (_data.PayoutOverrides.ContainsKey(r.MissionId))
                {
                    var typed = row.Cells[PayoutCol.Payout];
                    typed.Style.ForeColor = Theme.Gold;   // typed by you
                    typed.ToolTipText = "Typed by you. Empty the box to use the reward of the contract again.";
                }
            }
        }
        finally
        {
            _debtUpdating = false;
        }
    }

    // ------------------------------------------------------------------ data file

    bool _skipSave;

    /// <summary>Lets you keep the data file somewhere else, for example in a OneDrive folder, so it follows you between PCs.</summary>
    void ChooseDataFile()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Pick the folder for the data file, for example a folder in your OneDrive. Now in use: " + Storage.FilePath,
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var target = Path.Combine(dlg.SelectedPath, "HaulingTracker-data.json");
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(Storage.FilePath), StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("That file is already in use.");
            return;
        }

        try
        {
            if (File.Exists(target))
            {
                var answer = MessageBox.Show(this,
                    "There is already a data file in that folder (for example from another PC).\n\n" +
                    "Yes = use that file (the tracker restarts, the data on this PC stays in its old file)\n" +
                    "No = replace it with the data from this PC\n" +
                    "Cancel = do nothing",
                    "Data File", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes)
                {
                    Storage.SetLocation(target);
                    _skipSave = true;   // do not overwrite the file we are switching to
                    Application.Restart();
                    return;
                }
            }

            Storage.SetLocation(target);
            _state.Save();
            SetStatus("Data is now saved in " + target);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not use that folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ------------------------------------------------------------------ editing

    /// <summary>Who was in the party of the selected session. The reward is split equally between them.</summary>
    void EditSessionParty()
    {
        if (_state.Inputs == null || _state.Current == null)
        {
            SetStatus("Pick a session first.");
            return;
        }

        var names = _payouts.DebtPartyNames();
        var now = _state.Inputs.Party ?? _state.Inputs.Members;
        using var f = new Form
        {
            Text = "Session Party",
            ClientSize = new Size(360, 320),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var label = new Label
        {
            Text = "Everyone who was in the party this session, also someone who carried nothing. The reward is split equally between them.",
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(6, 6, 6, 0),
        };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        foreach (var n in names) list.Items.Add(n, now.Any(x => SameName(x, n)));
        var ok = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(ok);
        f.Controls.Add(list);
        f.Controls.Add(label);
        f.Controls.Add(bar);
        f.AcceptButton = ok;
        ApplyTheme(f);
        if (f.ShowDialog(this) != DialogResult.OK) return;

        _state.Inputs.Party = list.CheckedItems.Cast<object>().Select(x => x.ToString() ?? "").Where(x => x.Length > 0).ToList();
        _state.Save();
        _payouts.UpdateSnapshots();
        RefreshDebts();
    }

    void BalanceEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_debtUpdating || e.RowIndex < 0 || _balanceGrid.ColumnName(e.ColumnIndex) != BalanceCol.Balance || _debtSelected.Length == 0) return;
        var row = _balanceGrid.Rows[e.RowIndex];
        if (row.Tag is not decimal calc) return;
        var other = Convert.ToString(row.Cells[BalanceCol.Member].Value) ?? "";
        _payouts.SetAdj(_debtSelected, other, ParseSigned(row.Cells[BalanceCol.Balance].Value) - calc);
        _state.Save();
        BeginInvoke(new Action(RefreshDebts));
    }

    /// <summary>The Payout of a mission was typed: it is what the mission really paid, so the shares and the debts follow it (empty = the reward of the contract again).</summary>
    void PayoutEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_debtUpdating || e.RowIndex < 0 || _payoutGrid.ColumnName(e.ColumnIndex) != PayoutCol.Payout) return;
        var row = _payoutGrid.Rows[e.RowIndex];
        if (row.Tag is not PayoutRecord r) return;
        _payouts.SetPayoutOverride(r.MissionId, ParseNumber(row.Cells[PayoutCol.Payout].Value));
        _state.Save();
        BeginInvoke(new Action(() =>
        {
            _renderedVersion = -1;   // worked out again with the new payout
            RefreshFromLog();
            RefreshDebts();
        }));
    }

    void TxChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_debtUpdating || e.RowIndex < 0 || _txGrid.ColumnName(e.ColumnIndex) != TransferCol.Payment) return;
        var row = _txGrid.Rows[e.RowIndex];
        if (row.Tag is not SavedTransfer t) return;
        t.Payment = row.Cells[TransferCol.Payment].Value is true;
        _state.Save();
        BeginInvoke(new Action(RefreshDebts));
    }
}
