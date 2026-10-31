using SCTools.Tests.Support;

namespace SCTools.Tests.Locations;

/// <summary>
/// The location tracker (LocationTracker.Work): everybody playing where his clues put him, a crew member through his pilot's log (his party asked first),
/// the Hauling Planner's pickups and deliveries where they are newer than his jumps; and where the map draws him now with his drive (Now).
/// </summary>
public class LocationTrackerTests
{
    static readonly DateTime T = new(2026, 10, 31, 20, 0, 0, DateTimeKind.Utc);
    const string Gaslight = "rs_ext_pyro5_l2", PatchCity = "rs_ext_pyro3_l3";

    static readonly Dictionary<string, IReadOnlyList<SCTools.Missions.MissionSeen>> NoTitles = new();

    /// <summary>The logs of these players, each playing until the time given (quit: his game was quit then).</summary>
    static (List<HaulingLog> Logs, Dictionary<string, List<(string File, GameRun Run, bool Quit)>> Games) Logs(params (string Name, GameLogWriter Log, DateTime Last, bool Quit)[] players)
    {
        var logs = new List<HaulingLog>();
        var games = new Dictionary<string, List<(string File, GameRun Run, bool Quit)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, w, last, quit) in players)
        {
            var log = w.Read();
            log.Uploader = name;
            logs.Add(log);
            games[name] = new() { ("game.log", new GameRun(T.AddHours(-1), last, quit), quit) };
        }
        return (logs, games);
    }

    [Fact]
    public void Everybody_playing_is_in_the_book_and_nobody_else()
    {
        var (logs, games) = Logs(
            ("zzoega", new GameLogWriter().At(T.AddMinutes(-10)).Login("zzoega").Inventory("zzoega", "Nyx_Levski"), T, false),
            ("zzatvo", new GameLogWriter().At(T.AddMinutes(-40)).Login("zzatvo").Inventory("zzatvo", "Nyx_Levski"), T.AddMinutes(-20), false),   // quiet for twenty minutes: his game ended
            ("zzoii", new GameLogWriter().At(T.AddMinutes(-10)).Login("zzoii").Inventory("zzoii", "Nyx_Levski"), T, true));   // quit
        var book = LocationTracker.Work(logs, games, NoTitles, Atlas.Default, T);
        var zzoega = Assert.Single(book.Players);
        Assert.Equal(("zzoega", "nyx/levski", T.AddMinutes(-10)), (zzoega.Name, zzoega.Where.At?.Key, zzoega.Where.Since));
        Assert.Same(zzoega, book.Of("ZZOEGA"));
        Assert.Null(book.Where("zzatvo"));
        Assert.Null(book.ForPlanner("zzoii"));
    }

    [Fact]
    public void A_crew_member_goes_where_his_pilot_picked_and_his_party_is_asked_first()
    {
        // zzatvo's log has only the arrival of the ship he rides in; two logs have an arrival of a ship with that id at the same moment, with a destination
        // picked before it: the one of his party (zzoega) is his pilot
        var (logs, games) = Logs(
            ("zzatvo", new GameLogWriter().At(T.AddMinutes(-5)).Login("zzatvo").Minutes(4).QuantumArrived(), T, false),
            ("stranger", new GameLogWriter().At(T.AddMinutes(-5)).Login("stranger").Minutes(1).QuantumSelected(PatchCity).Minutes(3).QuantumArrived(), T, false),
            ("zzoega", new GameLogWriter().At(T.AddMinutes(-5)).Login("zzoega").Minutes(1).QuantumSelected(Gaslight).Minutes(3).QuantumArrived(), T, false));
        Assert.Equal("pyro/patch-city", LocationTracker.Work(logs, games, NoTitles, Atlas.Default, T).Where("zzatvo")?.At?.Key);   // without the parties: the first log that has it
        var parties = new PartyBook { Now = T };
        parties.Groups.Add(new PartyGroup("zzoega", new[] { "zzoega", "zzatvo" }));
        var book = LocationTracker.Work(logs, games, NoTitles, Atlas.Default, T, parties);
        Assert.Equal("pyro/gaslight", book.Where("zzatvo")?.At?.Key);
        Assert.Equal(new PlannerPlace(PlayedSession.Gaslight, T.AddMinutes(-1), null, null), book.ForPlanner("zzatvo"));
    }

    [Fact]
    public void The_planners_pickup_places_him_when_it_is_newer_than_his_jumps()
    {
        var (logs, games) = Logs(("zzoega", new GameLogWriter().At(T.AddMinutes(-30)).Login("zzoega").Minutes(1).QuantumSelected(Gaslight).Minutes(4).QuantumArrived(), T, false));
        PlayerLocation Where(PlannerClue? clue) => LocationTracker.Work(logs, games, NoTitles, Atlas.Default, T, null, _ => clue).Where("zzoega")!;

        // he picked up at Patch City after he arrived at Gaslight: he is at Patch City, by the Hauling Planner
        var w = Where(new PlannerClue(PlayedSession.PatchCity, T.AddMinutes(-10)));
        Assert.Equal(("pyro/patch-city", T.AddMinutes(-10), true), (w.At?.Key, w.Since, w.ByPlanner));
        Assert.Equal(new PlannerPlace(PlayedSession.PatchCity, T.AddMinutes(-10), null, null), w.ForPlanner);
        // a pickup before the arrival, or at the station he is at: his jumps say it
        w = Where(new PlannerClue(PlayedSession.PatchCity, T.AddMinutes(-28)));
        Assert.Equal(("pyro/gaslight", false), (w.At?.Key, w.ByPlanner));
        Assert.False(Where(new PlannerClue(PlayedSession.Gaslight, T.AddMinutes(-10))).ByPlanner);
        Assert.False(Where(null).ByPlanner);   // not in the planner's party
    }

    [Fact]
    public void Now_says_where_the_map_draws_him_with_his_drive()
    {
        var drive = QuantumDrives.All[0];
        var ruin = Atlas.Default.Find("pyro/ruin-station")!;
        var gaslight = Atlas.Default.Find("pyro/gaslight")!;
        var picked = new QtEvent { Time = T, Kind = QtKind.Selected, Destination = Gaslight };
        var seconds = drive.Seconds(StarMap.JumpGm(ruin, gaslight));

        // picked on the pad and not off it yet: still at Ruin, leaving for Gaslight
        var onPad = new PlayerLocation("pyro", ruin, T.AddMinutes(-5), picked, ruin, gaslight, Moves: new List<AreaChange>(), PickedLanded: true);
        var now = LocationTracker.Now(onPad, drive, T.AddSeconds(30));
        Assert.Equal((PlayerState.At, "pyro/ruin-station", "Gaslight", (DateTime?)T.AddMinutes(-5)), (now.State, now.At?.Key, now.Leaving, now.Since));

        // picked out in space: the jump starts a few seconds later, and the map moves him from then on
        var inSpace = onPad with { PickedLanded = false };
        now = LocationTracker.Now(inSpace, drive, T.AddSeconds(30));
        Assert.Equal((PlayerState.Jump, T + LocationTracker.JumpDelay, seconds), (now.State, now.Start, now.Seconds));
        Assert.Equal(("pyro/ruin-station", "pyro/gaslight", "Gaslight"), (now.From?.Key, now.To?.Key, now.ToName));

        // long over without the arrival in the log: he is there since the jump ended
        var late = T + LocationTracker.JumpDelay + TimeSpan.FromSeconds(seconds);
        now = LocationTracker.Now(inSpace, drive, late + LocationTracker.LateArrival + TimeSpan.FromSeconds(1));
        Assert.Equal((PlayerState.At, "pyro/gaslight", (DateTime?)late), (now.State, now.At?.Key, now.Since));

        // off the grid after a jump the map cannot follow, with the last place it had him at
        now = LocationTracker.Now(new PlayerLocation("pyro", null, T, null, null, null, Jumped: true, LastPing: ruin), drive, T);
        Assert.Equal((PlayerState.Unknown, true, "Ruin Station", (DateTime?)T), (now.State, now.Off, now.LastPing, now.Since));
    }
}
