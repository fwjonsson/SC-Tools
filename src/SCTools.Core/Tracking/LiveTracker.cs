using System.Globalization;

namespace SCTools.Tracking;

/// <summary>
/// The tracker on the server: the logs the trackers uploaded are worked out again whenever something new came in (the same steps as the program: the
/// sessions, the newest one shown, its stops, the pickups the plan credits, the runs and the payouts), the team status line moves on, and data.json for the page
/// is ready. Everything happens under the uploads' lock, so a piece of a log that comes in meanwhile waits.
/// </summary>
public sealed class LiveTracker
{
    readonly WebSnapshotOptions _view;
    readonly string _run = Guid.NewGuid().ToString("N")[..8];   // this run of the server: an ETag from the run before never matches by chance
    int _version = -1;
    int _saves;   // how many times the tracker's data was saved (a change on a page, the plan crediting a pickup, a payout...)
    int _builtSaves = -1;
    int _builtStatusSeq = -1;
    string _builtStatusText = "";
    JobBook? _builtJobs;
    IReadOnlyDictionary<string, WorkerPosition>? _builtPositions;   // the map's places data.json was made with (PlannerPositions on the server)
    DateTime _builtAt = DateTime.MinValue;
    long _builds;
    volatile LiveSnapshot _snapshot = LiveSnapshot.Empty;

    /// <summary>
    /// data.json is made again at least this often, so the few things in it that follow the clock alone (an aUEC/h, a payout that settles) do not fall behind;
    /// the clocks on the page count up by themselves between two makings.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    /// <param name="followParty">the logs decide who of the party is active (AutoParty), instead of the Active ticks</param>
    /// <param name="followSessions">the logs start and stop the sessions (AutoSessions), instead of the Start / Stop Session button</param>
    public LiveTracker(AppData data, Action<AppData>? save, LogUploads uploads, WebSnapshotOptions? view = null, bool followParty = false, bool followSessions = false)
    {
        Uploads = uploads;
        var inner = save ?? SCTools.Persistence.Storage.Save;
        Tracker = new Tracker(data, d => { _saves++; inner(d); });   // every save is counted: data.json is made again after one
        Tracker.State.Live = true;   // the logs are followed while the games write them: the clocks run
        _view = view ?? new WebSnapshotOptions();
        FollowParty = followParty;
        FollowSessions = followSessions;
    }

    public Tracker Tracker { get; }
    public LogUploads Uploads { get; }

    /// <summary>The logs decide who of the party is active: the in-game party and the trackers (see AutoParty).</summary>
    public bool FollowParty { get; }

    /// <summary>The logs start and stop the sessions (see AutoSessions): one session for the party, the hauling of everybody in it.</summary>
    public bool FollowSessions { get; }

    /// <summary>data.json as it was made last (see Refresh).</summary>
    public string Json => _snapshot.Json;

    /// <summary>data.json as it was made last, with its bytes and its ETag (see Refresh).</summary>
    public LiveSnapshot Snapshot => _snapshot;

    /// <summary>How many times data.json was made (for the server's log: a tick that makes it is the work, a tick that does not is nearly free).</summary>
    public long Builds => _builds;

    /// <summary>
    /// Works everything out again when something new was uploaded, moves the status line on, and makes data.json anew when anything in it can have changed: a
    /// new piece of log, somebody coming or going, a session starting or stopping, a save (a change on a page, the plan), the status line, the book of hauling
    /// jobs, the map's places for the planner, or half a minute gone by (MaxAge). Most ticks change nothing: then data.json stays as it is, and the page, which sends its ETag back, gets 304.
    /// </summary>
    public void Refresh(DateTime nowUtc)
    {
        lock (Uploads.Sync)
        {
            var changed = false;
            var version = Uploads.Version;
            if (version != _version)
            {
                _version = version;
                Show();
                changed = true;
            }
            if (FollowParty && Party(nowUtc))   // somebody came or went: the payouts and the runs are worked out again
            {
                Show();
                changed = true;
            }
            if (FollowSessions && Sessions(nowUtc))   // a session started or stopped: the newest is shown
            {
                Show();
                changed = true;
            }
            Tracker.Status.Advance(nowUtc);
            var status = Tracker.Status;
            if (!changed)
            {
                changed = _saves != _builtSaves || status.Seq != _builtStatusSeq || status.Text != _builtStatusText
                    || !ReferenceEquals(Tracker.State.Jobs, _builtJobs) || !ReferenceEquals(Tracker.State.Positions, _builtPositions) || nowUtc - _builtAt >= MaxAge;
            }
            if (changed) Build(nowUtc);
        }
    }

    /// <summary>
    /// Makes data.json (under the lock). Making it can save too (the plan credits a pickup, a location is worked out): that is in the data.json just made, so the
    /// saves are counted after it and are no reason to make it again.
    /// </summary>
    void Build(DateTime nowUtc)
    {
        var json = WebSnapshot.Json(Tracker, _view);
        _builds++;
        _builtSaves = _saves;
        _builtStatusSeq = Tracker.Status.Seq;
        _builtStatusText = Tracker.Status.Text;
        _builtJobs = Tracker.State.Jobs;
        _builtPositions = Tracker.State.Positions;
        _builtAt = nowUtc;
        _snapshot = new LiveSnapshot(json, "\"" + _run + "-" + _builds.ToString(CultureInfo.InvariantCulture) + "\"");
    }

    /// <summary>Reads something of the tracker for a page, under the lock (so nothing changes it meanwhile).</summary>
    public T Read<T>(Func<Tracker, T> read)
    {
        lock (Uploads.Sync) return read(Tracker);
    }

    /// <summary>
    /// A change made on a page (the party, a location, a colour...): made under the lock, saved, and everything is worked out again at once, so the next
    /// data.json already shows it.
    /// </summary>
    public void Edit(Action<Tracker> change, DateTime nowUtc)
    {
        lock (Uploads.Sync)
        {
            change(Tracker);
            Tracker.State.Save();
            Tracker.Plan.Invalidate();
            _version = -1;
            Refresh(nowUtc);
        }
    }

    /// <summary>
    /// Who of the party is active, from the logs (every time: a game that stopped writing its log ends after a while, with nothing new uploaded). True when
    /// that changed anything, which is then saved.
    /// </summary>
    bool Party(DateTime nowUtc)
    {
        var data = Tracker.State.Data;
        var parties = AutoParty.Work(Uploads.Logs, data.Party, nowUtc);
        // the party follows the game's parties (Frank, 29 Oct 2026): whoever the logs show in an in-game party with a member is a member too, with the
        // settings every member has, so nobody is added by hand (a player alone, or in a party of strangers, is not: he does not haul with the party)
        var joined = false;
        foreach (var g in parties.Groups.Where(g => g.Names.Any(n => data.Party.Any(m => PlayerNames.SameName(m, n)))))
        {
            foreach (var n in g.Names.Where(n => !data.Party.Any(m => PlayerNames.SameName(m, n))).ToList())
            {
                if (!Tracker.Crew.Join(n)) continue;
                if (data.AutoPartySince != null) Tracker.Crew.SetMemberActive(n, true, nowUtc, PartyChange.BackToLogs);   // the logs say whether he plays
                joined = true;
            }
        }
        if (joined) parties = AutoParty.Work(Uploads.Logs, data.Party, nowUtc);   // with the newcomers as members: when they were active
        Tracker.State.Parties = parties;
        if (!AutoParty.Apply(data, parties, nowUtc) && !joined) return false;
        Tracker.State.Save();
        Tracker.Plan.Invalidate();
        return true;
    }

    /// <summary>
    /// The sessions from the logs (every time: a session stops an hour after the last thing that happened, with nothing new uploaded). True when that changed
    /// anything, which is then saved.
    /// </summary>
    bool Sessions(DateTime nowUtc)
    {
        if (Tracker.State.Log is not { } log) return false;
        var (starts, activity, going) = AutoSessions.HaulingActivity(log, nowUtc);
        if (!AutoSessions.Apply(Tracker.State.Data.ManualSessions, starts, activity, nowUtc, going)) return false;
        Tracker.State.Save();
        Tracker.Plan.Invalidate();
        return true;
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
            Earnings.FillDefaults(state.Inputs!, state.Data);   // nothing typed: the rewards typed last, or the built-in ones
            t.LoadRuns();
        }
        t.Payouts.UpdateSnapshots();
    }
}
