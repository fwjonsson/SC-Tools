namespace HaulingTracker.Tracking;

/// <summary>
/// What the program knows right now: the saved data, the merged log of every player, the sessions found in it, and the session that is shown with
/// its stops and runs. The window fills it from the logs; the services (Places, Crew, Rewards, Runs, ...) work from it, so they need no window
/// and can be tested.
/// </summary>
public sealed class TrackerState
{
    readonly Action<AppData> _save;

    /// <param name="data">The saved data.</param>
    /// <param name="save">How the saved data is written (the data file; a test passes something else).</param>
    public TrackerState(AppData data, Action<AppData>? save = null)
    {
        Data = data;
        _save = save ?? Storage.Save;
    }

    /// <summary>The saved data: the party, ships, names, what was typed for every session, the debts...</summary>
    public AppData Data { get; }

    /// <summary>Writes the saved data to its file.</summary>
    public void Save() => _save(Data);

    /// <summary>The logs of all players merged into one (a mission shared in the party is in it once). Null while no log is read.</summary>
    public HaulingLog? Log { get; set; }

    /// <summary>The sessions found in the log, oldest first.</summary>
    public List<SessionGroup> Sessions { get; set; } = new();

    /// <summary>The session that is shown.</summary>
    public SessionGroup? Current { get; set; }

    /// <summary>What was typed for the session that is shown (payouts, lost cargo, notes...).</summary>
    public SessionInputs? Inputs { get; set; }

    /// <summary>The start, end and counts of the session that is shown.</summary>
    public SessionSummary? Summary { get; set; }

    /// <summary>Every pickup location of the session (the totals and the saved numbers use these).</summary>
    public List<LocationRow> Rows { get; set; } = new();

    /// <summary>The locations of the missions that are not completed yet (completed missions are on neither board).</summary>
    public List<LocationRow> OpenRows { get; set; } = new();

    /// <summary>Of the open locations, the ones with cargo still to deliver (the Stops board).</summary>
    public List<LocationRow> PickupRows { get; set; } = new();

    /// <summary>Of the open locations, the ones where every batch is handed in (the Finished Stops board).</summary>
    public List<LocationRow> DoneRows { get; set; } = new();

    /// <summary>The runs of the session, latest activity first.</summary>
    public List<WorkerTrip> Trips { get; set; } = new();

    /// <summary>Missions whose cargo is all handed in but that never completed (see SessionBuilder.MissingCargo).</summary>
    public HashSet<string> Missing { get; set; } = new();

    /// <summary>True while the running game's Game.log is watched.</summary>
    public bool Live { get; set; }

    /// <summary>True when the session that is shown is the newest one (the one that is going on).</summary>
    public bool IsLatest => Sessions.Count > 0 && Current != null && Current.Key == Sessions[^1].Key;

    /// <summary>
    /// True while the session has a contract that is not completed (abandoned and failed ones are not part of the session). A mission with missing cargo never
    /// completes, so it does not count.
    /// </summary>
    public bool HasOpenContracts() => Current != null && Current.Missions.Any(m => !m.Completed && !Missing.Contains(m.Id));

    /// <summary>True while the clocks of the runs and the session are counting: watching live, the newest session, with contracts still going.</summary>
    public bool ClocksRunning => Live && IsLatest && HasOpenContracts();

    /// <summary>What was typed for a session (made, empty, the first time it is asked for).</summary>
    public SessionInputs InputsOf(string key)
    {
        if (!Data.Sessions.TryGetValue(key, out var inputs))
        {
            inputs = new SessionInputs();
            Data.Sessions[key] = inputs;
        }
        return inputs;
    }

    /// <summary>What was typed for a pickup location of the session that is shown (made, empty, the first time it is asked for).</summary>
    public RowInputs RowOf(string zone)
    {
        var rows = Inputs!.Rows;
        if (!rows.TryGetValue(zone, out var r))
        {
            r = new RowInputs();
            rows[zone] = r;
        }
        return r;
    }
}
