using SCTools.Tests.Support;

namespace SCTools.Tests.Locations;

/// <summary>
/// Riders on the map (LocationTracker.Rides, AboardJump): zzatvo is zzoega's gunner in the Perseus. His log has the ship's channel, but about one jump in
/// four not its arrival line, and a jump picked out in space has no area line to go by until it arrives: the ship tracker's channel puts him on zzoega's
/// jump, unless his own area lines say he got off.
/// </summary>
public class RidersTests
{
    static readonly DateTime T = new(2026, 10, 31, 3, 0, 0, DateTimeKind.Utc);
    const long PyroSpace = 3184339241, NyxGatewayArea = 125532322, RuinArea = 685899972, RuinInside = 2703988875;
    const string NyxGateway = "rs_ext_pyro-nyx_jp", Perseus = "898683539008";

    static readonly Dictionary<string, IReadOnlyList<SCTools.Missions.MissionSeen>> NoTitles = new();

    static LocationBook Work(DateTime now, params (string Name, GameLogWriter Log)[] players)
    {
        var logs = new List<HaulingLog>();
        var games = new Dictionary<string, List<(string File, GameRun Run, bool Quit)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, w) in players)
        {
            var log = w.Read();
            log.Uploader = name;
            log.GameRuns["game.log"] = new GameRun(T.AddHours(-1), T.AddMinutes(10));
            logs.Add(log);
            games[name] = new() { ("game.log", new GameRun(T.AddHours(-1), T.AddMinutes(10)), false) };
        }
        return LocationTracker.Work(logs, games, NoTitles, Atlas.Default, now, ships: ShipTracker.Work(logs, games, now));
    }

    // zzoega in his Perseus out in Pyro's space picks the Nyx Gateway (no area to leave), and comes into the gateway's area five minutes later
    static GameLogWriter Pilot(bool arrived) =>
        Arrive(new GameLogWriter().At(T.AddMinutes(-20)).Login("zzoega").Boarded("RSI Perseus", "zzoega").AreaChange("zzoega", 0, 0, RuinArea, PyroSpace)
            .At(T).QuantumSelected(NyxGateway, "RSI_Perseus", Perseus), arrived);

    static GameLogWriter Arrive(GameLogWriter w, bool arrived) =>
        arrived ? w.At(T.AddSeconds(300)).AreaChange("zzoega", 0, 0, PyroSpace, NyxGatewayArea).At(T.AddSeconds(302)).QuantumArrived("RSI_Perseus", Perseus) : w;

    static GameLogWriter Gunner(bool channel = true)
    {
        var w = new GameLogWriter().At(T.AddMinutes(-19)).Login("zzatvo");
        if (channel) w.Boarded("RSI Perseus", "zzoega");
        return w;
    }

    [Fact]
    public void A_rider_whose_log_has_no_arrival_goes_where_his_pilot_jumped()
    {
        // zzatvo's log: the Perseus's channel, nothing about the jump (no arrival line, no area line)
        var book = Work(T.AddMinutes(6), ("zzoega", Pilot(arrived: true)), ("zzatvo", Gunner()));
        Assert.Equal("pyro/nyx-gateway", book.Where("zzoega")!.At?.Key);
        Assert.Equal("pyro/nyx-gateway", book.Where("zzatvo")!.At?.Key);

        // before, with no channel line, the map had no idea (he stayed where he was)
        Assert.NotEqual("pyro/nyx-gateway", Work(T.AddMinutes(6), ("zzoega", Pilot(arrived: true)), ("zzatvo", Gunner(channel: false))).Where("zzatvo")!.At?.Key);

        // without the channel, his area line into the gateway's area with zzoega's says it as well
        var cameIn = Gunner(channel: false).At(T.AddSeconds(300.6)).AreaChange("zzatvo", 0, 0, PyroSpace, NyxGatewayArea);
        Assert.Equal("pyro/nyx-gateway", Work(T.AddMinutes(6), ("zzoega", Pilot(arrived: true)), ("zzatvo", cameIn)).Where("zzatvo")!.At?.Key);
    }

    [Fact]
    public void During_a_jump_picked_out_in_space_the_rider_is_on_it()
    {
        var book = Work(T.AddMinutes(1), ("zzoega", Pilot(arrived: false)), ("zzatvo", Gunner()));
        var gunner = book.Where("zzatvo")!;
        Assert.Equal(("pyro/nyx-gateway", T), (gunner.To?.Key, gunner.Picked?.Time));
        // as his pilot: heading to the gateway (picked out in space, the game named no place it starts from)
        Assert.Equal((PlayerState.Heading, PlayerState.Heading),
            (LocationTracker.Now(book.Where("zzoega")!, QuantumDrives.All[0], T.AddMinutes(1)).State, LocationTracker.Now(gunner, QuantumDrives.All[0], T.AddMinutes(1)).State));

        // without the channel nothing of his says he is aboard: not on it
        Assert.Null(Work(T.AddMinutes(1), ("zzoega", Pilot(arrived: false)), ("zzatvo", Gunner(channel: false))).Where("zzatvo")!.Picked);
    }

    [Fact]
    public void A_rider_who_got_off_is_not_taken_along()
    {
        // zzoega took off from Ruin and jumped; zzatvo had got off and walked into the station (the game did not say he left the ship's channel)
        var pilot = new GameLogWriter().At(T.AddMinutes(-20)).Login("zzoega").Boarded("RSI Perseus", "zzoega").At(T)
            .QuantumSelected(NyxGateway, "RSI_Perseus", Perseus).RouteStart("Ruin Station", NyxGateway).At(T.AddSeconds(40)).AreaChange("zzoega", 0, 0, RuinArea, PyroSpace);
        var off = Gunner().At(T.AddSeconds(20)).AreaChange("zzatvo", 0, 0, RuinArea, RuinInside);
        Assert.Null(Work(T.AddMinutes(1), ("zzoega", pilot), ("zzatvo", off)).Where("zzatvo")!.Picked);

        var arrived = Arrive(pilot, true);
        Assert.NotEqual("pyro/nyx-gateway", Work(T.AddMinutes(6), ("zzoega", arrived), ("zzatvo", off)).Where("zzatvo")!.At?.Key);
    }
}
