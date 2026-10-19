using System.Globalization;
using System.Text;

namespace HaulingTracker;

public sealed partial class MainForm : Form
{
    static readonly Color HeaderBlue = Color.FromArgb(54, 92, 138);
    static readonly Color CalcBlue = Color.FromArgb(243, 248, 253);
    const decimal DefaultReward2 = 1268250m;   // payout of a 2 stop mission
    const decimal DefaultReward3 = 1691000m;   // payout of a 3 stop mission
    static readonly Color LostRed = Color.FromArgb(253, 224, 224);
    static readonly Color DoneGreen = Color.FromArgb(226, 244, 226);
    static readonly Color PartialYellow = Color.FromArgb(255, 244, 204);

    const string DefaultLogPath = @"C:\Program Files\Roberts Space Industries\StarCitizen";

    readonly List<HaulingLog> _logs = new();
    HaulingLog? _log;
    readonly Button _addBtn = new();
    readonly DataGridView _tripGrid = new();
    List<WorkerTrip> _trips = new();
    readonly AppData _data = Storage.Load();
    List<SessionGroup> _sessions = new();
    SessionGroup? _current;
    SessionInputs? _inputs;
    SessionSummary? _summary;
    List<LocationRow> _rows = new();
    decimal _revenue;
    int _renderedVersion = -1;
    bool _updating;

    // live watching of the running game's Game.log
    bool _live;

    readonly ComboBox _sessionBox = new();
    readonly Button _openBtn = new();
    readonly Button _liveBtn = new();
    readonly Button _resetBtn = new();
    readonly Button _copyBtn = new();
    readonly Label _status = new();

    readonly TextBox _startBox = new();
    readonly TextBox _endBox = new();
    readonly Label _durationVal = new();
    readonly Label _missionsVal = new();
    readonly Label _revenueVal = new();
    readonly Label _hourlyVal = new();

    readonly DataGridView _rewards = new();
    readonly Label _rewardsTotalMissions = new();
    readonly Label _rewardsTotalRevenue = new();

    readonly DataGridView _logGrid = new();

    readonly System.Windows.Forms.Timer _timer = new();

    public MainForm()
    {
        Text = "Hauling Session Tracker";
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 780);
        MinimumSize = new Size(1000, 640);
        BackColor = Color.White;

        BuildUi();

        _timer.Interval = 1500;
        _timer.Tick += (_, _) => PollLive();
        FormClosing += (_, _) => Storage.Save(_data);

        ClearView();
        SetStatus("Open a log file, or press \"Watch live Game.log\" while you play.");
    }

    // ------------------------------------------------------------------ layout

    void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 262));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var sessionPage = new TabPage("Session") { BackColor = Color.White };
        var debtsPage = new TabPage("Debts") { BackColor = Color.White };
        tabs.TabPages.Add(sessionPage);
        tabs.TabPages.Add(debtsPage);
        Controls.Add(tabs);
        sessionPage.Controls.Add(root);
        debtsPage.Controls.Add(BuildDebtsPage());
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedIndex == 1) RefreshDebts();
        };

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildHeader(), 0, 1);
        BuildLogGrid();
        root.Controls.Add(_logGrid, 0, 2);
        BuildTripGrid();
        root.Controls.Add(_tripGrid, 0, 3);
        root.Controls.Add(BuildFooter(), 0, 4);
    }

    Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
        };

        StyleButton(_openBtn, "Open log file(s)…");
        _openBtn.Click += (_, _) => OpenLogs();

        StyleButton(_liveBtn, "Watch live Game.log");
        _liveBtn.Click += (_, _) => ToggleLive();

        StyleButton(_addBtn, "Add live source…");
        _addBtn.Click += (_, _) => AddSource();

        StyleButton(_partyBtn, "Party…");
        _partyBtn.Click += (_, _) => EditParty();

        StyleButton(_sourcesBtn, "Sources…");
        _sourcesBtn.Click += (_, _) => ShowSources();

        _channelBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _channelBox.Width = 90;
        _channelBox.Margin = new Padding(0, 6, 10, 4);
        _channelBox.Items.AddRange(new object[] { "Auto", "LIVE", "HOTFIX", "PTU", "EPTU" });
        _channelBox.SelectedItem = _data.Channel is { Length: > 0 } ch && _channelBox.Items.Contains(ch) ? ch : "Auto";
        _channelBox.SelectedIndexChanged += ChannelChanged;


        _sessionBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sessionBox.Width = 280;
        _sessionBox.Margin = new Padding(0, 6, 10, 4);
        _sessionBox.SelectedIndexChanged += SessionChanged;

        StyleButton(_resetBtn, "Use log times");
        _resetBtn.Click += (_, _) => ResetTimes();

        StyleButton(_copyBtn, "Copy table for Google Sheets");
        _copyBtn.Click += (_, _) => CopyTable();

        _status.AutoSize = true;
        _status.ForeColor = Color.DimGray;
        _status.Margin = new Padding(6, 10, 0, 0);

        bar.Controls.AddRange(new Control[] { _openBtn, _liveBtn, _addBtn, _sourcesBtn, _partyBtn, _channelBox, _sessionBox, _resetBtn, _copyBtn, _status });
        return bar;
    }

    static void StyleButton(Button b, string text)
    {
        b.Text = text;
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.Height = 32;
        b.Margin = new Padding(0, 4, 10, 4);
        b.Padding = new Padding(8, 0, 8, 0);
    }

    Control BuildHeader()
    {
        var p = new Panel { Dock = DockStyle.Fill };

        p.Controls.Add(new Label
        {
            Text = "Star Citizen Hauling Session",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(4, 6),
        });

        // ---- left block: start / end / duration / totals
        var t = new TableLayoutPanel
        {
            Location = new Point(4, 56),
            Size = new Size(348, 190),
            ColumnCount = 2,
            RowCount = 7,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192));
        for (var i = 0; i < 7; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        ConfigureTimeBox(_startBox);
        ConfigureTimeBox(_endBox);
        StyleValue(_durationVal);
        StyleValue(_missionsVal);
        StyleValue(_revenueVal);
        StyleValue(_hourlyVal);

        AddSummaryRow(t, 0, "Start Time", WrapInput(_startBox));
        AddSummaryRow(t, 1, "End Time", WrapInput(_endBox));
        AddSummaryRow(t, 2, "Duration", _durationVal);
        AddSummaryRow(t, 3, "", null);
        AddSummaryRow(t, 4, "Total Missions", _missionsVal);
        AddSummaryRow(t, 5, "Total Revenue", _revenueVal);
        AddSummaryRow(t, 6, "Hourly Rate", _hourlyVal);
        p.Controls.Add(t);

        // ---- right block: reward table
        _rewards.Location = new Point(400, 56);
        _rewards.Size = new Size(572, 90);
        StyleGrid(_rewards);
        _rewards.AllowUserToAddRows = false;
        _rewards.AllowUserToDeleteRows = false;
        _rewards.ScrollBars = ScrollBars.None;
        _rewards.Columns.Add(Col("Contract", 100, true, DataGridViewContentAlignment.MiddleLeft));
        _rewards.Columns.Add(Col("Mission Reward", 120, false, DataGridViewContentAlignment.MiddleRight));
        _rewards.Columns.Add(Col("# Missions", 110, true, DataGridViewContentAlignment.MiddleRight));
        _rewards.Columns.Add(Col("Total", 242, true, DataGridViewContentAlignment.MiddleRight));
        _rewards.CellEndEdit += (_, _) =>
        {
            if (_inputs != null && _rewards.Rows.Count >= 2)
            {
                _inputs.Reward2 = ParseNumber(_rewards.Rows[0].Cells[1].Value);
                _inputs.Reward3 = ParseNumber(_rewards.Rows[1].Cells[1].Value);
            }
            RecomputeRewards();
            Storage.Save(_data);
            UpdateSnapshots();
            RefreshDebts();
        };
        p.Controls.Add(_rewards);

        var totals = new TableLayoutPanel
        {
            Location = new Point(400, 150),
            Size = new Size(572, 28),
            ColumnCount = 3,
            RowCount = 1,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
        };
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        totals.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        totals.Controls.Add(new Label
        {
            Text = "Total",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        }, 0, 0);
        StyleValue(_rewardsTotalMissions);
        StyleValue(_rewardsTotalRevenue);
        _rewardsTotalMissions.BackColor = Color.White;
        _rewardsTotalRevenue.BackColor = Color.White;
        totals.Controls.Add(_rewardsTotalMissions, 1, 0);
        totals.Controls.Add(_rewardsTotalRevenue, 2, 0);
        p.Controls.Add(totals);

        return p;
    }

    static void ConfigureTimeBox(TextBox tb)
    {
        tb.BorderStyle = BorderStyle.None;
        tb.TextAlign = HorizontalAlignment.Right;
        tb.MaxLength = 5;
        tb.PlaceholderText = "00:00";
        tb.Dock = DockStyle.Fill;
    }

    static Control WrapInput(TextBox tb)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(0, 3, 6, 0), Margin = new Padding(0) };
        p.Controls.Add(tb);
        return p;
    }

    static void StyleValue(Label l)
    {
        l.Dock = DockStyle.Fill;
        l.TextAlign = ContentAlignment.MiddleRight;
        l.BackColor = CalcBlue;
        l.Margin = new Padding(0);
        l.Padding = new Padding(0, 0, 6, 0);
    }

    static void AddSummaryRow(TableLayoutPanel t, int row, string label, Control? value)
    {
        t.Controls.Add(new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        }, 0, row);

        if (value != null)
        {
            value.Margin = new Padding(0);
            t.Controls.Add(value, 1, row);
        }
    }

    void BuildLogGrid()
    {
        _logGrid.Dock = DockStyle.Fill;
        StyleGrid(_logGrid);
        _logGrid.AllowUserToAddRows = false;
        _logGrid.AllowUserToDeleteRows = false;

        // Columns you type in are white. Columns filled in from the log are tinted.
        _logGrid.Columns.Add(Col("Pickup Location", 210, false, DataGridViewContentAlignment.MiddleLeft));
        _logGrid.Columns.Add(Col("Missions", 110, true));
        _logGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Partial pickup",
            Width = 110,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        var workerCol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Assigned Worker",
            Width = 180,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        workerCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _logGrid.Columns.Add(workerCol);
        _logGrid.Columns.Add(Col("Value", 120, true, DataGridViewContentAlignment.MiddleRight));
        _logGrid.Columns.Add(Col("Notes", 120, false, DataGridViewContentAlignment.MiddleLeft, true));
        _logGrid.CellEndEdit += LogGrid_CellEndEdit;
        _logGrid.DataError += (_, e) => e.ThrowException = false;
        _logGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_logGrid.IsCurrentCellDirty) _logGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _logGrid.CellValueChanged += LogGrid_CellValueChanged;
    }

    void BuildTripGrid()
    {
        _tripGrid.Dock = DockStyle.Fill;
        StyleGrid(_tripGrid);
        _tripGrid.AllowUserToAddRows = false;
        _tripGrid.AllowUserToDeleteRows = false;
        _tripGrid.Columns.Add(Col("Worker", 120, true));
        _tripGrid.Columns.Add(Col("Run", 60, true));
        _tripGrid.Columns.Add(Col("Started", 80, true));
        _tripGrid.Columns.Add(Col("Pickups (location, missions, time)", 420, true, DataGridViewContentAlignment.MiddleLeft, true));
        _tripGrid.Columns.Add(Col("Handed in", 90, true));
        _tripGrid.Columns.Add(Col("Missions", 80, true));
        _tripGrid.Columns.Add(Col("Duration", 90, true));
        _tripGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Lost cargo",
            Width = 90,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        _tripGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_tripGrid.IsCurrentCellDirty) _tripGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _tripGrid.CellValueChanged += (_, e) =>
        {
            if (_updating || _inputs == null || e.RowIndex < 0 || e.ColumnIndex != 7) return;
            var row = _tripGrid.Rows[e.RowIndex];
            if (row.Tag is not string key) return;
            var lost = row.Cells[7].Value is true;
            _inputs.LostRuns.Remove(key);
            if (lost) _inputs.LostRuns.Add(key);
            row.DefaultCellStyle.BackColor = lost ? LostRed : Color.White;
            Storage.Save(_data);
        };
    }

    string WorkerOfZone(string zone)
    {
        if (_inputs != null && _inputs.Rows.TryGetValue(zone, out var r) && !string.IsNullOrWhiteSpace(r.Worker))
            return r.Worker;
        return "Unassigned";
    }

    void FillTrips()
    {
        var wasUpdating = _updating;
        _updating = true;
        try
        {
            _tripGrid.Rows.Clear();
            _trips = new List<WorkerTrip>();
            if (_current == null || _inputs == null || _log == null) return;

            _trips = WorkerTripBuilder.Build(_current, WorkerOfZone, _log)
                .OrderBy(t => t.Start).ThenBy(t => t.Worker, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var t in _trips)
            {
                var pickups = string.Join(",  ", t.Pickups.Select(p => LocationName(p.Zone) + " " + p.Missions + " (" + FormatTime(p.Time) + ")"));
                var dur = t.End.HasValue ? (t.End.Value - t.Start) : TimeSpan.Zero;
                var lost = _inputs.LostRuns.Contains(t.Key);
                var idx = _tripGrid.Rows.Add(
                    t.Worker,
                    "#" + t.Number,
                    FormatTime(t.Start),
                    pickups,
                    t.End.HasValue ? FormatTime(t.End.Value) : "on the way",
                    t.End.HasValue ? t.HandedIn.ToString() : "",
                    t.End.HasValue ? (int)dur.TotalHours + ":" + dur.Minutes.ToString("00") : "",
                    lost);
                var row = _tripGrid.Rows[idx];
                row.Tag = t.Key;
                if (lost) row.DefaultCellStyle.BackColor = LostRed;
                if (!t.End.HasValue) row.Cells[4].Style.BackColor = PartialYellow;
            }
        }
        finally
        {
            _updating = wasUpdating;
        }
    }

    Control BuildFooter()
    {
        var f = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        f.RowStyles.Add(new RowStyle(SizeType.Percent, 100));


        f.Controls.Add(new Label
        {
            Text = "Rename a location once and it is remembered. \"Partially\" means some missions still have cargo waiting there. SCU is typed by you.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            AutoEllipsis = true,
        }, 1, 0);
        return f;
    }

    static DataGridViewTextBoxColumn Col(
        string header, int width, bool readOnly,
        DataGridViewContentAlignment align = DataGridViewContentAlignment.MiddleCenter,
        bool fill = false)
    {
        var c = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            Width = width,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        c.DefaultCellStyle.Alignment = align;
        if (readOnly) c.DefaultCellStyle.BackColor = CalcBlue;
        if (fill) c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        return c;
    }

    static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Color.White;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        g.GridColor = Color.Black;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersDefaultCellStyle.BackColor = HeaderBlue;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBlue;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 28;
        g.RowHeadersVisible = false;
        g.RowTemplate.Height = 26;
        g.AllowUserToResizeRows = false;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(200, 220, 245);
        g.DefaultCellStyle.SelectionForeColor = Color.Black;
    }

    // ------------------------------------------------------------------ loading logs

    sealed class LiveSource
    {
        public string Path = "";
        public long Pos;
        public Decoder Dec = Encoding.UTF8.GetDecoder();
        public string Carry = "";
        public HaulingLog Log = new();
        public string? Current;     // the Game.log file that is being read right now
    }

    readonly ComboBox _channelBox = new();
    readonly Button _sourcesBtn = new();
    readonly Button _partyBtn = new();

    /// <summary>A watch path is a Game.log file, a folder with Game.log, or the StarCitizen folder with LIVE, HOTFIX... inside.</summary>
    string? ResolveLog(string path)
    {
        try
        {
            if (File.Exists(path)) return path;
            if (!Directory.Exists(path)) return null;

            var direct = Path.Combine(path, "Game.log");
            if (File.Exists(direct)) return direct;

            var found = Directory.GetDirectories(path)
                .Select(d => (Name: Path.GetFileName(d), File: Path.Combine(d, "Game.log")))
                .Where(x => File.Exists(x.File))
                .ToList();

            var channel = _data.Channel;
            if (!string.IsNullOrEmpty(channel) && channel != "Auto")
            {
                var pick = found.FirstOrDefault(x => string.Equals(x.Name, channel, StringComparison.OrdinalIgnoreCase));
                return pick.File;   // null when that channel has no log
            }
            return found.OrderByDescending(x => File.GetLastWriteTimeUtc(x.File)).Select(x => x.File).FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    void ChannelChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        _data.Channel = _channelBox.SelectedItem as string ?? "Auto";
        Storage.Save(_data);
        if (_live) PollLive();
    }

    readonly List<LiveSource> _sources = new();

    void OpenLogs()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose one or more Game.log files (from any of your computers)",
            Filter = "Star Citizen logs (*.log)|*.log|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        StopLive();
        try
        {
            Cursor = Cursors.WaitCursor;
            var files = dlg.FileNames
                .OrderBy(f => HaulingLog.FirstTimestamp(f) ?? DateTime.MaxValue)
                .ToList();

            // one log per player, so logs from two accounts stay apart
            _logs.Clear();
            var byPlayer = new Dictionary<string, HaulingLog>();
            foreach (var f in files)
            {
                var player = HaulingLog.FirstPlayer(f) ?? "Unknown player";
                if (!byPlayer.TryGetValue(player, out var log))
                {
                    log = new HaulingLog();
                    byPlayer[player] = log;
                    _logs.Add(log);
                }
                log.ProcessFile(f);
            }

            _renderedVersion = -1;
            _current = null;
            _log = null;
            RefreshFromLog();
            SetStatus($"Loaded {files.Count} log file(s) for {_logs.Count} player(s), {_sessions.Count} session(s) found.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not read the log", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>Add a Game.log to watch live. Can be a network path to another computer, like the Gauntlet Tracker.</summary>
    void AddSource()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Pick the StarCitizen folder (the one that holds LIVE and HOTFIX). It can be on another computer, for example \\\\PCNAME\\StarCitizen.",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (!_data.WatchPaths.Contains(dlg.SelectedPath)) _data.WatchPaths.Add(dlg.SelectedPath);
        Storage.Save(_data);
        SetStatus("Added " + dlg.SelectedPath);
        if (_live) StartLive();
    }

    /// <summary>Shows every added live source, whether it can be reached right now, and lets you remove one.</summary>
    void ShowSources()
    {
        using var f = new Form
        {
            Text = "Live sources",
            ClientSize = new Size(760, 320),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var remove = new Button { Text = "Remove selected", AutoSize = true };
        bar.Controls.Add(close);
        bar.Controls.Add(remove);
        f.Controls.Add(list);
        f.Controls.Add(bar);
        f.AcceptButton = close;

        var changed = false;
        void Fill()
        {
            list.Items.Clear();
            if (_data.WatchPaths.Count == 0) list.Items.Add("(no live sources added yet, use \"Add live source…\")");
            foreach (var path in _data.WatchPaths)
            {
                var file = ResolveLog(path);
                list.Items.Add(path + "    →    " + (file ?? "cannot be reached right now"));
            }
        }
        remove.Click += (_, _) =>
        {
            var i = list.SelectedIndex;
            if (i < 0 || i >= _data.WatchPaths.Count) return;
            _data.WatchPaths.RemoveAt(i);
            Storage.Save(_data);
            changed = true;
            Fill();
        };
        Fill();
        f.ShowDialog(this);

        if (changed && _live) StartLive();
    }

    void ToggleLive()
    {
        if (_live)
        {
            StopLive();
            SetStatus("Stopped watching.");
            return;
        }

        if (_data.WatchPaths.Count == 0)
        {
            if (Directory.Exists(DefaultLogPath)) _data.WatchPaths.Add(DefaultLogPath);
            else
            {
                AddSource();
                if (_data.WatchPaths.Count == 0) return;
            }
        }
        StartLive();
    }

    void StartLive()
    {
        _sources.Clear();
        _logs.Clear();
        foreach (var path in _data.WatchPaths)
        {
            var src = new LiveSource { Path = path };
            _sources.Add(src);
            _logs.Add(src.Log);
        }
        _renderedVersion = -1;
        _current = null;
        _log = null;
        _live = true;
        _liveBtn.Text = "Stop watching";
        _timer.Start();
        PollLive();
        SetStatus("Watching " + string.Join(", ", _data.WatchPaths));
    }

    void StopLive()
    {
        _timer.Stop();
        _live = false;
        _liveBtn.Text = "Watch live Game.log";
    }

    void PollLive()
    {
        if (!_live) return;
        foreach (var src in _sources)
        {
            try
            {
                var file = ResolveLog(src.Path);
                if (file == null) continue;
                if (file != src.Current)
                {
                    // after a restart of this program or of the game: read the recent backup logs first,
                    // so a hauling session that was already going on is not lost
                    Backfill(src, file);

                    // switched channel (LIVE / HOTFIX) or a new location: read the new file from the top
                    src.Current = file;
                    src.Pos = 0;
                    src.Carry = "";
                    src.Dec = Encoding.UTF8.GetDecoder();
                }
                var info = new FileInfo(file);
                if (!info.Exists) continue;

                // A new Game.log starts empty. Keep the missions we already know and read it from the top.
                if (info.Length < src.Pos)
                {
                    src.Pos = 0;
                    src.Carry = "";
                    src.Dec = Encoding.UTF8.GetDecoder();
                }
                if (info.Length <= src.Pos) continue;

                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(src.Pos, SeekOrigin.Begin);
                var buffer = new byte[1 << 16];
                var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    src.Pos += read;
                    var n = src.Dec.GetChars(buffer, 0, read, chars, 0);
                    Feed(src, new string(chars, 0, n));
                }
            }
            catch (IOException)
            {
                // The game may be writing at the same moment, or the other computer is offline. Try again next tick.
            }
        }
        RefreshFromLog();
    }

    static readonly TimeSpan BackupAge = TimeSpan.FromHours(6);

    /// <summary>Reads the logs in the logbackups folder next to Game.log that are less than 6 hours old.</summary>
    static void Backfill(LiveSource src, string liveFile)
    {
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(liveFile) ?? "", "logbackups");
            if (!Directory.Exists(dir)) return;

            var cutoff = DateTime.UtcNow - BackupAge;
            var files = Directory.GetFiles(dir, "*.log")
                .Where(f => File.GetLastWriteTimeUtc(f) >= cutoff)
                .OrderBy(f => HaulingLog.FirstTimestamp(f) ?? DateTime.MaxValue)
                .ToList();

            foreach (var f in files)
            {
                try { src.Log.ProcessFile(f); }
                catch (IOException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void Feed(LiveSource src, string text)
    {
        var data = src.Carry + text;
        var start = 0;
        int nl;
        while ((nl = data.IndexOf('\n', start)) >= 0)
        {
            src.Log.ProcessLine(data.Substring(start, nl - start).TrimEnd('\r'));
            start = nl + 1;
        }
        src.Carry = data.Substring(start);
    }

    // ------------------------------------------------------------------ showing a session

    bool IsEditing() =>
        _logGrid.IsCurrentCellInEditMode || _rewards.IsCurrentCellInEditMode || _balanceGrid.IsCurrentCellInEditMode || _startBox.Focused || _endBox.Focused;

    void RefreshFromLog()
    {
        var version = _logs.Sum(l => l.Version) + _logs.Count * 1000003;
        if (version == _renderedVersion) return;
        if (IsEditing()) return;   // do not disturb typing, we try again on the next tick
        _renderedVersion = version;

        if (_logs.Count == 0) { ClearView(); return; }

        // the logs of all accounts are merged, a mission shared in the party appears once
        _log = HaulingLog.Merge(_logs);

        var keep = _current?.Key;
        _sessions = SessionBuilder.Build(_log);

        _updating = true;
        try
        {
            _sessionBox.Items.Clear();
            foreach (var g in _sessions) _sessionBox.Items.Add(DescribeSession(g));

            var idx = keep == null ? -1 : _sessions.FindIndex(g => g.Key == keep);
            if (idx < 0) idx = _sessions.Count - 1;
            if (idx >= 0) _sessionBox.SelectedIndex = idx;
        }
        finally
        {
            _updating = false;
        }

        _current = _sessionBox.SelectedIndex >= 0 ? _sessions[_sessionBox.SelectedIndex] : null;
        if (_current == null)
        {
            ClearView();
            UpdateSnapshots();
            RefreshDebts();
            if (!_live) SetStatus("No Station to Ruin hauling missions were found in this log.");
            return;
        }
        ShowSession();
        UpdateSnapshots();
        RefreshDebts();
    }

    static string DescribeSession(SessionGroup g)
    {
        var s = SessionBuilder.Summarize(g);
        var start = s.Start.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
        var end = s.End.HasValue ? s.End.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) : "ongoing";
        return start + " to " + end + "  (" + g.Missions.Count + " missions)";
    }

    void SessionChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        var i = _sessionBox.SelectedIndex;
        if (i < 0 || i >= _sessions.Count) return;
        _current = _sessions[i];
        ShowSession();
    }

    SessionInputs GetInputs(string key)
    {
        if (!_data.Sessions.TryGetValue(key, out var inputs))
        {
            inputs = new SessionInputs();
            _data.Sessions[key] = inputs;
        }
        return inputs;
    }

    RowInputs GetRow(string zone)
    {
        var rows = _inputs!.Rows;
        if (!rows.TryGetValue(zone, out var r))
        {
            r = new RowInputs();
            rows[zone] = r;
        }
        return r;
    }

    static string FormatTime(DateTime utc) =>
        utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    static string Tail(string zone) => zone.Length > 5 ? zone.Substring(zone.Length - 5) : zone;

    // Names found so far, keyed by the last digits of the location id. Your own names in the grid win.
    static readonly Dictionary<string, string> KnownNames = new()
    {
        ["73791"] = "Rat's Nest",
        ["70269"] = "Megumi Refueling",
        ["70120"] = "Patch City",
        ["69936"] = "Gaslight",
        ["72785"] = "Dudley & Daughters",
    };

    string LocationName(string zone)
    {
        if (_data.LocationNames.TryGetValue(zone, out var n) && !string.IsNullOrWhiteSpace(n)) return n;
        if (KnownNames.TryGetValue(Tail(zone), out var known)) return known;
        return "Unknown ..." + Tail(zone);
    }

    void ShowSession()
    {
        if (_current == null)
        {
            ClearView();
            return;
        }

        _updating = true;
        try
        {
            _inputs = GetInputs(_current.Key);
            _summary = SessionBuilder.Summarize(_current);
            _rows = SessionBuilder.Locations(_current);

            _startBox.Text = _inputs.Start ?? FormatTime(_summary.Start);
            _endBox.Text = _inputs.End ?? (_summary.End.HasValue ? FormatTime(_summary.End.Value) : "");

            // the mission counts come from the log (2 stop and 3 stop contracts), you only type the payouts
            _rewards.Rows.Clear();
            if (_inputs.Reward2 == 0) _inputs.Reward2 = DefaultReward2;
            if (_inputs.Reward3 == 0) _inputs.Reward3 = DefaultReward3;
            var accepted = _current.Missions;   // every accepted mission of the session (abandoned ones are already left out)
            _rewards.Rows.Add("2 stops", _inputs.Reward2.ToString("N0"), accepted.Count(m => m.Multi == 2), "");
            _rewards.Rows.Add("3 stops", _inputs.Reward3.ToString("N0"), accepted.Count(m => m.Multi == 3), "");

            FillLog();
            FillTrips();
        }
        finally
        {
            _updating = false;
        }

        RecomputeRewards();
    }

    void ClearView()
    {
        _updating = true;
        try
        {
            _inputs = null;
            _summary = null;
            _rows = new List<LocationRow>();
            _startBox.Text = "";
            _endBox.Text = "";
            _rewards.Rows.Clear();
            _logGrid.Rows.Clear();
            _tripGrid.Rows.Clear();
        }
        finally
        {
            _updating = false;
        }
        _revenue = 0;
        _durationVal.Text = "";
        _missionsVal.Text = "";
        _revenueVal.Text = "";
        _hourlyVal.Text = "";
        _rewardsTotalMissions.Text = "";
        _rewardsTotalRevenue.Text = "";
    }

    /// <summary>Missions still waiting at this location / missions that have cargo there. 5 missions start as 5/5.</summary>
    static string MissionsText(LocationRow r) => (r.Missions - r.Loaded) + "/" + r.Missions;

    static string DeliveredText(LocationRow r)
    {
        if (r.DeliveredTime.HasValue) return FormatTime(r.DeliveredTime.Value);
        if (r.Delivered > 0) return "Partial " + r.Delivered + "/" + r.Missions;
        return "";
    }

    void RefreshWorkerChoices()
    {
        var col = (DataGridViewComboBoxColumn)_logGrid.Columns[3];
        col.Items.Clear();
        col.Items.Add("");
        foreach (var name in _data.Party) col.Items.Add(name);
    }

    void FillLog()
    {
        _logGrid.Rows.Clear();

        // a location nobody is assigned to gets the player the logs show picking up there (you can change it afterwards)
        var assignedNow = false;
        if (_log != null && _current != null)
        {
            foreach (var r in _rows)
            {
                var input = GetRow(r.Zone);
                if (!string.IsNullOrWhiteSpace(input.Worker)) continue;
                var who = WorkerTripBuilder.PickupActor(_current, r.Zone, _log);
                if (who == null) continue;
                input.Worker = who;
                assignedNow = true;
            }
        }
        if (assignedNow) Storage.Save(_data);

        // a worker that is not in the party yet (typed earlier) is added to it
        foreach (var r in _rows)
        {
            if (_inputs!.Rows.TryGetValue(r.Zone, out var w) && !string.IsNullOrWhiteSpace(w.Worker)
                && !_data.Party.Contains(w.Worker))
                _data.Party.Add(w.Worker);
        }
        RefreshWorkerChoices();

        foreach (var r in _rows)
        {
            _inputs!.Rows.TryGetValue(r.Zone, out var inp);
            var idx = _logGrid.Rows.Add(
                LocationName(r.Zone),
                MissionsText(r),
                inp?.PartialPickup ?? false,
                inp?.Worker ?? "",
                StopValueText(r),
                inp?.Notes ?? "");

            var row = _logGrid.Rows[idx];
            row.Tag = r.Zone;
            ColorMissionsCell(row, r);
        }
    }

    /// <summary>
    /// Value of a pickup location: every mission pays its reward split over its stops,
    /// so a stop of a 2 stop mission is worth reward / 2 and a stop of a 3 stop mission reward / 3.
    /// </summary>
    decimal StopValue(LocationRow r)
    {
        if (_inputs == null) return 0;
        return r.Stops2 * _inputs.Reward2 / 2m + r.Stops3 * _inputs.Reward3 / 3m;
    }

    string StopValueText(LocationRow r)
    {
        var v = StopValue(r);
        return v == 0 ? "" : v.ToString("N0");
    }

    void RefreshStopValues()
    {
        foreach (DataGridViewRow row in _logGrid.Rows)
        {
            if (row.Tag is not string zone) continue;
            var info = _rows.FirstOrDefault(x => x.Zone == zone);
            if (info != null) row.Cells[4].Value = StopValueText(info);
        }
    }

    void ColorMissionsCell(DataGridViewRow row, LocationRow r)
    {
        var partial = row.Cells[2].Value is true;
        row.Cells[1].Style.BackColor = partial ? PartialYellow : r.Loaded >= r.Missions ? DoneGreen : CalcBlue;
    }

    void LogGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updating || _inputs == null || e.RowIndex < 0) return;
        var row = _logGrid.Rows[e.RowIndex];
        if (row.Tag is not string zone) return;

        if (e.ColumnIndex == 2)
        {
            GetRow(zone).PartialPickup = row.Cells[2].Value is true;
            var info = _rows.FirstOrDefault(x => x.Zone == zone);
            if (info != null) ColorMissionsCell(row, info);
            Storage.Save(_data);
        }
        else if (e.ColumnIndex == 3)
        {
            GetRow(zone).Worker = (Convert.ToString(row.Cells[3].Value) ?? "").Trim();
            Storage.Save(_data);
            FillTrips();   // the run board follows the assigned workers
            UpdateSnapshots();
            RefreshDebts();
        }
    }

    void EditParty()
    {
        using var f = new Form
        {
            Text = "Party",
            ClientSize = new Size(360, 300),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var label = new Label { Text = "One name per line. These are the choices in Assigned Worker.", Dock = DockStyle.Top, Height = 30, Padding = new Padding(6, 6, 6, 0) };
        var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Text = string.Join(Environment.NewLine, _data.Party) };
        var ok = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(ok);
        f.Controls.Add(box);
        f.Controls.Add(label);
        f.Controls.Add(bar);
        f.AcceptButton = ok;
        if (f.ShowDialog(this) != DialogResult.OK) return;

        _data.Party = box.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Storage.Save(_data);

        _renderedVersion = -1;
        RefreshFromLog();
    }

    // ------------------------------------------------------------------ editing

    void LogGrid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updating || _inputs == null || e.RowIndex < 0) return;
        var row = _logGrid.Rows[e.RowIndex];
        if (row.Tag is not string zone) return;
        var text = (Convert.ToString(row.Cells[e.ColumnIndex].Value) ?? "").Trim();

        switch (e.ColumnIndex)
        {
            case 0:
                if (text.Length == 0 || text.StartsWith("Unknown ..."))
                {
                    _data.LocationNames.Remove(zone);
                    row.Cells[0].Value = LocationName(zone);
                }
                else
                {
                    _data.LocationNames[zone] = text;
                }
                break;
            case 5:
                GetRow(zone).Notes = text;
                break;
        }
        Storage.Save(_data);
    }

    void RecomputeRewards()
    {
        if (_inputs == null) return;

        decimal revenue = 0;
        var missions = 0;

        var wasUpdating = _updating;
        _updating = true;
        try
        {
            foreach (DataGridViewRow row in _rewards.Rows)
            {
                var reward = ParseNumber(row.Cells[1].Value);
                var count = (int)ParseNumber(row.Cells[2].Value);
                var total = reward * count;
                row.Cells[3].Value = total == 0 ? "" : total.ToString("N0");
                revenue += total;
                missions += count;
            }
        }
        finally
        {
            _updating = wasUpdating;
        }

        _revenue = revenue;
        _rewardsTotalMissions.Text = missions.ToString("N0");
        _rewardsTotalRevenue.Text = revenue.ToString("N0");
        RefreshStopValues();
        RecomputeTimes();
    }

    void TimeBoxChanged(object? sender, EventArgs e)
    {
        if (_updating || _inputs == null) return;
        var start = _startBox.Text.Trim();
        var end = _endBox.Text.Trim();
        _inputs.Start = start.Length == 0 ? null : start;
        _inputs.End = end.Length == 0 ? null : end;
        RecomputeTimes();
    }

    void ResetTimes()
    {
        if (_inputs == null) return;
        _inputs.Start = null;
        _inputs.End = null;
        Storage.Save(_data);
        ShowSession();
    }

    void RecomputeTimes()
    {
        TimeSpan? duration = null;
        if (TryTime(_startBox.Text, out var s) && TryTime(_endBox.Text, out var e))
        {
            var d = e - s;
            if (d < TimeSpan.Zero) d += TimeSpan.FromDays(1);
            duration = d;
        }

        _durationVal.Text = duration.HasValue
            ? (int)duration.Value.TotalHours + ":" + duration.Value.Minutes.ToString("00")
            : "";

        _revenueVal.Text = _inputs == null ? "" : _revenue.ToString("N0");

        _hourlyVal.Text = duration.HasValue && duration.Value.TotalHours > 0 && _revenue > 0
            ? (_revenue / (decimal)duration.Value.TotalHours).ToString("N0")
            : "";

        if (_summary == null)
            _missionsVal.Text = "";
        else if (_summary.Completed == _summary.Total)
            _missionsVal.Text = _summary.Total.ToString();
        else
            _missionsVal.Text = _summary.Completed + " of " + _summary.Total + " done";
    }

    static bool TryTime(string text, out TimeSpan value) =>
        TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out value);

    static decimal ParseNumber(object? v)
    {
        var s = Convert.ToString(v, CultureInfo.CurrentCulture) ?? "";
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsDigit(ch) || ch == '.') sb.Append(ch);   // drops spaces, commas and non-breaking spaces
        }
        return decimal.TryParse(sb.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    // ------------------------------------------------------------------ copy to Google Sheets

    void CopyTable()
    {
        var sb = new StringBuilder();
        foreach (DataGridViewRow row in _logGrid.Rows)
        {
            string C(int i) =>
                (Convert.ToString(row.Cells[i].Value) ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

            var notes = C(5);
            var info = _rows.FirstOrDefault(x => x.Zone == (row.Tag as string));
            var loaded = "";
            var delivered = "";
            var total = C(1);
            if (info != null)
            {
                total = info.Missions.ToString();
                loaded = info.LoadedTime.HasValue ? FormatTime(info.LoadedTime.Value) : "";
                delivered = DeliveredText(info);
                if (info.Loaded < info.Missions)
                    notes = (notes + " (" + (info.Missions - info.Loaded) + " of " + info.Missions + " still waiting)").Trim();
                if (row.Cells[2].Value is true)
                    notes = (notes + " (partial pickup)").Trim();
            }

            // Same order as the spreadsheet: Pickup Location, Missions, Assigned Worker, Loaded Time, Delivered Time, Notes
            sb.Append(string.Join("\t", C(0), total, C(3), loaded, delivered, notes)).Append('\n');
        }

        if (sb.Length == 0)
        {
            SetStatus("Nothing to copy yet.");
            return;
        }
        Clipboard.SetText(sb.ToString());
        SetStatus("Copied. Click the first Pickup Location cell in the sheet and paste.");
    }

    void SetStatus(string text) => _status.Text = text;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _startBox.TextChanged += TimeBoxChanged;
        _endBox.TextChanged += TimeBoxChanged;
        _startBox.Leave += (_, _) => Storage.Save(_data);
        _endBox.Leave += (_, _) => Storage.Save(_data);
    }
}
