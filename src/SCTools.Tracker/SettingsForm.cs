using SCTools.GameLog;

namespace SCTools.Tracker;

/// <summary>The tracker's settings: the server, your key (Test asks the server whose key it is), and where Star Citizen is.</summary>
sealed class SettingsForm : Form
{
    readonly TextBox _server = new() { Width = 360 };
    readonly TextBox _key = new() { Width = 360, UseSystemPasswordChar = true };
    readonly TextBox _folder = new() { Width = 360 };
    readonly ComboBox _channel = new() { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label _test = new() { AutoSize = true, MaximumSize = new Size(440, 0) };

    public SettingsForm(SenderSettings settings)
    {
        Settings = settings;
        Text = SCTools.Updates.AppName.Tracker.Title + ": Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 9.5f);

        _server.Text = settings.ServerUrl;
        _key.Text = settings.Key;
        _folder.Text = settings.StarCitizenFolder;
        _channel.Items.AddRange(new object[] { "Auto", "LIVE", "HOTFIX", "PTU", "EPTU", "TECH-PREVIEW" });
        _channel.SelectedItem = _channel.Items.Contains(settings.Channel) ? settings.Channel : "Auto";

        var table = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        void Row(string label, Control control, Control? extra = null)
        {
            var r = table.RowCount++;
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 10, 7) }, 0, r);
            table.Controls.Add(control, 1, r);
            if (extra != null) table.Controls.Add(extra, 2, r);
        }

        var show = new CheckBox { Text = "Show", AutoSize = true, Anchor = AnchorStyles.Left };
        show.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !show.Checked;
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _folder.Text, Description = "The StarCitizen folder (with LIVE, HOTFIX... in it)" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
        };

        Row("Server", _server);
        Row("Your key", _key, show);
        Row("Star Citizen folder", _folder, browse);
        Row("Log channel", _channel);

        var test = new Button { Text = "Test", AutoSize = true };
        test.Click += async (_, _) => await TestAsync();
        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        bar.Controls.AddRange(new Control[] { cancel, save, test });
        var r2 = table.RowCount++;
        table.Controls.Add(_test, 0, r2);
        table.SetColumnSpan(_test, 3);
        var r3 = table.RowCount++;
        table.Controls.Add(bar, 0, r3);
        table.SetColumnSpan(bar, 3);
        Controls.Add(table);
        AcceptButton = save;
        CancelButton = cancel;
        save.Click += (_, _) => Store();
    }

    public SenderSettings Settings { get; }

    void Store()
    {
        Settings.ServerUrl = _server.Text.Trim();
        Settings.Key = _key.Text.Trim();
        Settings.StarCitizenFolder = _folder.Text.Trim();
        Settings.Channel = _channel.SelectedItem as string ?? "Auto";
    }

    /// <summary>Asks the server whose key this is, and looks for the Game.log.</summary>
    async Task TestAsync()
    {
        Store();
        _test.Text = "Asking the server...";
        try
        {
            using var server = new HttpLogServer(Settings.ServerUrl, Settings.Key);
            var player = await server.PlayerAsync(CancellationToken.None);
            var log = LogSources.ResolveLog(Settings.StarCitizenFolder, Settings.Channel);
            _test.Text = (player == null ? "The server does not know this key." : "The key is " + player + "'s.")
                + "  " + (log == null ? "No Game.log found in that folder." : "Game.log: " + log);
        }
        catch (Exception ex)
        {
            _test.Text = "Cannot reach the server: " + ex.Message;
        }
    }
}
