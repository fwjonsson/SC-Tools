using System.Diagnostics;
using System.Net.Http;
using Microsoft.Win32;
using SCTools.GameLog;
using SCTools.Updates;

namespace SCTools.Tracker;

/// <summary>
/// The SC Tools Tracker in the tray: every three seconds it uploads what is new in the Game.log (and the backup logs of the last day) to the server, it sends
/// the old backup logs the server does not have yet (HistorySender: all of them the first time, then the new ones), and it updates itself from the latest
/// release on GitHub. The menu shows how each goes and its version, opens the site, the settings, and starts it with Windows.
/// </summary>
sealed class TrayApp : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "SCToolsTracker";
    const string OldRunName = "HaulingTrackerSender";

    readonly NotifyIcon _icon = new();
    readonly ToolStripMenuItem _status = new() { Enabled = false };
    readonly ToolStripMenuItem _history = new("Old logs: not looked yet") { Enabled = false };
    readonly ToolStripMenuItem _version = new() { Enabled = false };
    readonly ToolStripMenuItem _startWithWindows = new("Start with Windows") { CheckOnClick = true };
    readonly CancellationTokenSource _stop = new();

    SenderSettings _settings = SenderSettings.Load();
    HttpLogServer? _server;
    LogSender? _sender;
    string? _player;
    bool _updating;
    readonly RetryDelay _delay = new();   // how long until the next upload: longer and longer while the server cannot be reached
    CancellationTokenSource _wake = new();   // cancelled by new settings: the next upload is at once, however long the wait was

    /// <param name="updated">it was started by an update of itself: it says so</param>
    public TrayApp(bool updated = false)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(_history);
        menu.Items.Add(_version);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open SC Tools", null, (_, _) => Open(_settings.ServerUrl.Trim().TrimEnd('/') + "/"));
        menu.Items.Add("Open my page (location, colour, ship)", null, async (_, _) => await OpenMyPageAsync());
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add("Check for an update now", null, async (_, _) => await TryUpdateAsync(true));
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
        _version.Text = "Version " + Version;
        if (updated) _icon.ShowBalloonTip(5000, AppName.Tracker.Title, "Updated to version " + Version + ".", ToolTipIcon.Info);

        if (!_settings.Complete) ShowSettings();
        _ = SendLoopAsync();
        _ = HistoryLoopAsync();
        _ = UpdateLoopAsync();
    }

    /// <summary>The first letters of the commit this tracker was built from ("own build" for one made on your own computer).</summary>
    static string Version => Updater.OwnCommit is { } c ? c[..7] : "own build";

    static Icon AppIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("icon.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    void SetStatus(string text)
    {
        _status.Text = text;
        var tip = AppName.Tracker.Title + ": " + text;
        _icon.Text = tip.Length > 120 ? tip[..120] : tip;
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    /// <summary>My page on the website, logged in as this tracker's player (a link from the server that works once).</summary>
    async Task OpenMyPageAsync()
    {
        if (!_settings.Complete)
        {
            ShowSettings();
            return;
        }
        try
        {
            using var server = new HttpLogServer(_settings.ServerUrl.Trim(), _settings.Key.Trim());
            var link = await server.LoginLinkAsync(_stop.Token);
            if (link == null) _icon.ShowBalloonTip(5000, AppName.Tracker.Title, "The server does not know this key: check it in Settings.", ToolTipIcon.Warning);
            else Open(link);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            _icon.ShowBalloonTip(5000, AppName.Tracker.Title, "The server could not be reached: " + ex.Message, ToolTipIcon.Warning);
        }
    }

    void ShowSettings()
    {
        using var form = new SettingsForm(_settings.Copy());
        if (form.ShowDialog() != DialogResult.OK) return;
        _settings = form.Settings;
        _settings.Save();
        Reconnect();
    }

    /// <summary>New settings: a new connection, and the logs are followed again from where the server is, at once (not when the wait after a failure ends).</summary>
    void Reconnect()
    {
        _server?.Dispose();
        _server = null;
        _sender = null;
        _player = null;
        _delay.Worked();
        var wake = _wake;
        _wake = new CancellationTokenSource();
        wake.Cancel();
        wake.Dispose();
    }

    /// <summary>
    /// Uploads what is new, then waits: three seconds, or longer after failures in a row (RetryDelay), so a server that is down or being updated is not asked
    /// every three seconds by every tracker.
    /// </summary>
    async Task SendLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await SendOnceAsync();
            using var wake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, _wake.Token);
            // while the game is closed (its Game.log has not grown for a few minutes) it looks every 15 seconds, and every 3 again at its first new line
            var wait = _sender == null ? _delay.Next : _delay.NextFor(_sender.QuietFor(DateTime.UtcNow));
            try { await Task.Delay(wait, wake.Token); }
            catch (TaskCanceledException) { if (_stop.IsCancellationRequested) return; }
        }
    }

    async Task SendOnceAsync()
    {
        if (!_settings.Complete)
        {
            _delay.Worked();
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
                    _delay.KeyUnknown();
                    SetStatus("The server does not know this key: check it in Settings (asked again in " + RetryDelay.Say(_delay.Next) + ")");
                    return;
                }
            }
            var sender = _sender;
            await Task.Run(() => sender.TickAsync(_stop.Token));   // off the tray's thread: a slow disk or network folder never freezes the menu
            _delay.Worked();
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
            _delay.KeyUnknown();
            SetStatus("The server does not know this key: check it in Settings (asked again in " + RetryDelay.Say(_delay.Next) + ")");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _delay.Failed();
            SetStatus("Cannot reach the server, trying again in " + RetryDelay.Say(_delay.Next) + " (" + ex.Message + ")");
        }
        catch (IOException ex)
        {
            _delay.Worked();   // the log, not the server: soon again
            SetStatus("Cannot read the log, trying again (" + ex.Message + ")");
        }
    }

    /// <summary>
    /// The old logs: once the server knows the key, every backup log it does not have is sent (all of them the first time, then only the new ones), and
    /// again every six hours. It runs beside the uploads of the Game.log, off the window's thread, and the menu says how far it is.
    /// </summary>
    async Task HistoryLoopAsync()
    {
        var ui = SynchronizationContext.Current;
        void Show(string text)
        {
            if (ui == null) _history.Text = text;
            else ui.Post(_ => _history.Text = text, null);
        }
        while (!_stop.IsCancellationRequested)
        {
            if (_player == null || !_settings.Complete)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token); }
                catch (TaskCanceledException) { return; }
                continue;
            }
            var settings = _settings;
            try
            {
                await Task.Run(async () =>
                {
                    using var server = new HttpLogServer(settings.ServerUrl.Trim(), settings.Key.Trim());
                    var sender = new HistorySender(server) { Path = settings.StarCitizenFolder.Trim(), Channel = settings.Channel };
                    Show("Old logs: looking which the server has...");
                    sender.Progress = () => Show("Old logs: sending " + (sender.Sent + 1) + " of " + sender.Missing + "...");
                    await sender.RunAsync(_stop.Token);
                    Show(sender.Found == null ? "Old logs: no Game.log found"
                        : "Old logs: all on the server (" + sender.Found + (sender.Missing > 0 ? ", " + sender.Missing + " sent now" : "") + ")");
                }, _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Show("Old logs: trying again later (" + ex.Message + ")");
                try { await Task.Delay(TimeSpan.FromMinutes(10), _stop.Token); }
                catch (TaskCanceledException) { return; }
                continue;
            }
            try { await Task.Delay(TimeSpan.FromHours(6), _stop.Token); }
            catch (TaskCanceledException) { return; }
        }
    }

    /// <summary>How often a running tracker looks for a newer one: a new kind of line the site reads only comes from trackers that updated.</summary>
    static readonly TimeSpan UpdateEvery = TimeSpan.FromMinutes(30);

    /// <summary>Looks for a newer tracker at start and every half hour, and puts it in place (a build made on your own computer never updates).</summary>
    async Task UpdateLoopAsync()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), _stop.Token); }
        catch (TaskCanceledException) { return; }
        while (!_stop.IsCancellationRequested)
        {
            await TryUpdateAsync();
            try { await Task.Delay(UpdateEvery, _stop.Token); }
            catch (TaskCanceledException) { return; }
        }
    }

    /// <param name="asked">from the menu: it says what it found in a balloon too</param>
    async Task TryUpdateAsync(bool asked = false)
    {
        var own = Updater.OwnCommit;
        if (own == null)
        {
            _version.Text = "Version " + Version + " (built on this computer: it does not update itself)";
            if (asked) _icon.ShowBalloonTip(5000, AppName.Tracker.Title, "This tracker was built on this computer: it does not update itself.", ToolTipIcon.Info);
            return;
        }
        if (_updating) return;
        _updating = true;
        try
        {
            _version.Text = "Version " + Version + ", looking for an update...";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            cts.CancelAfter(TimeSpan.FromMinutes(2));
            var latest = await Updater.LatestCommitAsync(cts.Token);
            if (latest == null || !await Updater.IsNewerAsync(own, latest, cts.Token))
            {
                _version.Text = "Version " + Version + (latest == null ? ", no answer from GitHub " : ", up to date ") + "(" + DateTime.Now.ToString("HH:mm") + ")";
                if (asked) _icon.ShowBalloonTip(5000, AppName.Tracker.Title, latest == null ? "GitHub did not answer: try again later." : "You have the newest version (" + Version + ").", ToolTipIcon.Info);
                return;
            }
            _version.Text = "Version " + Version + ", updating to " + latest[..7] + "...";
            var exe = await Updater.DownloadAsync(AppName.Tracker.Zip, AppName.Tracker.Exe);
            try
            {
                Updater.ReplaceAndStart(exe, "--updated");   // the new tracker waits for this one to close, and says it was updated
            }
            catch
            {
                Updater.Discard(exe);   // not put in place (the exe was locked): the 45 MB download does not stay in Temp
                throw;
            }
            ExitThread();
        }
        catch (Exception ex)
        {
            // no update this time: the tracker goes on as it is, and tries again later
            _version.Text = "Version " + Version + ", the update did not work (" + ex.Message + ")";
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

    /// <summary>The tracker got its new name (Program): Start with Windows, when it was on, starts the new exe.</summary>
    public static void MoveStartWithWindows(string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (key.GetValue(OldRunName) is not string) return;
        key.DeleteValue(OldRunName, false);
        key.SetValue(RunName, "\"" + exe + "\"");
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
