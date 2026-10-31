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
    long _autoPieces = -1;
    DateTime _autoAt = DateTime.MinValue;
    PartyBook? _book;   // what the party tracker said last, for the pieces of log and the members it was worked out with
    long _bookPieces = -1;
    string _bookMembers = "";
    LocationBook? _locations;   // what the location tracker said last, for the logs, the planner's state, the parties and the places it was worked out with
    string _locationsKey = "";
    Atlas? _locationsAtlas;
    PartyBook? _locationsParties;
    string _plannerKey = "";   // what the plan was made with of the location tracker's answer (LocationBook.PlannerKey)

    /// <summary>The party and the sessions are worked out from the logs again at least this often when nothing came in (else at every piece that does).</summary>
    public static readonly TimeSpan AutoEvery = TimeSpan.FromSeconds(30);
    int _saves;   // how many times the tracker's data was saved (a change on a page, the plan crediting a pickup, a payout...)
    int _builtSaves = -1;
    JobBook? _builtJobs;
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
        var inner = save ?? (_ => { });   // no save given (the tests): nothing is written
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

    /// <summary>
    /// Everybody the site knows (the server's PartyService.Players: the Hauling Planner's party, the tracker keys, the fleets, the logs), whose activity the
    /// party tracker works out and whose names it writes as they are written there; without it, the Hauling Planner's party and the players of the logs.
    /// </summary>
    public Func<IReadOnlyCollection<string>>? Players { get; set; }

    /// <summary>The places the location tracker reads the logs with (the server's: the game data and the admin's Map places); the game data's alone when null.</summary>
    public Func<Atlas>? AtlasSource { get; set; }

    /// <summary>
    /// What the location tracker says now (see LocationTracker): worked out again when a piece of log came in, the logs were worked out anew, the data was
    /// saved (the planner's pickups and deliveries are clues), the party tracker or the places changed, or half a minute after the last time (a game that
    /// stopped writing its log ends after a while; a mission's beacon gets old); else the same answer, so every page and the refresh read one result.
    /// </summary>
    public LocationBook Locations(DateTime nowUtc)
    {
        lock (Uploads.Sync) return Where(nowUtc);
    }

    LocationBook Where(DateTime nowUtc)
    {
        var atlas = AtlasSource?.Invoke() ?? Atlas.Default;
        var parties = Book(nowUtc, false);
        var key = string.Join("|", Uploads.Pieces, _version, _saves, string.Join(",", Tracker.State.Data.Party));
        if (_locations == null || key != _locationsKey || !ReferenceEquals(atlas, _locationsAtlas) || !ReferenceEquals(parties, _locationsParties)
            || nowUtc - _locations.Now >= AutoEvery || nowUtc < _locations.Now)
        {
            _locations = LocationTracker.Work(Uploads.Logs, Uploads.Games(), Uploads.MissionsSeen(), atlas, nowUtc, parties, Clue);
            _locationsKey = key;
            _locationsAtlas = atlas;
            _locationsParties = parties;
        }
        return _locations;
    }

    /// <summary>The Hauling Planner's own reading of where a member of its party is (Whereabouts.OwnLocationOf, LastMoveOf); null for anybody else.</summary>
    PlannerClue? Clue(string name) =>
        Tracker.State.Data.Party.Any(n => PlayerNames.SameName(n, name))
            ? new PlannerClue(Tracker.Whereabouts.OwnLocationOf(name), Tracker.Whereabouts.LastMoveOf(name)) : null;

    /// <summary>
    /// What the party tracker says now (see PartyTracker): worked out again when a piece of log came in, when the members changed, or half a minute after the
    /// last time (a game that stopped writing its log ends after a while); else the same answer, so every page and the refresh read one result.
    /// </summary>
    public PartyBook Parties(DateTime nowUtc)
    {
        lock (Uploads.Sync) return Book(nowUtc, false);
    }

    PartyBook Book(DateTime nowUtc, bool fresh)
    {
        var members = Members();
        var key = string.Join("|", members);
        var pieces = Uploads.Pieces;
        if (fresh || _book == null || pieces != _bookPieces || key != _bookMembers || nowUtc - _book.Now >= AutoEvery || nowUtc < _book.Now)
        {
            _book = PartyTracker.Work(Uploads.Logs, members, nowUtc);
            _bookPieces = pieces;
            _bookMembers = key;
        }
        return _book;
    }

    IReadOnlyCollection<string> Members()
    {
        if (Players != null) return Players();
        var names = new List<string>(Tracker.State.Data.Party);
        foreach (var log in Uploads.Logs)
        {
            var who = log.Player ?? log.Uploader;
            if (!string.IsNullOrWhiteSpace(who) && !names.Any(n => PlayerNames.SameName(n, who))) names.Add(who);
        }
        return names;
    }

    /// <summary>data.json as it was made last (see Refresh).</summary>
    public string Json => _snapshot.Json;

    /// <summary>data.json as it was made last, with its bytes and its ETag (see Refresh).</summary>
    public LiveSnapshot Snapshot => _snapshot;

    /// <summary>How many times data.json was made (for the server's log: a tick that makes it is the work, a tick that does not is nearly free).</summary>
    public long Builds => _builds;

    /// <summary>
    /// Works everything out again when something new was uploaded, moves the status line on, and makes data.json anew when anything in it can have changed: a
    /// new piece of log, somebody coming or going, a session starting or stopping, a save (a change on a page, the plan), the status line, the book of hauling
    /// jobs, where the location tracker has the workers (only a change of what the planner takes from it makes the plan anew), or half a minute gone by (MaxAge). Most ticks change nothing: then data.json stays as it is, and the page, which sends its ETag back, gets 304.
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
                if (FollowParty) Tracker.State.Parties = Book(nowUtc, false);   // the payouts of the session shown ask it who was in the party
                Show();
                changed = true;
            }
            var pieces = Uploads.Pieces;
            var auto = changed || pieces != _autoPieces || nowUtc - _autoAt >= AutoEvery || nowUtc < _autoAt;
            if (auto)
            {
                _autoPieces = pieces;
                _autoAt = nowUtc;
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
            }
            // where everybody is (after the logs are worked out: the planner's pickups and deliveries are clues); the plan starts where it has him
            var locations = Where(nowUtc);
            Tracker.State.Locations = locations;
            if (locations.PlannerKey != _plannerKey)
            {
                _plannerKey = locations.PlannerKey;
                Tracker.Plan.Invalidate();
                changed = true;
            }
            Tracker.Status.Advance(nowUtc);   // the status line has an address of its own (/hauling/status.json): it is no reason to make data.json again
            if (!changed)
            {
                changed = _saves != _builtSaves || !ReferenceEquals(Tracker.State.Jobs, _builtJobs) || nowUtc - _builtAt >= MaxAge;
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
        _builtJobs = Tracker.State.Jobs;
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
        var parties = Book(nowUtc, false);
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
        if (joined) parties = Book(nowUtc, true);   // with the newcomers as members: when they were active
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
