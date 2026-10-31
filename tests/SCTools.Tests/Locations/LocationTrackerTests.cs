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

    // the game's numbers of the area lines (zzoega's log of 30 Oct 2026): Ruin's hangars, Ruin's own area, Pyro's open space; Levski's hangars, Nyx's open space
    const long RuinHangar = 2026442305, RuinArea = 685899972, PyroSpace = 3184339241, LevskiHangar = 3723364946, NyxSpace = 2627827560, GaslightArea = 1719129565;

    [Fact]
    public void Out_of_the_hangar_into_the_stations_area_is_taking_off_and_the_jump_starts_when_he_leaves_that_area()
    {
        // in a hangar at Ruin he picks Gaslight: landed, leaving for Gaslight
        var qt = new List<QtEvent> { new() { Time = T.AddMinutes(1), Kind = QtKind.Selected, Destination = Gaslight, From = "Ruin Station" } };
        var areas = new List<AreaChange> { new(T, 0, RuinHangar, RuinArea, RuinHangar) };
        var drive = QuantumDrives.All[0];
        PlayerNow Now(DateTime t) => LocationTracker.Now(LocationTracker.Follow(qt, Atlas.Default, null, null, null, areas), drive, t);
        var now = Now(T.AddMinutes(1.5));
        Assert.Equal((PlayerState.At, "pyro/ruin-station", HangarState.Landed, "Gaslight"), (now.State, now.At?.Key, now.Hangar, now.Leaving));

        // out of the hangar into Ruin's own area: taking off, the jump has not started (he flies about the station for a minute and a half)
        areas.Add(new AreaChange(T.AddMinutes(2), RuinHangar, 0, RuinHangar, RuinArea));
        now = Now(T.AddMinutes(3));
        Assert.Equal((PlayerState.At, HangarState.TakingOff, "Gaslight"), (now.State, now.Hangar, now.Leaving));

        // out of Ruin's area into space: the jump starts then (before: out of the hangar, a minute and a half too early)
        areas.Add(new AreaChange(T.AddMinutes(3.5), RuinHangar, 0, RuinArea, PyroSpace));
        now = Now(T.AddMinutes(4));
        Assert.Equal((PlayerState.Jump, T.AddMinutes(3.5)), (now.State, now.Start));

        // into Gaslight's area well into the jump, then into its hangar: there, landed
        areas.Add(new AreaChange(T.AddMinutes(3.5).AddSeconds(now.Seconds * 0.9), RuinHangar, 0, PyroSpace, GaslightArea));
        var there = Now(T.AddMinutes(20));
        Assert.Equal((PlayerState.At, "pyro/gaslight", (HangarState?)null), (there.State, there.At?.Key, there.Hangar));
        qt.Add(new QtEvent { Time = T.AddMinutes(3.5).AddSeconds(now.Seconds * 0.95), Kind = QtKind.Arrived });
        areas.Add(new AreaChange(T.AddMinutes(3.5).AddSeconds(now.Seconds), RuinHangar, 1183702464, GaslightArea, 1183702464));
        now = Now(T.AddMinutes(20));
        Assert.Equal((PlayerState.At, "pyro/gaslight", (HangarState?)HangarState.Landed), (now.State, now.At?.Key, now.Hangar));
    }

    [Fact]
    public void Where_the_hangar_lets_out_straight_into_space_out_of_the_hangar_is_leaving()
    {
        // at Levski the hangars let out into open space: one line, and the jump starts with it
        var qt = new List<QtEvent> { new() { Time = T, Kind = QtKind.Selected, Destination = "rs_ext_nyx-pyro_jp1", From = "Levski" } };
        var areas = new List<AreaChange> { new(T.AddMinutes(-5), 0, LevskiHangar, NyxSpace, LevskiHangar), new(T.AddSeconds(40), LevskiHangar, 0, LevskiHangar, NyxSpace) };
        var w = LocationTracker.Follow(qt, Atlas.Default, null, null, null, areas);
        Assert.Equal(("nyx/levski", "nyx/pyro-gateway", (HangarState?)null), (w.From?.Key, w.To?.Key, w.Hangar));
        var now = LocationTracker.Now(w, QuantumDrives.All[0], T.AddMinutes(1));
        Assert.Equal((PlayerState.Jump, T.AddSeconds(40)), (now.State, now.Start));
    }

    [Fact]
    public void A_station_area_is_one_a_hangar_lets_out_into_and_he_leaves_again_soon()
    {
        Assert.Contains(RuinArea, StationAreas.Known);
        Assert.DoesNotContain(PyroSpace, StationAreas.Known);
        Assert.DoesNotContain(NyxSpace, StationAreas.Known);
        // a station the seed does not know: three take-offs into its area, each out of it within half a minute
        const long hangar = 11, area = 22, space = 33;
        var log = new HaulingLog();
        for (var i = 0; i < 3; i++)
        {
            var t = T.AddHours(i);
            log.Areas.Add(new AreaChange(t, hangar, 0, hangar, area));
            log.Areas.Add(new AreaChange(t.AddSeconds(12), hangar, 0, area, space));
        }
        Assert.Contains(area, StationAreas.Learn(new[] { log }));
        Assert.DoesNotContain(space, StationAreas.Learn(new[] { log }));
        // two of them are not enough yet, and an area he stays in (open space) is none
        log.Areas.RemoveRange(4, 2);
        Assert.DoesNotContain(area, StationAreas.Learn(new[] { log }));
    }

    [Fact]
    public void Now_says_where_the_map_draws_him_with_his_drive()
    {
        var drive = QuantumDrives.All[0];
        var ruin = Atlas.Default.Find("pyro/ruin-station")!;
        var gaslight = Atlas.Default.Find("pyro/gaslight")!;
        var picked = new QtEvent { Time = T, Kind = QtKind.Selected, Destination = Gaslight };
        var seconds = drive.Seconds(StarMap.JumpGm(ruin, gaslight));

        // picked in the hangar and not out of it yet: still at Ruin, leaving for Gaslight
        var inHangar = new PlayerLocation("pyro", ruin, T.AddMinutes(-5), picked, ruin, gaslight, Moves: new List<AreaChange>(), PickedLanded: true);
        var now = LocationTracker.Now(inHangar, drive, T.AddSeconds(30));
        Assert.Equal((PlayerState.At, "pyro/ruin-station", "Gaslight", (DateTime?)T.AddMinutes(-5)), (now.State, now.At?.Key, now.Leaving, now.Since));

        // picked out in space: the jump starts a few seconds later, and the map moves him from then on
        var inSpace = inHangar with { PickedLanded = false };
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
