namespace HaulingTracker.Tracking;

/// <summary>
/// The tracker on the server: the logs the trackers uploaded are worked out again whenever something new came in (the same steps as the program: the
/// sessions, the newest one shown, its stops, the pickups the plan credits, the runs and the payouts), the team status line moves on, and data.json for the page
/// is ready. Everything happens under the uploads' lock, so a piece of a log that comes in meanwhile waits.
/// </summary>
public sealed class LiveTracker
{
    readonly WebSnapshotOptions _view;
    int _version = -1;
    volatile string _json = "{}";

    public LiveTracker(AppData data, Action<AppData>? save, LogUploads uploads, WebSnapshotOptions? view = null)
    {
        Uploads = uploads;
        Tracker = new Tracker(data, save);
        Tracker.State.Live = true;   // the logs are followed while the games write them: the clocks run
        _view = view ?? new WebSnapshotOptions();
    }

    public Tracker Tracker { get; }
    public LogUploads Uploads { get; }

    /// <summary>data.json as it was made last (see Refresh).</summary>
    public string Json => _json;

    /// <summary>Works everything out again when something new was uploaded, moves the status line on, and makes data.json.</summary>
    public void Refresh(DateTime nowUtc)
    {
        lock (Uploads.Sync)
        {
            var version = Uploads.Version;
            if (version != _version)
            {
                _version = version;
                Show();
            }
            Tracker.Status.Advance(nowUtc);
            _json = WebSnapshot.Json(Tracker, _view);
        }
    }

    /// <summary>The newest session, as the program shows it when it follows the logs.</summary>
    void Show()
    {
        var t = Tracker;
        var state = t.State;
        t.Load(Uploads.Logs);
        var newest = state.Sessions.Count > 0 ? state.Sessions[^1] : null;
        if (newest == null)
        {
            t.ClearSession();
        }
        else
        {
            t.Select(newest);
            t.Plan.ClaimNewPickups();   // before the stops change: the plan still has the batches that were just picked up
            t.LoadStops();
            Earnings.FillDefaults(state.Inputs!);   // nothing typed: the built-in payouts
            t.LoadRuns();
        }
        t.Payouts.UpdateSnapshots();
    }
}
