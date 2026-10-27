namespace HaulingTracker.Tracking;

/// <summary>
/// The state of the tracker and the services that work from it, made once and wired together. The window keeps one; a test makes its own with the data it needs
/// (and a save that writes nothing).
/// </summary>
public sealed class Tracker
{
    /// <param name="data">The saved data.</param>
    /// <param name="save">How the saved data is written (the data file; a test passes something else).</param>
    public Tracker(AppData data, Action<AppData>? save = null)
    {
        State = new TrackerState(data, save);
        Places = new Places(State);
        Crew = new Crew(State);
        Contracts = new Contracts(State, Places, Crew);
        Earnings = new Earnings(State, Places);
        DropOffs = new DropOffs(State, Places, Crew, Contracts);
        Runs = new CargoRuns(State, Places, Crew, Earnings);
        Whereabouts = new Whereabouts(State, Runs, DropOffs);
        Figures = new SessionFigures(State);
        Payouts = new Payouts(State, Crew, Contracts, Earnings, Runs, Places);
        Leaderboard = new Leaderboard(State, Places, Payouts, Figures);
        Plan = new PlanService(State, Places, Crew, Contracts, Earnings, DropOffs, Runs, Whereabouts);
        Status = new StatusFeed(State, Places, Crew, Earnings, Runs);
    }

    public TrackerState State { get; }
    public Places Places { get; }
    public Crew Crew { get; }
    public Contracts Contracts { get; }
    public Earnings Earnings { get; }
    public DropOffs DropOffs { get; }
    public CargoRuns Runs { get; }
    public Whereabouts Whereabouts { get; }
    public SessionFigures Figures { get; }
    public Payouts Payouts { get; }
    public Leaderboard Leaderboard { get; }
    public PlanService Plan { get; }
    public StatusFeed Status { get; }

    /// <summary>
    /// Reads the logs of every player as one (a mission shared in the party is in it once), with the missions marked as gone and the SCU you typed, and finds the
    /// sessions in it.
    /// </summary>
    public void Load(IEnumerable<HaulingLog> logs)
    {
        State.Log = HaulingLog.Merge(logs);
        Contracts.ApplyDroppedMissions(State.Log);
        DropOffs.ApplyTypedScu(State.Log);
        State.Sessions = SessionBuilder.Build(State.Log, State.Data.ManualSessions);
    }

    /// <summary>Shows a session, with what was typed for it. A new set of missions clears the workers assigned to the stops (and that is saved).</summary>
    public void Select(SessionGroup g)
    {
        State.Current = g;
        State.Inputs = State.InputsOf(g.Key);
        if (WorkerTripBuilder.ResetWorkersForNewBatch(g, State.Inputs, State.Missing)) State.Save();   // new missions: a clean slate for the assigned workers
    }

    /// <summary>
    /// Works out the stops of the session shown: the missions whose cargo went missing, the summary, and the pickup locations. Both boards only show the missions
    /// that are active (not completed); a location where every batch of those missions is handed in moves from the pickup table to the finished stops table.
    /// A completed mission is on neither: the Cargo Runs keep everything since the session started.
    /// </summary>
    public void LoadStops()
    {
        var g = State.Current ?? throw new InvalidOperationException("No session is shown.");
        State.Missing = State.Log == null ? new HashSet<string>() : SessionBuilder.MissingCargo(g, Runs.BuildRuns(g, State.Inputs!), State.Log.LastTime);
        State.Summary = SessionBuilder.Summarize(g);
        State.Rows = SessionBuilder.Locations(g);
        State.OpenRows = SessionBuilder.Locations(g, true, State.Missing);
        State.PickupRows = State.OpenRows.Where(r => !r.DeliveredTime.HasValue).ToList();
        State.DoneRows = State.OpenRows.Where(r => r.DeliveredTime.HasValue).ToList();
    }

    /// <summary>Works out the runs of the session shown, latest activity first (none while no session is shown).</summary>
    public void LoadRuns() => State.Trips = Runs.SessionRuns();

    /// <summary>
    /// Works out the session shown for these players only, for the party tabs of the page: the missions one of them has by his own log (the to-Ruin and the
    /// from-Ruin ones; a player without a log is seen with the missions of those he plays with), the stops of those, and their runs. The session as it was
    /// comes back when the result is disposed.
    /// </summary>
    public IDisposable Focus(IReadOnlyCollection<string> names)
    {
        var s = State;
        var saved = (s.Current, s.Summary, s.Rows, s.OpenRows, s.PickupRows, s.DoneRows, s.Trips, s.Missing, s.Focus);
        if (s.Current != null && s.Inputs != null)
        {
            var g = s.Current;
            var logged = names.Where(n => s.Log?.Players.Contains(n) == true).ToList();
            bool Theirs(Mission m) => logged.Any(n => Contracts.HasMission(m, n));
            s.Focus = Theirs;
            s.Current = new SessionGroup
            {
                Missions = g.Missions.Where(Theirs).ToList(),
                Failed = g.Failed.Where(Theirs).ToList(),
                Start = g.Start,
                End = g.End,
                Manual = g.Manual,
                Stop = g.Stop,
                CustomKey = g.Key,   // the same session: what was typed for it
            };
            LoadStops();
            LoadRuns();
            s.Trips = s.Trips.Where(t => names.Any(n => PlayerNames.SameName(n, t.Worker))).ToList();
        }
        return new Restore(() => (s.Current, s.Summary, s.Rows, s.OpenRows, s.PickupRows, s.DoneRows, s.Trips, s.Missing, s.Focus) = saved);
    }

    sealed class Restore : IDisposable
    {
        readonly Action _back;
        public Restore(Action back) => _back = back;
        public void Dispose() => _back();
    }

    /// <summary>No session is shown: nothing typed, no stops.</summary>
    public void ClearSession()
    {
        State.Missing = new HashSet<string>();
        State.Inputs = null;
        State.Summary = null;
        State.Rows = new List<LocationRow>();
        State.OpenRows = new List<LocationRow>();
        State.PickupRows = new List<LocationRow>();
        State.DoneRows = new List<LocationRow>();
    }
}
