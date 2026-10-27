using System.Diagnostics;
using System.Net.Http;
using Microsoft.Win32;
using HaulingTracker.GameLog;
using HaulingTracker.Updates;

namespace HaulingTracker.Sender;

/// <summary>
/// The tracker in the tray: every three seconds it uploads what is new in the Game.log (and the backup logs of the last day) to the server, and it updates
/// itself from the latest release on GitHub. The menu shows how it goes, opens the tracker website, the settings, and starts it with Windows.
/// </summary>
sealed class TrayApp : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "HaulingTrackerSender";

    readonly NotifyIcon _icon = new();
    readonly ToolStripMenuItem _status = new() { Enabled = false };
    readonly ToolStripMenuItem _startWithWindows = new("Start with Windows") { CheckOnClick = true };
    readonly CancellationTokenSource _stop = new();

    SenderSettings _settings = SenderSettings.Load();
    HttpLogServer? _server;
    LogSender? _sender;
    string? _player;
    bool _updating;

    public TrayApp()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open the tracker page", null, (_, _) => Open(_settings.ServerUrl));
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add(_startWithWindows);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _startWithWindows.Checked = StartsWithWindows();
        _startWithWindows.CheckedChanged += (_, _) => SetStartWithWindows(_startWithWindows.Checked);

        _icon.Icon = AppIcon();
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowSettings();
        _icon.Visible = true;
        SetStatus("Starting...");

        if (!_settings.Complete) ShowSettings();
        _ = SendLoopAsync();
        _ = UpdateLoopAsync();
    }

    static Icon AppIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("icon.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    void SetStatus(string text)
    {
        _status.Text = text;
        var tip = "Hauling Tracker: " + text;
        _icon.Text = tip.Length > 120 ? tip[..120] : tip;
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    void ShowSettings()
    {
        using var form = new SettingsForm(_settings.Copy());
        if (form.ShowDialog() != DialogResult.OK) return;
        _settings = form.Settings;
        _settings.Save();
        Reconnect();
    }

    /// <summary>New settings: a new connection, and the logs are followed again from where the server is.</summary>
    void Reconnect()
    {
        _server?.Dispose();
        _server = null;
        _sender = null;
        _player = null;
    }

    async Task SendLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await SendOnceAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(3), _stop.Token); }
            catch (TaskCanceledException) { return; }
        }
    }

    async Task SendOnceAsync()
    {
        if (!_settings.Complete)
        {
            SetStatus("Not set up: open Settings and type your key");
            return;
        }
        try
        {
            if (_server == null || _sender == null)
            {
                _server = new HttpLogServer(_settings.ServerUrl.Trim(), _settings.Key.Trim());
                _sender = new LogSender(_server) { Path = _settings.StarCitizenFolder.Trim(), Channel = _settings.Channel };
            }
            if (_player == null)
            {
                _player = await _server.PlayerAsync(_stop.Token);
                if (_player == null)
                {
                    SetStatus("The server does not know this key: check it in Settings");
                    return;
                }
            }
            await _sender.TickAsync(_stop.Token);
            if (_sender.LiveFile == null)
            {
                SetStatus("No Game.log found in " + _settings.StarCitizenFolder);
                return;
            }
            var last = _sender.LastUpload.HasValue ? _sender.LastUpload.Value.ToLocalTime().ToString("HH:mm:ss") : "nothing new yet";
            SetStatus("Sending as " + _player + ", last upload " + last + " (" + _sender.LinesSent.ToString("N0") + " lines)");
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (UnauthorizedAccessException)
        {
            _player = null;
            SetStatus("The server does not know this key: check it in Settings");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            SetStatus("Cannot reach the server, trying again (" + ex.Message + ")");
        }
        catch (IOException ex)
        {
            SetStatus("Cannot read the log, trying again (" + ex.Message + ")");
        }
    }

    /// <summary>Looks for a newer tracker at start and every six hours, and puts it in place (a build made on your own computer never updates).</summary>
    async Task UpdateLoopAsync()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), _stop.Token); }
        catch (TaskCanceledException) { return; }
        while (!_stop.IsCancellationRequested)
        {
            await TryUpdateAsync();
            try { await Task.Delay(TimeSpan.FromHours(6), _stop.Token); }
            catch (TaskCanceledException) { return; }
        }
    }

    async Task TryUpdateAsync()
    {
        var own = Updater.OwnCommit;
        if (own == null || _updating) return;
        _updating = true;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            cts.CancelAfter(TimeSpan.FromMinutes(2));
            var latest = await Updater.LatestCommitAsync(cts.Token);
            if (latest == null || !await Updater.IsNewerAsync(own, latest, cts.Token)) return;
            var exe = await Updater.DownloadAsync("HaulingTrackerSender.zip", "HaulingTrackerSender.exe");
            Updater.ReplaceAndStart(exe);   // the new tracker waits for this one to close
            ExitThread();
        }
        catch (Exception)
        {
            // no update this time: the tracker goes on as it is, and tries again later
        }
        finally
        {
            _updating = false;
        }
    }

    static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) is string;
    }

    static void SetStartWithWindows(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on && Environment.ProcessPath is { } exe) key.SetValue(RunName, "\"" + exe + "\"");
        else key.DeleteValue(RunName, false);
    }

    protected override void ExitThreadCore()
    {
        _stop.Cancel();
        _icon.Visible = false;
        _icon.Dispose();
        _server?.Dispose();
        base.ExitThreadCore();
    }
}
