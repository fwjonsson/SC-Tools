using SCTools.Tests.Support;

namespace SCTools.Tests.Locations;

/// <summary>
/// zzoega and zzatvo's flight of 31 Oct 2026, as their logs have it: zzoega picked the Nyx Gateway in his hangar at Ruin three minutes after his tracker
/// updated (so the server had no area line before the pick), took off and left Ruin's area; zzatvo rode in his ship. The jump starts when they leave Ruin's
/// area for both, and zzatvo flies with him, not at Ruin.
/// </summary>
public class CrewAndPickTests
{
    static readonly DateTime T = new(2026, 10, 31, 2, 40, 6, DateTimeKind.Utc);
    const long RuinHangar = 2026442305, RuinArea = 685899972, PyroSpace = 3184339241, NyxGatewayArea = 125532322, GatewayHangar = 3503445867;
    const string NyxGateway = "rs_ext_pyro-nyx_jp";

    static readonly Dictionary<string, IReadOnlyList<SCTools.Missions.MissionSeen>> NoTitles = new();

    static (List<HaulingLog>, Dictionary<string, List<(string File, GameRun Run, bool Quit)>>) Logs(params (string Name, GameLogWriter Log)[] players)
    {
        var logs = new List<HaulingLog>();
        var games = new Dictionary<string, List<(string File, GameRun Run, bool Quit)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, w) in players)
        {
            var log = w.Read();
            log.Uploader = name;
            logs.Add(log);
            games[name] = new() { ("game.log", new GameRun(T.AddHours(-1), T.AddMinutes(10), false), false) };
        }
        return (logs, games);
    }

    // zzoega's log as the server has it: no area line before the pick (his tracker sent none before it updated)
    static GameLogWriter Pilot(bool arrived = false)
    {
        var w = new GameLogWriter().At(T.AddMinutes(-10)).Login("zzoega").At(T).QuantumSelected(NyxGateway).RouteStart("Ruin Station", NyxGateway)
            .At(T.AddSeconds(41.3)).AreaChange("zzoega", RuinHangar, 0, RuinHangar, RuinArea)
            .At(T.AddSeconds(44.2)).AreaChange("zzoega", RuinHangar, 0, RuinArea, PyroSpace);
        if (arrived) w.At(T.AddSeconds(296.8)).AreaChange("zzoega", RuinHangar, 0, PyroSpace, NyxGatewayArea).At(T.AddSeconds(298.7)).QuantumArrived();
        return w;
    }

    // zzatvo's: landed at Ruin before, then out of the hangar straight into space half a second before zzoega's line
    static GameLogWriter Crew(bool arrived = false)
    {
        var w = new GameLogWriter().At(T.AddMinutes(-10)).Login("zzatvo").AreaChange("zzatvo", 0, RuinHangar, 2703988875, RuinHangar)
            .At(T.AddSeconds(43.7)).AreaChange("zzatvo", RuinHangar, 0, RuinHangar, PyroSpace);
        // his log names the gateway's area (the inventory line) 47 ms before his arrival line
        if (arrived) w.At(T.AddSeconds(298.5)).AreaChange("zzatvo", RuinHangar, 0, PyroSpace, NyxGatewayArea)
            .At(T.AddSeconds(299.1)).AreaChange("zzatvo", RuinHangar, GatewayHangar, NyxGatewayArea, GatewayHangar).Inventory("zzatvo", "RR_JP_PyroNyx")
            .At(T.AddSeconds(299.16)).QuantumArrived();
        return w;
    }

    [Fact]
    public void The_first_area_line_after_the_pick_says_he_was_in_a_hangar_and_the_jump_starts_when_he_leaves_the_stations_area()
    {
        var log = Pilot().Read();
        var w = LocationTracker.Follow(log.Qt, Atlas.Default, null, null, null, log.Areas);
        Assert.True(w.PickedLanded);
        var now = LocationTracker.Now(w, QuantumDrives.All[0], T.AddMinutes(1));
        Assert.Equal((PlayerState.Jump, "pyro/ruin-station", "pyro/nyx-gateway", T.AddSeconds(44.2)), (now.State, now.From?.Key, now.To?.Key, now.Start));   // not the pick + 10 s

        // before he took off: still at Ruin, leaving for the gateway
        var before = new GameLogWriter().At(T.AddMinutes(-10)).Login("zzoega").At(T).QuantumSelected(NyxGateway).RouteStart("Ruin Station", NyxGateway).Read();
        Assert.Equal(PlayerState.Jump, LocationTracker.Now(LocationTracker.Follow(before.Qt, Atlas.Default, null, null, null, before.Areas), QuantumDrives.All[0], T.AddSeconds(30)).State);   // nothing says he is landed: out in space, as before
    }

    [Fact]
    public void A_crew_member_flies_with_his_pilot_from_their_first_area_line_together()
    {
        var (logs, games) = Logs(("zzoega", Pilot()), ("zzatvo", Crew()));
        var book = LocationTracker.Work(logs, games, NoTitles, Atlas.Default, T.AddMinutes(1));
        var crew = book.Where("zzatvo")!;
        Assert.Equal(("pyro/ruin-station", "pyro/nyx-gateway"), (crew.From?.Key, crew.To?.Key));
        var now = LocationTracker.Now(crew, QuantumDrives.All[0], T.AddMinutes(1));
        Assert.Equal((PlayerState.Jump, T.AddSeconds(43.7)), (now.State, now.Start));   // his own line: half a second before zzoega's

        // before they took off he is where he was (nothing of his own says he is aboard)
        var (early, earlyGames) = Logs(("zzoega", new GameLogWriter().At(T.AddMinutes(-10)).Login("zzoega").At(T).QuantumSelected(NyxGateway).RouteStart("Ruin Station", NyxGateway)),
            ("zzatvo", new GameLogWriter().At(T.AddMinutes(-10)).Login("zzatvo").AreaChange("zzatvo", 0, RuinHangar, 2703988875, RuinHangar)));
        Assert.Null(LocationTracker.Work(early, earlyGames, NoTitles, Atlas.Default, T.AddSeconds(30)).Where("zzatvo")!.Picked);

        // they arrived: both at the Nyx Gateway, with markers (before: zzatvo's inventory line came first, and his arrival line took him "off the grid")
        (logs, games) = Logs(("zzoega", Pilot(arrived: true)), ("zzatvo", Crew(arrived: true)));
        book = LocationTracker.Work(logs, games, NoTitles, Atlas.Default, T.AddMinutes(6));
        Assert.Equal(("pyro/nyx-gateway", "pyro/nyx-gateway"), (book.Where("zzoega")!.At?.Key, book.Where("zzatvo")!.At?.Key));
        Assert.False(book.Where("zzatvo")!.Jumped);
        Assert.Equal(PlayerState.At, LocationTracker.Now(book.Where("zzatvo")!, QuantumDrives.All[0], T.AddMinutes(6)).State);
    }

    [Fact]
    public void Somebody_else_leaving_the_same_station_on_his_own_jump_is_not_crew()
    {
        // zzatvo flies his own ship: he picked his own jump, so he is on his, not on zzoega's
        var own = Crew().At(T.AddSeconds(40)).QuantumSelected("rs_ext_pyro5_l2").RouteStart("Ruin Station", "rs_ext_pyro5_l2");
        var (logs, games) = Logs(("zzoega", Pilot()), ("zzatvo", own));
        Assert.Equal("pyro/gaslight", LocationTracker.Work(logs, games, NoTitles, Atlas.Default, T.AddMinutes(1)).Where("zzatvo")!.To?.Key);
    }
}
