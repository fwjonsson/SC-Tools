using System.Globalization;

namespace HaulingTracker;

// Start Session / Stop Session: a session you choose yourself. It reads the logs from the moment you press Start until you press Stop,
// also when that is days later, and the start is remembered when you close the program.
public sealed partial class MainForm
{
    readonly Button _startSessionBtn = new();
    readonly Button _stopSessionBtn = new();
    string? _forceKey;   // the session to show after the next refresh (the one you just started)

    ManualSession? OpenManualSession => _data.ManualSessions.LastOrDefault(s => !s.StopUtc.HasValue);

    void InitSessionButtons()
    {
        StyleButton(_startSessionBtn, "Start Session");
        StyleButton(_stopSessionBtn, "Stop Session");
        _startSessionBtn.Click += (_, _) => StartSession();
        _stopSessionBtn.Click += (_, _) => StopSession();
        UpdateSessionButtons();
    }

    void UpdateSessionButtons()
    {
        var open = OpenManualSession != null;
        _startSessionBtn.Text = open ? "Restart Session" : "Start Session";
        _stopSessionBtn.Enabled = open;
    }

    void StartSession()
    {
        var now = DateTime.UtcNow;
        var open = OpenManualSession;
        if (open != null)
        {
            var since = open.StartUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
            var answer = MessageBox.Show(this,
                "A session has been running since " + since + ".\n\nStop it now and start a new one from this moment?",
                "Restart Session", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            open.StopUtc = now;
        }

        _data.ManualSessions.Add(new ManualSession { StartUtc = now });
        Storage.Save(_data);
        _forceKey = "manual-" + now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        UpdateSessionButtons();
        _renderedVersion = -1;
        RefreshFromLog();
        SetStatus("Session started. Only what happens from now on is read, until you press Stop Session.");
    }

    void StopSession()
    {
        var open = OpenManualSession;
        if (open == null) return;
        open.StopUtc = DateTime.UtcNow;
        Storage.Save(_data);
        UpdateSessionButtons();
        _renderedVersion = -1;
        RefreshFromLog();
        SetStatus("Session stopped.");
    }
}
