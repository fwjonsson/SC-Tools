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
        Earnings = new Earnings(State);
        DropOffs = new DropOffs(State, Places, Crew, Contracts);
        Runs = new CargoRuns(State, Places, Crew, Earnings);
        Whereabouts = new Whereabouts(State, Runs, DropOffs);
        Figures = new SessionFigures(State);
        Payouts = new Payouts(State, Crew, Contracts, Earnings, Runs, Places);
        Leaderboard = new Leaderboard(State, Places, Payouts, Figures);
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
}
