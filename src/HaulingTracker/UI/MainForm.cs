using System.Globalization;
using System.Text;

namespace HaulingTracker.UI;

public sealed partial class MainForm : Form
{
    static Color HeaderBlue => Theme.Header;
    static Color CalcBlue => Theme.Calc;

    static Color LostRed => Theme.Red;
    static Color DoneGreen => Theme.Green;
    static Color LostOrange => Theme.Orange;
    static Color PartialYellow => Theme.Yellow;

    const string DefaultLogPath = @"C:\Program Files\Roberts Space Industries\StarCitizen";

    readonly List<HaulingLog> _logs = new();   // one log per player (account); merged they are _state.Log
    readonly ToolStripMenuItem _addBtn = new();
    readonly DataGridView _tripGrid = new();
    readonly DataGridView _planRunsGrid = new();   // a read-only copy of Cargo Runs under the Trip Planner
    readonly AppData _data = Storage.Load();
    readonly Tracker _tracker;      // reads the logs and works out the session shown (see Load, Select, LoadStops, LoadRuns)
    readonly TrackerState _state;   // the merged log, the sessions, and the session shown with its stops and runs: what the services work from
    // the services of the core (see Tracker): the logic of the tabs, the window only shows what they give it
    readonly Places _places;
    readonly Crew _crew;
    readonly Contracts _contracts;
    readonly Earnings _earnings;
    readonly DropOffs _dropOffs;
    readonly CargoRuns _runs;
    readonly Whereabouts _where;
    readonly SessionFigures _figures;
    readonly Payouts _payouts;
    readonly Leaderboard _leaders;
    readonly PlanService _plan;
    readonly DataGridView _doneGrid = new();
    Label? _doneTitle;
    decimal _revenue;
    int _renderedVersion = -1;
    bool _updating;

    readonly ThemedCombo _sessionBox = new();
    readonly ToolStripMenuItem _openBtn = new();
    readonly ToolStripMenuItem _liveBtn = new();
    readonly ToolStripMenuItem _resetBtn = new();
    readonly ToolStripMenuItem _copyBtn = new();
    readonly Label _status = new();

    readonly TextBox _startBox = new();
    readonly TextBox _endBox = new();
    readonly Label _durationVal = new();
    readonly Label _missionsVal = new();
    readonly Label _revenueVal = new();
    readonly Label _hourlyVal = new();
    readonly Label _missionsDoneVal = new();
    readonly Label _missionsActiveVal = new();

    readonly DataGridView _rewards = new();
    readonly Label _rewardsTotalMissions = new();
    readonly Label _rewardsTotalRevenue = new();

    readonly DataGridView _logGrid = new();

    readonly System.Windows.Forms.Timer _timer = new();
    readonly System.Windows.Forms.Timer _clock = new();

    public MainForm()
    {
        var tracker = _tracker = new Tracker(_data);
        _state = tracker.State;
        _places = tracker.Places;
        _crew = tracker.Crew;
        _contracts = tracker.Contracts;
        _earnings = tracker.Earnings;
        _dropOffs = tracker.DropOffs;
        _runs = tracker.Runs;
        _where = tracker.Whereabouts;
        _figures = tracker.Figures;
        _payouts = tracker.Payouts;
        _leaders = tracker.Leaderboard;
        _plan = tracker.Plan;
        Text = "Hauling Tracker";
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1500, 860);
        MinimumSize = new Size(1250, 700);
        try
        {
            // the window and taskbar icon is the one built into the exe
            if (Environment.ProcessPath is { } exe) Icon = Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
        }
        Theme.Dark = _data.DarkMode;
        BackColor = Theme.Window;

        BuildUi();
        ApplyTheme(this);

        _clock.Interval = 1000;
        _clock.Tick += (_, _) =>
        {
            UpdateSessionClock();
            UpdateLiveDurations();
            AdvanceStatus();
        };
        _clock.Start();
        _timer.Interval = 1500;
        _timer.Tick += (_, _) => PollLive();
        FormClosing += (_, _) =>
        {
            if (!_skipSave) _state.Save();
        };
        Storage.SaveFailed += msg =>
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => SetStatus("Could not save the data file: " + msg)));
        };
        Storage.SaveOk += () =>
        {
            // a save that worked again after one that failed: the message is not true any more
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() =>
            {
                if (_status.Text.StartsWith("Could not save the data file", StringComparison.Ordinal)) SetStatus("The data file is saved again.");
            }));
        };

        InitSessionButtons();
        ClearView();
        InitWebView();
        SetStatus("Open a log file, or press \"Watch live Game.log\" while you play.");
    }

    // ------------------------------------------------------------------ layout

    void BuildUi()
    {
        var root = _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12, 3, 12, 12),   // the party table starts right under the line of the tabs
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 420));   // fitted to the boards by FitTopArea
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // the run board takes what is left and scrolls
        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        var sessionPage = new TabPage("Session") { BackColor = Color.White };
        var debtsPage = new TabPage("Debts") { BackColor = Color.White };
        tabs.TabPages.Add(sessionPage);
        tabs.TabPages.Add(debtsPage);
        Controls.Add(tabs);
        BuildMenu();
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        sessionPage.Controls.Add(root);
        debtsPage.Controls.Add(BuildDebtsPage());
        var historyPage = new TabPage("History") { BackColor = Color.White };
        tabs.TabPages.Add(historyPage);
        historyPage.Controls.Add(BuildHistoryPage());
        var plannerPage = new TabPage("Trip Planner") { BackColor = Color.White };
        tabs.TabPages.Insert(1, plannerPage);   // Session, Trip Planner, Debts, History: the same order as the web view
        plannerPage.Controls.Add(BuildPlannerPage());
        var contractsPage = new TabPage("Contracts") { BackColor = Color.White };
        tabs.TabPages.Insert(2, contractsPage);   // Session, Trip Planner, Contracts, Debts, History
        contractsPage.Controls.Add(BuildContractsPage());
        _contractsPage = contractsPage;
        _tabs = tabs;
        tabs.SelectedIndexChanged += (_, _) =>
        {
            _lockBtn.Visible = tabs.SelectedTab == plannerPage;
            if (tabs.SelectedTab == plannerPage) RefreshPlanner();
            else if (tabs.SelectedTab == contractsPage) RefreshContracts();
            else if (tabs.SelectedTab != sessionPage) RefreshDebts();
        };

        // top area: the session bar and the pickup boards on the left, the party with the small times and contracts tables on the right,
        // the leaderboard under both
        var main = _main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SideWidth));   // the party, the contracts and the times
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 306));   // both rows are fitted to the boards by FitTopArea
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));

        var left = _left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(0) };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));   // the title: Stops
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // the finished stops: shown when there are some
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        BuildLogGrid();
        BuildDoneGrid();
        BuildStatsGrid();
        FitColumns(_logGrid);     // these never scroll: the columns share the width instead
        FitColumns(_doneGrid);
        FitColumns(_statsGrid);
        _logGrid.Margin = new Padding(0, 0, 3, 3);     // the right gap keeps it clear of the summary tables
        _doneGrid.Margin = new Padding(0, 0, 3, 3);
        _statsGrid.Margin = new Padding(0, 3, 0, 3);   // runs to the right edge, like the run board
        foreach (var grid in new[] { _logGrid, _doneGrid, _statsGrid })
        {
            grid.RowsAdded += (_, _) => FitTopArea();
            grid.RowsRemoved += (_, _) => FitTopArea();
        }
        Shown += (_, _) => FitTopArea();
        _statsGrid.SizeChanged += (_, _) => AlignSessionColumns();
        left.Controls.Add(SectionTitle("Stops"), 0, 0);
        left.Controls.Add(_logGrid, 0, 1);
        _doneTitle = SectionTitle("Finished Stops");
        _doneTitle.Visible = false;
        _doneGrid.Visible = false;
        left.Controls.Add(_doneTitle, 0, 2);
        left.Controls.Add(_doneGrid, 0, 3);

        var board = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        board.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));
        board.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        board.Controls.Add(SectionTitle("Session Leaderboard"), 0, 0);
        board.Controls.Add(_statsGrid, 0, 1);

        // the party (with its name box and buttons) on top, the small tables with the contracts and the times under it
        var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), Padding = new Padding(10, 0, 0, 0) };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // as high as the party table with its members
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.Controls.Add(BuildPartyPanel(), 0, 0);
        side.Controls.Add(BuildHeader(), 0, 1);

        main.Controls.Add(left, 0, 0);
        main.Controls.Add(side, 1, 0);
        main.Controls.Add(board, 0, 1);
        main.SetColumnSpan(board, 2);

        // the run board with its title
        var runs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        runs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        runs.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));
        runs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        runs.Controls.Add(RunsTitle(), 0, 0);   // with a button that opens every run in a window of its own

        BuildTripGrid();
        root.Controls.Add(main, 0, 0);
        runs.Controls.Add(_tripGrid, 0, 1);
        root.Controls.Add(runs, 0, 1);
    }

    const int TitleHeight = 30;
    const int SideWidth = 560;     // the column on the right: the party, the contracts and the times
    const int ContractsHeight = 204;   // the contracts table: header and seven rows (hauling 2, 3, 4 stops, waste 2, 3, 4 drop-offs, Total)
    const int SummaryHeight = 190;     // the times and totals table: seven rows of 25 with their borders
    TableLayoutPanel? _header;

    /// <summary>The height the right column needs: the party, then the contracts and the times with their titles (as drawn, also on a scaled screen).</summary>
    int SideNeed()
    {
        var h = _partyPanel?.PreferredSize.Height ?? _partyHeight;
        if (_header != null)
        {
            foreach (RowStyle rs in _header.RowStyles)
            {
                if (rs.SizeType == SizeType.Absolute) h += (int)Math.Ceiling(rs.Height);
            }
        }
        return h;
    }

    /// <summary>
    /// The grid never scrolls, neither sideways nor up and down (FitTopArea gives it the height of all its rows).
    /// So the columns share the width in the proportion of their normal widths, and shrink a little on a narrow window.
    /// </summary>
    static void FitColumns(DataGridView g)
    {
        g.ScrollBars = ScrollBars.None;
        foreach (DataGridViewColumn c in g.Columns)
        {
            if (c.Name == GridCol.Number)
            {
                // the # column keeps its small width, the others share the rest
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                c.MinimumWidth = 30;
                c.Width = 36;
                continue;
            }
            c.FillWeight = c.Width;
            c.MinimumWidth = 40;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
    }

    TableLayoutPanel? _root, _left, _main;
    TabPage? _contractsPage;
    TabControl? _tabs;
    bool _fitPending;

    /// <summary>
    /// The pickup board and the leaderboard are always shown in full, the run board gets what is left (and scrolls).
    /// Called whenever their rows change or the window is resized.
    /// </summary>
    void FitTopArea()
    {
        if (_fitPending || !IsHandleCreated || _root == null || _left == null || _main == null) return;
        _fitPending = true;
        BeginInvoke(new Action(() =>
        {
            _fitPending = false;
            if (_root == null || _left == null || _main == null) return;
            int Need(DataGridView g)
            {
                var rows = 0;
                foreach (DataGridViewRow r in g.Rows) rows += r.Height + r.DividerHeight;
                return g.ColumnHeadersHeight + Math.Max(rows, g.RowTemplate.Height) + 4 + g.Margin.Vertical;
            }
            var pickups = Need(_logGrid);
            var done = Need(_doneGrid);
            var hasDone = _doneGrid.Rows.Count > 0;
            var leaders = Need(_statsGrid);

            // set the heights, then check that every row really is shown and add what is missing
            for (var pass = 0; pass < 4; pass++)
            {
                var title = LogicalToDeviceUnits(TitleHeight);   // as drawn, also on a scaled screen
                _left.RowStyles[0].Height = title;
                _left.RowStyles[1].Height = pickups;
                if (_doneTitle != null) _doneTitle.Visible = hasDone;
                _doneGrid.Visible = hasDone;
                _left.RowStyles[2].Height = hasDone ? title : 0;
                _left.RowStyles[3].Height = hasDone ? done : 0;
                var row0 = Math.Max(title + pickups + (hasDone ? title + done : 0), SideNeed());   // the party and the small tables on the right need their own height too
                var row1 = title + leaders;
                _main.RowStyles[0].Height = row0;
                _main.RowStyles[1].Height = row1;
                _root.RowStyles[0].Height = row0 + row1;
                _root.PerformLayout();
                _main.PerformLayout();
                _left.PerformLayout();

                var missingPickups = _logGrid.Rows.Count - _logGrid.DisplayedRowCount(false);
                var missingLeaders = _statsGrid.Rows.Count - _statsGrid.DisplayedRowCount(false);
                var missingDone = hasDone ? _doneGrid.Rows.Count - _doneGrid.DisplayedRowCount(false) : 0;
                AlignSessionColumns();
                if (missingPickups <= 0 && missingLeaders <= 0 && missingDone <= 0) break;
                if (missingDone > 0) done += missingDone * _doneGrid.RowTemplate.Height;
                if (missingPickups > 0) pickups += missingPickups * _logGrid.RowTemplate.Height;
                if (missingLeaders > 0) leaders += missingLeaders * _statsGrid.RowTemplate.Height;
            }
        }));
    }

    static List<DataGridViewColumn> ShownColumns(DataGridView g) =>
        g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();

    /// <summary>
    /// The first columns of the Stops, Finished Stops and Cargo Runs boards are as wide as the columns right above or below them in the Session Leaderboard
    /// (#, Worker, Completed Runs, ...), so everything lines up. The last column of the Stops boards (Notes, Handed In) takes what is left.
    /// </summary>
    void AlignSessionColumns()
    {
        var lead = ShownColumns(_statsGrid);
        if (lead.Count < 6 || lead[0].Width < 10) return;
        void Fix(DataGridView g, int count)
        {
            var cols = ShownColumns(g);
            for (var i = 0; i < count && i < cols.Count && i < lead.Count; i++)
            {
                if (i == cols.Count - 1) break;   // the last column keeps filling
                cols[i].MinimumWidth = 30;
                cols[i].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                cols[i].Width = lead[i].Width;
            }
        }
        Fix(_logGrid, 5);     // #, Pickup Location, From Ruin, Batches, Value
        Fix(_doneGrid, 6);    // #, Pickup Location, From Ruin, Delivered, Worker, Value
        Fix(_tripGrid, 2);    // #, Worker
    }

    static Label SectionTitle(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        Margin = new Padding(0),
        Padding = new Padding(2, 0, 0, 3),
    };

    /// <summary>
    /// The top row of the window, next to the Options menu: the status text just right of Options, and on the far right the session
    /// picker with the Start / Stop Session button.
    /// </summary>
    void AddSessionControlsToMenu()
    {
        _sessionBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sessionBox.Width = 260;
        _sessionBox.Margin = new Padding(0, 0, 8, 0);
        _sessionBox.SelectedIndexChanged += SessionChanged;

        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Size = new Size(430, 18);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.DimGray;
        _status.Tag = "muted";
        _status.Margin = new Padding(0);
        _menu.Items.Add(new ToolStripControlHost(_status) { Margin = new Padding(14, 0, 0, 0) });

        // right aligned items are placed from the right edge in the order they are added: the button is the last one, the picker is left of it
        _sessionToggleBtn.Alignment = ToolStripItemAlignment.Right;
        _sessionToggleBtn.Margin = new Padding(6, 1, 6, 1);
        _menu.Items.Add(_sessionToggleBtn);
        _menu.Items.Add(new ToolStripControlHost(_sessionBox) { Alignment = ToolStripItemAlignment.Right, Margin = new Padding(0, 0, 0, 0) });

        // the Trip Planner button (Next Stops: Locked / Unlocked), left of the session picker, only while that tab is shown
        _menu.ShowItemToolTips = true;
        _lockBtn.Alignment = ToolStripItemAlignment.Right;
        _lockBtn.Margin = new Padding(6, 1, 6, 1);
        _lockBtn.Click += (_, _) => ToggleLocks();
        _menu.Items.Add(_lockBtn);
    }

    readonly MenuStrip _menu = new();
    readonly List<ToolStripMenuItem> _channelItems = new();

    /// <summary>The Options menu in the top left of the window.</summary>
    void BuildMenu()
    {
        var options = new ToolStripMenuItem("Options");

        _openBtn.Text = "Open Log File(s)…";
        _openBtn.Click += (_, _) => OpenLogs();
        _liveBtn.Text = "Watch Live Game.log";
        _liveBtn.Click += (_, _) => ToggleLive();
        _addBtn.Text = "Add Live Source…";
        _addBtn.Click += (_, _) => AddSource();
        _sourcesBtn.Text = "Sources…";
        _sourcesBtn.Click += (_, _) => ShowSources();

        var channel = new ToolStripMenuItem("Log Channel");
        var current = _data.Channel is { Length: > 0 } ch && Channels.Contains(ch) ? ch : "Auto";
        foreach (var name in Channels)
        {
            var item = new ToolStripMenuItem(name) { Checked = name == current };
            item.Click += (_, _) => SetChannel(name);
            _channelItems.Add(item);
            channel.DropDownItems.Add(item);
        }

        _partyBtn.Text = "Party…";
        _partyBtn.Click += (_, _) => _partyGrid.Focus();   // the party is the table at the top right of the front page
        _resetBtn.Text = "Use Log Times";
        _resetBtn.Click += (_, _) => ResetTimes();
        _copyBtn.Text = "Copy Table for Google Sheets";
        _copyBtn.Click += (_, _) => CopyTable();
        var backupBtn = new ToolStripMenuItem("Open Backup Folder");
        backupBtn.Click += (_, _) => OpenBackupFolder();
        _themeBtn.Text = ThemeButtonText();
        _themeBtn.Click += (_, _) => ToggleDark();
        _webBtn.Text = WebButtonText();
        _webBtn.Click += (_, _) => ToggleWeb();

        options.DropDownItems.AddRange(new ToolStripItem[]
        {
            _openBtn, _liveBtn, _addBtn, _sourcesBtn, channel,
            new ToolStripSeparator(),
            _partyBtn,
            new ToolStripSeparator(),
            _resetBtn, _copyBtn, backupBtn,
            new ToolStripSeparator(),
            _themeBtn, _webBtn,
            new ToolStripSeparator(),
            _versionItem, _updateBtn,
        });
        InitUpdater();
        _menu.Items.Add(options);
        _menu.Dock = DockStyle.Top;
        AddSessionControlsToMenu();
    }

    /// <summary>Opens the folder with the daily copies of the data file.</summary>
    void OpenBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(Storage.BackupDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Storage.BackupDir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("Could not open the backup folder: " + ex.Message);
        }
    }

    static readonly string[] Channels = { "Auto", "LIVE", "HOTFIX", "PTU", "EPTU" };

    static void StyleButton(Button b, string text)
    {
        b.Text = text;
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.Height = 32;
        b.Margin = new Padding(0, 4, 10, 4);
        b.Padding = new Padding(8, 0, 8, 0);
    }

    /// <summary>The session summary on the right: the blue contract table on top, the clean times and totals table below it.</summary>
    Control BuildHeader()
    {
        var p = _header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0),
        };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));      // Contracts
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, ContractsHeight));   // the contracts: small, they are not the main thing
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleHeight));      // Session
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, SummaryHeight));
        p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // empty, so the clean table keeps its small rows

        // start / end / duration / totals, neutral and clean
        var t = new BufferedTable
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 2,
            RowCount = 7,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 7; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));

        ConfigureTimeBox(_startBox);
        ConfigureTimeBox(_endBox);
        StyleValue(_durationVal);
        StyleValue(_missionsVal);
        StyleValue(_revenueVal);
        StyleValue(_hourlyVal);
        StyleValue(_missionsDoneVal);
        StyleValue(_missionsActiveVal);

        AddSummaryRow(t, 0, "Start Time", WrapInput(_startBox));
        AddSummaryRow(t, 1, "Session Time", _durationVal);
        AddSummaryRow(t, 2, "Batches Delivered", _missionsVal);
        AddSummaryRow(t, 3, "Active Missions", _missionsActiveVal);
        AddSummaryRow(t, 4, "Missions Completed", _missionsDoneVal);
        AddSummaryRow(t, 5, "Total aUEC Earned", _revenueVal);
        AddSummaryRow(t, 6, "aUEC/Hour", _hourlyVal);
        p.Controls.Add(SectionTitle("Session"), 0, 2);
        p.Controls.Add(t, 0, 3);

        // the contract table on top, in the blue style of the other boards
        _rewards.Name = "compact";   // StyleGrid gives it smaller rows (also every time the theme is applied again)
        StyleGrid(_rewards);
        _rewards.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _rewards.Dock = DockStyle.Fill;
        _rewards.Margin = new Padding(0);
        _rewards.AllowUserToAddRows = false;
        _rewards.AllowUserToDeleteRows = false;
        _rewards.ScrollBars = ScrollBars.None;
        _rewards.Columns.Add(Col(RewardCol.Contract, "Contract", 110, true, DataGridViewContentAlignment.MiddleLeft));
        _rewards.Columns.Add(Col(RewardCol.Reward, "Mission Reward", 120, false, DataGridViewContentAlignment.MiddleRight));
        _rewards.Columns.Add(Col(RewardCol.Count, "# Missions", 90, true, DataGridViewContentAlignment.MiddleRight));
        _rewards.Columns.Add(Col(RewardCol.Total, "Total", 100, true, DataGridViewContentAlignment.MiddleRight, true));
        _rewards.CellEndEdit += (_, _) =>
        {
            if (_state.Inputs != null)
            {
                // every row knows which payout of the session it is
                foreach (DataGridViewRow row in _rewards.Rows)
                {
                    if (row.Tag is RewardLine line) line.Set(_state.Inputs, ParseNumber(row.Cells[RewardCol.Reward].Value));
                }
            }
            RecomputeRewards();
            _planShown = "";   // the values in the Trip Planner change with the payouts
            if (_planBody.Visible && _planBody.IsHandleCreated) RefreshPlanner();
            _state.Save();
            _payouts.UpdateSnapshots();
            RefreshDebts();
        };
        p.Controls.Add(SectionTitle("Contracts"), 0, 0);
        p.Controls.Add(_rewards, 0, 1);
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
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Input, Tag = "input", Padding = new Padding(0, 3, 6, 0), Margin = new Padding(0) };
        p.Controls.Add(tb);
        return p;
    }

    static void StyleValue(Label l)
    {
        l.Dock = DockStyle.Fill;
        l.TextAlign = ContentAlignment.MiddleRight;
        l.BackColor = CalcBlue;
        l.Tag = "calc";
        l.Margin = new Padding(0);
        l.Padding = new Padding(0, 0, 6, 0);
    }

    static void AddSummaryRow(TableLayoutPanel t, int row, string label, Control? value)
    {
        t.Controls.Add(new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
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
        _logGrid.Columns.Add(Col(StopCol.Location, "Pickup Location", 190, false, DataGridViewContentAlignment.MiddleLeft));
        _logGrid.Columns.Add(Col(StopCol.FromRuin, "From Ruin", 90, true, DataGridViewContentAlignment.MiddleRight));
        _logGrid.Columns.Add(Col(StopCol.Batches, "Batches", 90, true));
        var workerCol = new DataGridViewComboBoxColumn
        {
            Name = StopCol.Worker,
            HeaderText = "Assigned Worker",
            Width = 150,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        workerCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        workerCol.Visible = false;   // the runs get their workers from the logs and the Trip Planner now (the column stays so older sessions keep what was assigned)
        _logGrid.Columns.Add(workerCol);
        _logGrid.Columns.Add(Col(StopCol.Value, "Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        _logGrid.Columns.Add(Col(StopCol.Notes, "Notes", 120, false, DataGridViewContentAlignment.MiddleLeft, true));
        _logGrid.Columns.Add(Col(StopCol.DropOff, "Drop-off", 120, true, DataGridViewContentAlignment.MiddleCenter));   // cargo to bring out from Ruin (from-Ruin contracts)
        AddNumberColumn(_logGrid);
        _logGrid.CellEndEdit += LogGrid_CellEndEdit;
        _logGrid.DataError += (_, e) => e.ThrowException = false;
        _logGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_logGrid.IsCurrentCellDirty) _logGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    /// <summary>The finished stops: pickup locations whose missions are all completed. Nothing to type here.</summary>
    void BuildDoneGrid()
    {
        _doneGrid.Dock = DockStyle.Fill;
        StyleGrid(_doneGrid);
        _doneGrid.AllowUserToAddRows = false;
        _doneGrid.AllowUserToDeleteRows = false;
        _doneGrid.Columns.Add(Col(FinishedCol.Location, "Pickup Location", 190, true, DataGridViewContentAlignment.MiddleLeft));
        _doneGrid.Columns.Add(Col(FinishedCol.FromRuin, "From Ruin", 90, true, DataGridViewContentAlignment.MiddleRight));
        _doneGrid.Columns.Add(Col(FinishedCol.Delivered, "Delivered", 90, true));
        _doneGrid.Columns.Add(Col(FinishedCol.Worker, "Worker", 150, true));
        _doneGrid.Columns.Add(Col(FinishedCol.Value, "Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        _doneGrid.Columns.Add(Col(FinishedCol.HandedIn, "Handed In", 120, true, DataGridViewContentAlignment.MiddleCenter, true));
        AddNumberColumn(_doneGrid);
    }

    void BuildTripGrid()
    {
        _tripGrid.Dock = DockStyle.Fill;
        _tripGrid.Margin = new Padding(0, 3, 0, 3);   // no side gap, so it lines up with the boards above
        StyleGrid(_tripGrid);
        _tripGrid.AllowUserToAddRows = false;
        _tripGrid.AllowUserToDeleteRows = false;
        _tripGrid.Columns.Add(Col(RunCol.Worker, "Worker", 190, true));   // as wide as Pickup Location above
        var runCol = Col(RunCol.Number, "#", 36, true);   // the run number, at the far left like the # of the boards above
        _tripGrid.Columns.Add(runCol);
        runCol.DisplayIndex = 0;
        _tripGrid.Columns.Add(Col(RunCol.Places, "Pickup Location & Time", 420, true, DataGridViewContentAlignment.MiddleLeft, true));
        // the columns after Pickups add up to the width of the right panel, so Pickups ends where the boards above end
        _tripGrid.Columns.Add(Col(RunCol.Batches, "Batches", 90, true));
        _tripGrid.Columns.Add(Col(RunCol.Value, "Value", 100, true, DataGridViewContentAlignment.MiddleRight));
        _tripGrid.Columns.Add(Col(RunCol.Started, "Started", 76, true));
        _tripGrid.Columns.Add(Col(RunCol.Completed, "Completed", 108, true));
        _tripGrid.Columns.Add(Col(RunCol.Time, "Time", 90, true));
        _tripGrid.Columns.Add(Col(RunCol.Flight, "Flight time", 90, true));   // the estimated flying time of the run (the Flight time of the Trip Planner)
        var lostCol = new DataGridViewComboBoxColumn
        {
            Name = RunCol.Lost,
            HeaderText = "Lost Cargo",
            Width = 116,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        lostCol.Items.AddRange("None", "Little", "All");
        lostCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _tripGrid.Columns.Add(lostCol);
        var hourly = Col(RunCol.PerHour, "aUEC/h", 96, true, DataGridViewContentAlignment.MiddleRight);   // what the run earned per hour, shown right of Flight time
        _tripGrid.Columns.Add(hourly);
        hourly.DisplayIndex = lostCol.DisplayIndex;
        _tripGrid.DataError += (_, e) => e.ThrowException = false;
        _tripGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_tripGrid.IsCurrentCellDirty) _tripGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _tripGrid.CellValueChanged += (_, e) =>
        {
            if (_updating || _state.Inputs == null || e.RowIndex < 0 || _tripGrid.ColumnName(e.ColumnIndex) != RunCol.Lost) return;
            var row = _tripGrid.Rows[e.RowIndex];
            if (row.Tag is not WorkerTrip trip) return;
            var level = Convert.ToString(row.Cells[RunCol.Lost].Value) ?? "None";
            // All lost = the run failed: one that is still on the way ends here, and picking something else brings it back
            var restart = _runs.SetLostLevel(trip, level);
            row.DefaultCellStyle.BackColor = RunColor(level, trip.End.HasValue);
            _state.Save();
            BeginInvoke(new Action(() =>
            {
                if (restart)
                {
                    FillTrips();
                    if (_planBody.Visible && _planBody.IsHandleCreated) RefreshPlanner();   // a run that failed is not on the way any more
                }
                else RecomputeTimes();
                _payouts.UpdateSnapshots();
                RefreshDebts();
            }));
        };
    }

    /// <summary>Red = all cargo lost, orange = some lost, green = completed with everything delivered, yellow = still on the way.</summary>
    static Color RunColor(string level, bool ended) =>
        level == "All" ? LostRed : level == "Little" ? LostOrange : ended ? DoneGreen : PartialYellow;   // yellow = on the way

    /// <summary>Keeps the duration of runs that are on the way ticking between log updates.</summary>
    void UpdateLiveDurations()
    {
        foreach (var grid in new[] { _tripGrid, _planRunsGrid })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is not WorkerTrip t || t.End.HasValue) continue;
                var text = _runs.RunDuration(t);
                if (!Equals(row.Cells[RunCol.Time].Value, text)) row.Cells[RunCol.Time].Value = text;
                var perHour = _runs.RunHourlyText(t);
                if (!Equals(row.Cells[RunCol.PerHour].Value, perHour)) row.Cells[RunCol.PerHour].Value = perHour;
            }
        }
    }

    void FillTrips()
    {
        var wasUpdating = _updating;
        _updating = true;
        try
        {
            _tripGrid.Rows.Clear();
            _planRunsGrid.Rows.Clear();
            _tracker.LoadRuns();   // latest activity on top
            if (_state.Current == null || _state.Inputs == null || _state.Log == null) return;

            foreach (var t in _state.Trips)
            {
                var level = _runs.LostLevel(t.Key);
                var batches = _runs.BatchesText(t);
                var value = _earnings.RunValueText(t);
                var completed = t.End.HasValue ? FormatTime(t.End.Value) : "";
                var duration = _runs.RunDuration(t);
                var flight = _runs.RunFlightText(t);
                var perHour = _runs.RunHourlyText(t);
                // the short copy under the map in the Trip Planner shows just the places
                foreach (var (grid, places) in new[] { (_tripGrid, _runs.RunStepsText(t)), (_planRunsGrid, _runs.RunPlacesText(t)) })
                {
                    var row = grid.AddRow(
                        (RunCol.Worker, t.Worker),
                        (RunCol.Number, t.Number.ToString(CultureInfo.InvariantCulture)),
                        (RunCol.Places, places),
                        (RunCol.Batches, batches),
                        (RunCol.Value, value),
                        (RunCol.Started, FormatTime(t.Start)),
                        (RunCol.Completed, completed),
                        (RunCol.Time, duration),
                        (RunCol.Flight, flight),
                        (RunCol.Lost, level),
                        (RunCol.PerHour, perHour));
                    row.Tag = t;
                    row.DefaultCellStyle.BackColor = RunColor(t.Manual ? "All" : level, t.End.HasValue);
                }
            }

            // the finished stops show who carried from them
            foreach (DataGridViewRow row in _doneGrid.Rows)
            {
                if (row.Tag is not string zone) continue;
                row.Cells[FinishedCol.Worker].Value = string.Join(", ", _state.Trips.Where(t => t.Pickups.Any(p => p.Zone == zone)).Select(t => t.Worker).Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }
        finally
        {
            _updating = wasUpdating;
            RecomputeTimes();   // also refreshes the leaderboard
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
            Tag = "muted",
            AutoEllipsis = true,
        }, 1, 0);
        return f;
    }

    /// <summary>A text column: its name (the code finds its cells by it, see Columns.cs), the header that is shown, its width, and whether it is read-only (tinted).</summary>
    static DataGridViewTextBoxColumn Col(
        string name, string header, int width, bool readOnly,
        DataGridViewContentAlignment align = DataGridViewContentAlignment.MiddleCenter,
        bool fill = false)
    {
        var c = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        c.DefaultCellStyle.Alignment = align;
        if (readOnly) c.DefaultCellStyle.BackColor = CalcBlue;
        if (fill)
        {
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            c.MinimumWidth = 90;
        }
        return c;
    }

    /// <summary>A "#" column at the far left that counts the rows 1, 2, 3... It is added last and moved to the left on screen. NumberRows fills it.</summary>
    static void AddNumberColumn(DataGridView g)
    {
        var c = Col(GridCol.Number, "#", 36, true);
        g.Columns.Add(c);
        c.DisplayIndex = 0;
    }

    /// <summary>Numbers the first `count` rows 1, 2, 3... (the rest, like a Total row, stay empty).</summary>
    static void NumberRows(DataGridView g, int? count = null)
    {
        if (!g.Columns.Contains(GridCol.Number)) return;
        var n = count ?? g.Rows.Count;
        for (var i = 0; i < g.Rows.Count; i++) g.Rows[i].Cells[GridCol.Number].Value = i < n ? (i + 1).ToString(CultureInfo.InvariantCulture) : "";
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridView, object> HookedGrids = new();

    /// <summary>A grid only shows a selected cell while you are working in it, so no first cell lights up blue by itself.</summary>
    static void HideIdleSelection(DataGridView g)
    {
        if (HookedGrids.TryGetValue(g, out _)) return;
        HookedGrids.Add(g, new object());
        var busy = false;
        void Clear()
        {
            if (busy || g.ContainsFocus || g.SelectedCells.Count == 0) return;
            busy = true;
            try { g.ClearSelection(); }
            finally { busy = false; }
        }
        g.SelectionChanged += (_, _) => Clear();
        g.RowsAdded += (_, _) => { if (g.IsHandleCreated) g.BeginInvoke(new Action(Clear)); };
        g.HandleCreated += (_, _) => g.BeginInvoke(new Action(Clear));
        g.Leave += (_, _) => Clear();
    }

    static void StyleGrid(DataGridView g)
    {
        HideIdleSelection(g);
        g.BackgroundColor = Theme.Window;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        g.GridColor = Theme.Lines;
        g.DefaultCellStyle.BackColor = Theme.Cell;
        g.DefaultCellStyle.ForeColor = Theme.Text;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Header;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Header;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        var wrap = g.Name == "wrapheader";   // the leaderboards have long column names that wrap onto two lines
        var compact = g.Name == "compact";   // the small contracts table
        g.ColumnHeadersHeight = wrap ? 46 : compact ? 26 : 28;
        g.ColumnHeadersDefaultCellStyle.WrapMode = wrap ? DataGridViewTriState.True : DataGridViewTriState.NotSet;
        g.RowHeadersVisible = false;
        g.RowTemplate.Height = compact ? 24 : 26;
        g.AllowUserToResizeRows = false;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        g.DefaultCellStyle.SelectionBackColor = Theme.Selection;
        g.DefaultCellStyle.SelectionForeColor = Theme.SelectionText;
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

    readonly ToolStripMenuItem _sourcesBtn = new();
    readonly ToolStripMenuItem _partyBtn = new();

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

    void SetChannel(string name)
    {
        foreach (var item in _channelItems) item.Checked = item.Text == name;
        _data.Channel = name;
        _state.Save();
        if (_state.Live) PollLive();
    }

    readonly List<LiveSource> _sources = new();

    /// <summary>The sources that are switched on. A disabled source keeps its address but is not read.</summary>
    List<string> ActivePaths() => _data.WatchPaths.Where(p => !_data.DisabledWatchPaths.Contains(p)).ToList();

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
            _state.Current = null;
            _state.Log = null;
            RefreshFromLog();
            SetStatus($"Loaded {files.Count} log file(s) for {_logs.Count} player(s), {_state.Sessions.Count} session(s) found.");
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
        _data.DisabledWatchPaths.Remove(dlg.SelectedPath);
        _state.Save();
        SetStatus("Added " + dlg.SelectedPath);
        ApplySources();
    }

    /// <summary>Shows every added live source with a tick to switch it on or off, and lets you remove one for good.</summary>
    void ShowSources()
    {
        using var f = new Form
        {
            Text = "Live Sources",
            ClientSize = new Size(820, 320),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var label = new Label { Text = "Tick or untick Enabled: it works at once. Untick stops reading a source without losing its address.", Dock = DockStyle.Top, Height = 28, Padding = new Padding(6, 6, 6, 0) };
        var grid = new DataGridView();
        StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = SourceCol.Enabled, HeaderText = "Enabled", Width = 70, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(Col(SourceCol.Source, "Source", 330, true, DataGridViewContentAlignment.MiddleLeft));
        grid.Columns.Add(Col(SourceCol.Reads, "Reads", 400, true, DataGridViewContentAlignment.MiddleLeft, true));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var remove = new Button { Text = "Remove Selected", AutoSize = true };
        bar.Controls.Add(close);
        bar.Controls.Add(remove);
        f.Controls.Add(grid);
        f.Controls.Add(label);
        f.Controls.Add(bar);
        f.AcceptButton = close;

        var filling = false;
        void Fill()
        {
            filling = true;
            grid.Rows.Clear();
            foreach (var path in _data.WatchPaths)
            {
                var on = !_data.DisabledWatchPaths.Contains(path);
                var row = grid.AddRow((SourceCol.Enabled, on), (SourceCol.Source, path), (SourceCol.Reads, ""));
                row.Tag = path;
                RefreshStatus(row);
            }
            filling = false;
        }
        void RefreshStatus(DataGridViewRow row)
        {
            var path = (string)row.Tag!;
            if (row.Cells[SourceCol.Enabled].Value is false) { row.Cells[SourceCol.Reads].Value = "switched off"; return; }
            row.Cells[SourceCol.Reads].Value = ResolveLog(path) ?? "cannot be reached right now";
        }
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (filling || grid.ColumnName(e.ColumnIndex) != SourceCol.Enabled || e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            var path = (string)row.Tag!;
            if (row.Cells[SourceCol.Enabled].Value is false)
            {
                if (!_data.DisabledWatchPaths.Contains(path)) _data.DisabledWatchPaths.Add(path);
            }
            else _data.DisabledWatchPaths.Remove(path);
            _state.Save();
            filling = true;   // RefreshStatus writes to a cell, that is not a click
            RefreshStatus(row);
            filling = false;
            ApplySources();   // a tick works at once, the dialog does not have to be closed
        };
        remove.Click += (_, _) =>
        {
            if (grid.CurrentRow?.Tag is not string path) return;
            _data.WatchPaths.Remove(path);
            _data.DisabledWatchPaths.Remove(path);
            _state.Save();
            Fill();
            ApplySources();
        };
        Fill();
        ApplyTheme(f);
        f.ShowDialog(this);
    }

    /// <summary>
    /// Makes the program read exactly the sources that are ticked, right now. With none ticked it stops and clears the screen;
    /// ticking one again starts watching again (unless you pressed Stop Watching yourself).
    /// </summary>
    void ApplySources()
    {
        if (ActivePaths().Count > 0)
        {
            if (_state.Live || !_userStopped) StartLive();
            return;
        }
        StopLive();
        _logs.Clear();
        _sources.Clear();
        _renderedVersion = -1;
        RefreshFromLog();
        SetStatus("All sources are switched off. Tick one in Sources to watch again.");
    }

    bool _userStopped;   // Stop Watching was pressed: ticking a source does not start watching by itself

    void ToggleLive()
    {
        if (_state.Live)
        {
            StopLive();
            _userStopped = true;
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
        if (ActivePaths().Count == 0)
        {
            SetStatus("All sources are switched off. Tick one in Sources to watch.");
            return;
        }
        StartLive();
    }

    void StartLive()
    {
        _userStopped = false;
        _sources.Clear();
        _logs.Clear();
        var active = ActivePaths();
        foreach (var path in active)
        {
            var src = new LiveSource { Path = path };
            _sources.Add(src);
            _logs.Add(src.Log);
        }
        _renderedVersion = -1;
        _state.Current = null;
        _state.Log = null;
        _state.Live = true;
        _liveBtn.Text = "Stop Watching";
        _timer.Start();
        PollLive();
        _watchText = "Watching " + string.Join(", ", active);
        SetStatus(_watchText);
    }

    string _watchText = "";

    /// <summary>Shows the time of the last line read from the logs next to the sources, so it can be seen at a glance that the window follows the game.</summary>
    void ShowLastLine()
    {
        if (!_state.Live || _watchText.Length == 0 || _logs.Count == 0 || !_status.Text.StartsWith(_watchText, StringComparison.Ordinal)) return;   // a message in the status line stays
        var last = _logs.Max(l => l.LastTime);
        if (last == DateTime.MinValue) return;
        var text = _watchText + "   ·   last log line " + last.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        if (_status.Text != text) _status.Text = text;
    }

    void StopLive()
    {
        _timer.Stop();
        _state.Live = false;
        _liveBtn.Text = "Watch Live Game.log";
    }

    void PollLive()
    {
        if (!_state.Live) return;
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
        UpdateLiveDurations();
        ShowLastLine();
    }

    static readonly TimeSpan BackupAge = TimeSpan.FromHours(24);

    /// <summary>Reads the logs in the logbackups folder next to Game.log that are less than 24 hours old.</summary>
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

    /// <summary>
    /// True while somebody is really typing: the updates wait for him. A cell that is still in edit mode while the user is somewhere else (another tab,
    /// another window) is finished here (what was typed is kept), so a forgotten edit can never stop the window from following the log.
    /// </summary>
    bool IsEditing() =>
        EditingIn(_logGrid) || EditingIn(_rewards) || EditingIn(_balanceGrid) || EditingIn(_payoutGrid) || (_startBox.Focused && _startBox.Modified) || (_endBox.Focused && _endBox.Modified);

    static bool EditingIn(DataGridView g)
    {
        if (!g.IsCurrentCellInEditMode) return false;
        if (g.ContainsFocus) return true;
        g.EndEdit();
        return false;
    }

    void RefreshFromLog()
    {
        var version = _logs.Sum(l => l.Version) + _logs.Count * 1000003;
        if (version == _renderedVersion) return;
        if (IsEditing()) return;   // do not disturb typing, we try again on the next tick
        _renderedVersion = version;

        if (_logs.Count == 0) { ClearView(); return; }

        // the logs of all accounts are merged, a mission shared in the party appears once
        _tracker.Load(_logs);
        var keep = _forceKey ?? _state.Current?.Key;
        _forceKey = null;

        _updating = true;
        try
        {
            _sessionBox.Items.Clear();
            foreach (var g in _state.Sessions) _sessionBox.Items.Add(DescribeSession(g));

            var idx = keep == null ? -1 : _state.Sessions.FindIndex(g => g.Key == keep);
            if (idx < 0) idx = _state.Sessions.Count - 1;
            if (idx >= 0) _sessionBox.SelectedIndex = idx;
        }
        finally
        {
            _updating = false;
        }

        _state.Current = _sessionBox.SelectedIndex >= 0 ? _state.Sessions[_sessionBox.SelectedIndex] : null;
        if (_state.Current == null)
        {
            ClearView();
            _payouts.UpdateSnapshots();
            RefreshDebts();
            SetStatus("No session yet. Press Start Session to begin.");
            return;
        }
        ShowSession();
        _payouts.UpdateSnapshots();
        RefreshDebts();
        if (_tabs != null && _tabs.SelectedTab == _contractsPage) RefreshContracts();
    }

    static string DescribeSession(SessionGroup g)
    {
        var s = SessionBuilder.Summarize(g);
        var start = s.Start.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
        if (g.Manual)
        {
            var until = g.Stop.HasValue ? g.Stop.Value.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture) : "now";
            return "Session: " + start + " to " + until + (g.Stop.HasValue ? "" : " (running)") + "  (" + g.Missions.Count + " missions)";
        }
        var end = s.End.HasValue ? s.End.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) : "ongoing";
        return start + " to " + end + "  (" + g.Missions.Count + " missions)";
    }

    void SessionChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        var i = _sessionBox.SelectedIndex;
        if (i < 0 || i >= _state.Sessions.Count) return;
        _state.Current = _state.Sessions[i];
        ShowSession();
    }

    void ShowSession()
    {
        if (_state.Current == null)
        {
            ClearView();
            return;
        }

        _updating = true;
        try
        {
            _tracker.Select(_state.Current);
            _plan.ClaimNewPickups();   // before the boards change: the plan still has the batches that were just picked up
            _tracker.LoadStops();

            _startBox.Text = _state.Inputs.Start ?? FormatTime(_state.Summary.Start);
            _endBox.Text = _state.Inputs.End ?? (_state.Summary.End.HasValue ? FormatTime(_state.Summary.End.Value) : "");

            // the mission counts come from the log (2, 3 and 4 stop contracts), you only type the payouts
            _rewards.Rows.Clear();
            Earnings.FillDefaults(_state.Inputs);   // nothing typed: the built-in payouts
            var accepted = _state.Current.Missions;   // every accepted mission of the session (abandoned ones are already left out)
            // the from-Ruin (waste) contracts: the log does not say what they pay either, so you type it once, by the number of drop-offs
            var waste = _earnings.SessionWaste(_state.Current);
            foreach (var line in Earnings.RewardLines)
            {
                var row = _rewards.AddRow((RewardCol.Contract, line.Contract), (RewardCol.Reward, line.Get(_state.Inputs).ToString("N0")), (RewardCol.Count, line.CountIn(accepted, waste)), (RewardCol.Total, ""));
                row.Tag = line;
            }
            var totalRow = _rewards.AddRow((RewardCol.Contract, "Total"), (RewardCol.Reward, ""), (RewardCol.Count, 0), (RewardCol.Total, ""));
            totalRow.ReadOnly = true;
            totalRow.DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

            FillLog();
            FillTrips();
        }
        finally
        {
            _updating = false;
        }

        RecomputeRewards();
        // The plan is worked out last, when the batches (FillLog) and the runs (FillTrips) are both up to date. Before, the page was built in the middle of this:
        // with the batch you just picked up already gone from the open batches, but the run that carries it not there yet, so the plan sent you out with a whole new
        // trip on top of it, and it stayed like that until the next change in the log.
        if (_planBody.Visible && _planBody.IsHandleCreated) RefreshPlanner();   // the plan follows the batches you pick up while the tab is open
        else if (_state.Inputs != null) _plan.GetPlan();   // keeps the plan and the locked Current trips up to date while the planner tab is closed
    }

    void ClearView()
    {
        _tracker.ClearSession();
        _updating = true;
        try
        {
            _doneGrid.Rows.Clear();
            _startBox.Text = "";
            _endBox.Text = "";
            _rewards.Rows.Clear();
            _logGrid.Rows.Clear();
            _tripGrid.Rows.Clear();
            _planRunsGrid.Rows.Clear();
            _statsGrid.Rows.Clear();
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
        _missionsDoneVal.Text = "";
        _missionsActiveVal.Text = "";
        _rewardsTotalMissions.Text = "";
        _rewardsTotalRevenue.Text = "";
        RefreshPartyPanel();   // the party is not part of a session: it is shown also when no session is loaded
    }

    /// <summary>Missions still waiting at this location / missions that have cargo there. 5 missions start as 5/5.</summary>
    static string MissionsText(LocationRow r) => (r.Missions - r.Loaded) + "/" + r.Missions;

    static string DeliveredText(LocationRow r)
    {
        if (r.Missing > 0 && r.Missing >= r.Missions) return "Cargo Missing";
        if (r.DeliveredTime.HasValue) return FormatTime(r.DeliveredTime.Value) + (r.Missing > 0 ? "  (" + r.Missing + " missing)" : "");
        if (r.Delivered > 0) return "Partial " + r.Delivered + "/" + r.Missions;
        return "";
    }

    void RefreshWorkerChoices()
    {
        var col = (DataGridViewComboBoxColumn)_logGrid.Columns[StopCol.Worker]!;
        col.Items.Clear();
        col.Items.Add("");
        // only the active party members can be picked; someone already assigned stays in the list so the cell can show him
        var choices = _crew.ActiveCrew();
        if (_state.Inputs != null)
        {
            foreach (var r in _state.Inputs.Rows.Values)
            {
                var w = r.Worker?.Trim() ?? "";
                if (w.Length > 0 && !choices.Any(x => SameName(x, w))) choices.Add(w);
            }
        }
        foreach (var name in choices) col.Items.Add(name);
    }

    void FillLog()
    {
        _logGrid.Rows.Clear();

        // a location nobody is assigned to gets the player the logs show picking up there (you can change it afterwards)
        var assignedNow = false;
        if (_state.Log != null && _state.Current != null)
        {
            foreach (var r in _state.Rows)
            {
                var input = _state.RowOf(r.Zone);
                if (!string.IsNullOrWhiteSpace(input.Worker)) continue;
                var who = WorkerTripBuilder.PickupActor(_state.Current, r.Zone, _state.Log, _state.Inputs!.WorkersResetAt);
                if (who == null) continue;
                input.Worker = who;
                assignedNow = true;
            }
        }
        if (assignedNow) _state.Save();

        // a worker that is not in the party yet (typed earlier) is added to it
        foreach (var r in _state.Rows)
        {
            if (_state.Inputs!.Rows.TryGetValue(r.Zone, out var w) && !string.IsNullOrWhiteSpace(w.Worker)
                && !_data.Party.Contains(w.Worker))
                _data.Party.Add(w.Worker);
        }
        RefreshWorkerChoices();

        // the most valuable location on top. Once new missions are accepted, completed ones are gone from here
        // (everything else, runs, leaderboards and totals, keeps counting them)
        foreach (var r in _state.PickupRows.OrderByDescending(_earnings.StopValue))
        {
            _state.Inputs!.Rows.TryGetValue(r.Zone, out var inp);
            var row = _logGrid.AddRow(
                (StopCol.Location, _places.LocationName(r.Zone)),
                (StopCol.FromRuin, _places.DistanceText(r.Zone)),
                (StopCol.Batches, MissionsText(r)),
                (StopCol.Worker, inp?.Worker ?? ""),
                (StopCol.Value, _earnings.StopValueText(r)),
                (StopCol.Notes, inp?.Notes ?? ""));
            row.Tag = r.Zone;
            ColorMissionsCell(row, r);
        }
        FillDropOffs();
        NumberRows(_logGrid);

        // the finished stops: latest hand-in on top
        _doneGrid.Rows.Clear();
        foreach (var r in _state.DoneRows.OrderByDescending(x => x.DeliveredTime ?? x.LoadedTime ?? DateTime.MinValue))
        {
            _state.Inputs!.Rows.TryGetValue(r.Zone, out var inp);
            var row = _doneGrid.AddRow(
                (FinishedCol.Location, _places.LocationName(r.Zone)),
                (FinishedCol.FromRuin, _places.DistanceText(r.Zone)),
                (FinishedCol.Delivered, r.Delivered + "/" + r.Missions),
                (FinishedCol.Worker, inp?.Worker ?? ""),
                (FinishedCol.Value, _earnings.StopValueText(r)),
                (FinishedCol.HandedIn, DeliveredText(r)));
            row.Tag = r.Zone;
            row.Cells[FinishedCol.Delivered].Style.BackColor = DoneGreen;
            if (r.Missing > 0) row.Cells[FinishedCol.HandedIn].Style.BackColor = LostOrange;   // cargo is missing from a mission that has this stop
        }
        NumberRows(_doneGrid);
        FitTopArea();
        RefreshPartyPanel();
        // the plan is not refreshed here: the runs (_state.Trips) are built after this, and a plan made from the new batches and the old runs is wrong (see ShowSession)
    }

    void RefreshStopValues()
    {
        foreach (DataGridViewRow row in _logGrid.Rows)
        {
            if (row.Tag is not string zone) continue;
            var info = _state.PickupRows.FirstOrDefault(x => x.Zone == zone);
            if (info != null) row.Cells[StopCol.Value].Value = _earnings.StopValueText(info);
        }
        foreach (DataGridViewRow row in _doneGrid.Rows)
        {
            if (row.Tag is not string zone) continue;
            var info = _state.DoneRows.FirstOrDefault(x => x.Zone == zone);
            if (info != null) row.Cells[FinishedCol.Value].Value = _earnings.StopValueText(info);
        }
        foreach (var grid in new[] { _tripGrid, _planRunsGrid })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is not WorkerTrip t) continue;
                row.Cells[RunCol.Value].Value = _earnings.RunValueText(t);
                row.Cells[RunCol.PerHour].Value = _runs.RunHourlyText(t);
            }
        }
    }

    void ColorMissionsCell(DataGridViewRow row, LocationRow r)
    {
        row.Cells[StopCol.Batches].Style.BackColor = r.Loaded >= r.Missions ? DoneGreen : CalcBlue;
    }

    // ------------------------------------------------------------------ editing

    void LogGrid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updating || _state.Inputs == null || e.RowIndex < 0) return;
        var row = _logGrid.Rows[e.RowIndex];
        if (row.Tag is not string zone) return;
        var text = (Convert.ToString(row.Cells[e.ColumnIndex].Value) ?? "").Trim();

        switch (_logGrid.ColumnName(e.ColumnIndex))
        {
            case StopCol.Location:
                if (_places.Rename(zone, text)) row.Cells[StopCol.Location].Value = _places.LocationName(zone);   // back to the name the program knows
                row.Cells[StopCol.FromRuin].Value = _places.DistanceText(zone);   // a new name can be one the distance table knows
                break;
            case StopCol.Notes:
                _state.RowOf(zone).Notes = text;
                break;
        }
        _state.Save();
    }

    void RecomputeRewards()
    {
        if (_state.Inputs == null) return;

        decimal revenue = 0;
        var missions = 0;
        DataGridViewRow? totalRow = null;

        var wasUpdating = _updating;
        _updating = true;
        try
        {
            foreach (DataGridViewRow row in _rewards.Rows)
            {
                if (row.Tag is not RewardLine)
                {
                    totalRow = row;   // the Total row
                    continue;
                }
                var reward = ParseNumber(row.Cells[RewardCol.Reward].Value);
                var count = (int)ParseNumber(row.Cells[RewardCol.Count].Value);
                var total = reward * count;
                row.Cells[RewardCol.Total].Value = total == 0 ? "" : total.ToString("N0");
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
        if (totalRow != null)
        {
            totalRow.Cells[RewardCol.Count].Value = missions;
            totalRow.Cells[RewardCol.Total].Value = revenue == 0 ? "" : revenue.ToString("N0");
        }
        RefreshStopValues();
        RecomputeTimes();
    }

    void TimeBoxChanged(object? sender, EventArgs e)
    {
        if (_updating || _state.Inputs == null) return;
        var start = _startBox.Text.Trim();
        var end = _endBox.Text.Trim();
        _state.Inputs.Start = start.Length == 0 ? null : start;
        _state.Inputs.End = end.Length == 0 ? null : end;
        RecomputeTimes();
    }

    void ResetTimes()
    {
        if (_state.Inputs == null) return;
        _state.Inputs.Start = null;
        _state.Inputs.End = null;
        _state.Save();
        ShowSession();
    }

    decimal _incomeNow;

    /// <summary>Ticks the session time and the hourly rate every second.</summary>
    void UpdateSessionClock()
    {
        var d = _figures.Elapsed();
        var time = d.HasValue ? LongDuration(d.Value) : "";
        var hourly = SessionFigures.HourlyText(d, _incomeNow);
        if (_durationVal.Text != time) _durationVal.Text = time;   // only repaint what changed
        if (_hourlyVal.Text != hourly) _hourlyVal.Text = hourly;
    }

    void RecomputeTimes()
    {
        // total earned = what completed missions (and from-Ruin contracts) really paid
        var income = _earnings.Earned();
        _incomeNow = income;
        _revenueVal.Text = income == 0 ? "" : income.ToString("N0");
        UpdateSessionClock();

        FillStats();

        // batches = pickup stops; delivered = the ones in finished runs
        _missionsVal.Text = _figures.BatchesText();
        _missionsDoneVal.Text = _figures.CompletedText();
        _missionsActiveVal.Text = _figures.ActiveText();
    }

    // ------------------------------------------------------------------ copy to Google Sheets

    void CopyTable()
    {
        var sb = new StringBuilder();
        // the whole session, also the locations that are gone from the pickup table because their missions are completed
        foreach (var info in _state.Inputs == null ? new List<LocationRow>() : _state.Rows.OrderByDescending(_earnings.StopValue).ToList())
        {
            static string C(string? v) =>
                (v ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

            _state.Inputs!.Rows.TryGetValue(info.Zone, out var inp);
            var notes = C(inp?.Notes);
            var loaded = info.LoadedTime.HasValue ? FormatTime(info.LoadedTime.Value) : "";
            var delivered = DeliveredText(info);
            if (info.Loaded < info.Missions)
                notes = (notes + " (" + (info.Missions - info.Loaded) + " of " + info.Missions + " still waiting)").Trim();

            // Same order as the spreadsheet: Pickup Location, Missions, Assigned Worker, Loaded Time, Delivered Time, Notes
            sb.Append(string.Join("\t", C(_places.LocationName(info.Zone)), info.Missions.ToString(), C(inp?.Worker), loaded, delivered, notes)).Append('\n');
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
        // coming back to the window (from behind a full-screen video, from the game, or from the taskbar) shows the latest log and repaints everything
        Activated += (_, _) =>
        {
            if (!_state.Live) return;
            RefreshFromLog();
            UpdateLiveDurations();
            Invalidate(true);
        };
        _startBox.TextChanged += TimeBoxChanged;
        _endBox.TextChanged += TimeBoxChanged;
        _startBox.Leave += (_, _) => _state.Save();
        _endBox.Leave += (_, _) => _state.Save();

        // start watching by itself: this also reads the backup logs of the last 24 hours
        Shown += (_, _) =>
        {
            if (!_state.Live && (ActivePaths().Count > 0 || (_data.WatchPaths.Count == 0 && Directory.Exists(DefaultLogPath)))) ToggleLive();
        };
    }
}
