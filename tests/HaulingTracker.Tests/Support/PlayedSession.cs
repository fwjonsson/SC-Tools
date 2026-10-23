namespace HaulingTracker.Tests.Support;

/// <summary>
/// One player's session, started by hand: a 2-stop mission picked up at Gaslight and Patch City and handed in at Ruin, then a second mission picked up at
/// Gaslight and still on the way. Loaded into a Tracker the way the window does it (Load, Select, LoadStops, LoadRuns).
/// </summary>
static class PlayedSession
{
    public static readonly string M1 = GameLogWriter.MissionId(31);
    public static readonly string M2 = GameLogWriter.MissionId(32);
    public const string GaslightZone = "900000003101";
    public const string PatchZone = "900000003102";
    public const string RuinZone = "900000003103";
    public const string Player = "Pilot_A";
    public const string Gaslight = "69936";     // the station ids (PyroDistances)
    public const string PatchCity = "70120";

    /// <summary>The time of the last pickup (the second mission at Gaslight).</summary>
    public static readonly DateTime LastPickup = GameLogWriter.Start + TimeSpan.FromMinutes(1 + 19 + 15 + 15 + 1 + 19) + TimeSpan.FromSeconds(5 + 5 + 5 + 1 + 5);

    /// <summary>The lines of the session; more can be written after them.</summary>
    public static GameLogWriter Writer()
    {
        var contract = string.Format(GameLogWriter.HaulingContract, 2);
        return new GameLogWriter().Login(Player)
            .Minutes(1).Accepted(M1)
            .Marker(M1, contract, "pickup_m1_0", GaslightZone, GameLogWriter.Gaslight)
            .Marker(M1, contract, "pickup_m1_1", PatchZone, GameLogWriter.PatchCity)
            .Marker(M1, contract, "dropoff_m1_0", RuinZone, GameLogWriter.RuinStation)
            // he takes the cargo out of the elevator at Gaslight, then at Patch City, a few seconds before the game completes the pickup
            .Minutes(19).Unstow().Seconds(5).Objective(M1, "pickup_m1_0", "COMPLETED")
            .Minutes(15).Unstow().Seconds(5).Objective(M1, "pickup_m1_1", "COMPLETED")
            // and hands it in at Ruin (the loading platform)
            .Minutes(15).LoadingPlatform().Seconds(5).Objective(M1, "dropoff_m1_0", "COMPLETED").Seconds(1).MissionEnded(M1, "COMPLETED")
            // a second mission, picked up at Gaslight, still on the way
            .Minutes(1).Accepted(M2)
            .Marker(M2, contract, "pickup_m2_0", GaslightZone, GameLogWriter.Gaslight)
            .Marker(M2, contract, "pickup_m2_1", PatchZone, GameLogWriter.PatchCity)
            .Marker(M2, contract, "dropoff_m2_0", RuinZone, GameLogWriter.RuinStation)
            .Minutes(19).Unstow().Seconds(5).Objective(M2, "pickup_m2_0", "COMPLETED");
    }

    /// <summary>The saved data of the session: the player in the party, the session started by hand, and the runs credited from the logs.</summary>
    public static AppData Data() => new()
    {
        Party = new List<string> { Player },
        ManualSessions = new List<ManualSession> { new() { StartUtc = GameLogWriter.Start } },
        PlanCreditSince = GameLogWriter.Start.AddDays(-1),   // who did a pickup comes from the logs
        PartyTrackingSince = GameLogWriter.Start.AddDays(-1),
    };

    /// <summary>A tracker with the session shown. Every save of the data is counted.</summary>
    public static (Tracker Tracker, SaveCounter Saves) Load(AppData? data = null, Action<GameLogWriter>? more = null)
    {
        var w = Writer();
        more?.Invoke(w);
        var saves = new SaveCounter();
        var t = new Tracker(data ?? Data(), _ => saves.Count++);
        t.Load(new[] { w.Read() });
        t.Select(t.State.Sessions[^1]);
        t.LoadStops();
        t.LoadRuns();
        return (t, saves);
    }

    /// <summary>The run that is handed in, and the one on the way.</summary>
    public static (WorkerTrip Done, WorkerTrip OnTheWay) Runs(Tracker t) =>
        (t.State.Trips.Single(r => r.End.HasValue), t.State.Trips.Single(r => !r.End.HasValue));
}

/// <summary>How often the data was saved.</summary>
sealed class SaveCounter
{
    public int Count;
}
