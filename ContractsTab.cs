using System.Globalization;

namespace HaulingTracker;

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
        t.Controls.Add(Caption("Contracts", "Every mission that is still going. Click a box to change who has the mission: green ✔ has it, red ✖ does not, gold marks the one who took the contract (his box can be changed too). The Trip Planner only plans a worker for missions he has."), 0, 0);

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

    /// <summary>The missions of the selected session that are still going, oldest first.</summary>
    List<Mission> ActiveMissions() =>
        _current == null ? new List<Mission>() : _current.Missions.Where(m => m.EndState == null).OrderBy(m => m.Accepted ?? m.FirstSeen ?? DateTime.MaxValue).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

    /// <summary>The id of a player the logs name only by number: the one party member without a log (when there is exactly one such id and member).</summary>
    string? PlayerIdOf(string member)
    {
        if (_log == null) return null;
        var named = _log.IdNames.FirstOrDefault(kv => SameName(kv.Value, member));
        if (named.Key != null) return named.Key;
        var unnamed = _log.MemberEvents.Select(e => e.Player).Distinct().Where(id => !_log.IdNames.ContainsKey(id)).ToList();
        var withoutLog = _data.Party.Where(n => !_log.Players.Any(p => SameName(p, n))).ToList();
        return unnamed.Count == 1 && withoutLog.Count == 1 && SameName(withoutLog[0], member) ? unnamed[0] : null;
    }

    /// <summary>What the logs say: does this party member have the mission? A member with a log has it when his log has it (not ended it); one without a log (nobody can read his game) unless the others were told that he left it.</summary>
    bool LogSaysHas(Mission m, string member, DateTime? at = null)
    {
        if (_log == null) return true;
        if (_log.Players.Any(p => SameName(p, member))) return m.Holders.Any(h => SameName(h, member));
        var id = PlayerIdOf(member);
        // nothing tells when it was shared with him: he is assumed to have it while he is in the party (click his box when he does not), and not when he is not
        var inParty = at.HasValue ? IsActiveAt(member, at.Value) : !_data.InactiveParty.Any(x => SameName(x, member));
        if (id == null) return inParty;
        var last = _log.MemberEvents.Where(e => e.Mission == m.Id && e.Player == id).OrderBy(e => e.Time).LastOrDefault();
        return last == null ? inParty : !last.Left;
    }

    /// <summary>Does this member have the mission: what was ticked on the Contracts tab, else what the logs say.</summary>
    bool HasMission(Mission m, string member, DateTime? at = null)
    {
        if (_data.MissionHolders.TryGetValue(m.Id, out var ticked))
        {
            foreach (var kv in ticked)
            {
                if (SameName(kv.Key, member)) return kv.Value;
            }
        }
        return LogSaysHas(m, member, at);
    }

    void RefreshContracts()
    {
        if (_contractsFilling || _contractGrid.IsCurrentCellInEditMode) return;
        var missions = ActiveMissions();
        var members = _data.Party.ToList();
        var sig = Theme.Dark + "¦" + string.Join("|", members) + "¦" + string.Join("¦", missions.Select(m =>
            m.Id + "|" + m.Multi + "|" + m.Cargo + "|" + m.Taker + "|" + string.Join(",", members.Select(n => HasMission(m, n) ? "1" : "0"))));
        if (sig == _contractsShown && _contractGrid.Columns.Count > 0) return;
        _contractsShown = sig;

        _contractsFilling = true;
        try
        {
            var firstShown = _contractGrid.FirstDisplayedScrollingRowIndex;
            _contractGrid.Rows.Clear();
            _contractGrid.Columns.Clear();
            _contractGrid.Columns.Add(Col("Contract", 130, true));
            _contractGrid.Columns.Add(Col("Cargo", 130, true));
            foreach (var n in members)
            {
                var c = Col(n, 130, true);
                c.Name = "m:" + n;
                _contractGrid.Columns.Add(c);
            }
            AddNumberColumn(_contractGrid);
            foreach (DataGridViewColumn c in _contractGrid.Columns)
            {
                if (c.Name == "num") continue;
                c.FillWeight = c.Width;
                c.MinimumWidth = 60;
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }

            foreach (var m in missions)
            {
                var cells = new List<object?> { m.Multi > 0 ? m.Multi + " stops" : "?", string.IsNullOrEmpty(m.Cargo) ? "" : m.Cargo };
                cells.AddRange(members.Select(_ => (object?)""));
                cells.Add("");   // the # column
                var row = _contractGrid.Rows[_contractGrid.Rows.Add(cells.ToArray())];
                row.Tag = m;
                for (var k = 0; k < members.Count; k++) ShowHas(row.Cells[2 + k], m, members[k], HasMission(m, members[k]));
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
        var col = _contractGrid.Columns[e.ColumnIndex];
        if (!col.Name.StartsWith("m:") || _contractGrid.Rows[e.RowIndex].Tag is not Mission m) return;
        var member = col.Name[2..];
        var on = !HasMission(m, member);
        if (!_data.MissionHolders.TryGetValue(m.Id, out var ticked)) _data.MissionHolders[m.Id] = ticked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (on == LogSaysHas(m, member)) ticked.Remove(member);
        else ticked[member] = on;
        if (ticked.Count == 0) _data.MissionHolders.Remove(m.Id);
        Storage.Save(_data);
        ShowHas(_contractGrid.Rows[e.RowIndex].Cells[e.ColumnIndex], m, member, on);
        _contractsShown = "";   // the plan and the payouts use it
    }
}
