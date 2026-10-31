using SCTools.Tests.Support;

namespace SCTools.Tests.Boarding;

/// <summary>
/// The ship tracker (ShipTracker.Work): whose ship every id is, from the voice channel of whoever jumps in it; who flies it; the ship everybody playing is
/// in, a rider with the pilot. The situations are Frank's (31 Oct 2026): zzatvo and zzoii ride in zzoega's Perseus; zzoega leaves it to steal a Freelancer
/// and zzatvo flies the Perseus on; zzoega's Pisces docks in zzatvo's Carrack.
/// </summary>
public class ShipTrackerTests
{
    static readonly DateTime T = new(2026, 10, 31, 2, 40, 0, DateTimeKind.Utc);
    const string Perseus = "898683539008";

    static (List<HaulingLog>, Dictionary<string, List<(string File, GameRun Run, bool Quit)>>) Logs(params (string Name, GameLogWriter Log)[] players)
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
        return (logs, games);
    }

    static GameLogWriter Player(string name) => new GameLogWriter().At(T.AddMinutes(-30)).Login(name);

    [Fact]
    public void The_game_writes_whose_ship_he_boards_on_the_notifications_first_line_and_the_trackers_send_it()
    {
        var w = Player("zzatvo").Minutes(1).Boarded("Crusader C2 Hercules Starlifter", "zzoega").Seconds(0.002).Boarded("Crusader C2 Hercules Starlifter", "zzoega")
            .Minutes(5).LeftShip("Crusader C2 Hercules Starlifter", "zzoega");
        var log = w.Read();
        Assert.Equal(new[] { (true, "Crusader C2 Hercules Starlifter", "zzoega"), (false, "Crusader C2 Hercules Starlifter", "zzoega") },
            log.Channels.Select(c => (c.Joined, c.Ship, c.Owner)));   // the same notification twice in a moment is one
        Assert.Equal(T.AddMinutes(-29), log.Channels[0].Time);

        // the tracker uploads the line that names the ship (the line after it, with the queue, has only its number)
        var filter = new LineFilter();
        var kept = w.Lines.Where(filter.Keep).ToList();
        Assert.Contains(kept, l => l.Contains("You have joined channel 'Crusader C2 Hercules Starlifter : zzoega'."));
        Assert.Contains(kept, l => l.Contains("You have left the channel 'Crusader C2 Hercules Starlifter : zzoega'."));
        Assert.DoesNotContain(kept, l => l.Contains("<UpdateNotificationItem>"));

        // somebody else joining the owner's channel is not a ship of his own (that line names nobody's ship he is in)
        Assert.Empty(Player("zzoega").Minutes(1).ChannelJoined("zzatvo").Read().Channels);
    }

    [Fact]
    public void Whose_ship_an_id_is_comes_from_the_channel_of_whoever_jumps_in_it_and_a_rider_rides_with_its_pilot()
    {
        var (logs, games) = Logs(
            ("zzoega", Player("zzoega").Minutes(1).Boarded("RSI Perseus", "zzoega").Minutes(5).QuantumSelected("rs_ext_pyro-nyx_jp", "RSI_Perseus", Perseus)),
            ("zzatvo", Player("zzatvo").Minutes(2).Boarded("RSI Perseus", "zzoega")),
            ("zzoii", Player("zzoii").Minutes(2).Boarded("RSI Aurora Mk II", "zzoii")));
        var book = ShipTracker.Work(logs, games, T);

        Assert.Equal(new ShipOwner("RSI Perseus", "zzoega", "RSI_Perseus"), book.Ships[Perseus]);
        Assert.Equal("zzoega", book.OwnerOf(Perseus));
        var zzatvo = book.Of("zzatvo")!;
        Assert.Equal(("RSI Perseus", "zzoega", Perseus, "RSI_Perseus", "zzoega", true, true),
            (zzatvo.Ship, zzatvo.Owner, zzatvo.ShipId, zzatvo.Vehicle, zzatvo.Pilot, zzatvo.Riding, zzatvo.Borrowed));
        Assert.False(book.Of("zzoega")!.Riding);
        // his own ship, no jump in it yet: he flies it (its owner), and the book does not know its id
        Assert.Equal(("zzoii", false, (string?)null), (book.Of("zzoii")!.Pilot, book.Of("zzoii")!.Riding, book.Of("zzoii")!.ShipId));
        Assert.Equal("zzatvo", book.RidersKey);

        // a game that has ended is in no ship
        Assert.Empty(ShipTracker.Work(logs, games, T.AddHours(1)).Players);
    }

    [Fact]
    public void The_owner_is_not_always_the_pilot_and_a_stolen_ship_has_no_channel()
    {
        // zzoega leaves his Perseus to steal a Freelancer full of gold (an AI ship: no channel), and zzatvo flies the Perseus on with zzoii aboard (5 Oct 2026)
        var (logs, games) = Logs(
            ("zzoega", Player("zzoega").Minutes(1).Boarded("RSI Perseus", "zzoega").Minutes(5).QuantumSelected("rs_ext_pyro5", "RSI_Perseus", Perseus).Minutes(4).QuantumArrived("RSI_Perseus", Perseus)
                .Minutes(10).QuantumSelected("gold", "MISC_Freelancer_PU_AI_CRIM", "858422929977")),
            ("zzatvo", Player("zzatvo").Minutes(2).Boarded("RSI Perseus", "zzoega").Minutes(19).Seconds(30).QuantumSelected("rs_ext_pyro6", "RSI_Perseus", Perseus)),
            ("zzoii", Player("zzoii").Minutes(2).Boarded("RSI Perseus", "zzoega")));
        var book = ShipTracker.Work(logs, games, T);

        Assert.Null(book.Of("zzoega"));   // he flies the Freelancer: in no ship of anybody's (the fleets take the class of his jump)
        var zzatvo = book.Of("zzatvo")!;
        Assert.Equal(("zzatvo", false, true), (zzatvo.Pilot, zzatvo.Riding, zzatvo.Borrowed));   // he flies zzoega's ship: zzoega's drive
        Assert.Equal(("zzatvo", true), (book.Of("zzoii")!.Pilot, book.Of("zzoii")!.Riding));
        Assert.Equal(("zzoega", "RSI Perseus"), (book.Ships[Perseus].Owner, book.Ships[Perseus].Ship));
        Assert.False(book.Ships.ContainsKey("858422929977"));
    }

    [Fact]
    public void Climbing_out_of_a_docked_ship_leaves_him_in_the_ship_it_is_docked_in_and_a_new_game_starts_in_none()
    {
        // 17 Oct 2026: zzoega in zzatvo's Carrack, into his own Pisces docked in it and out again; the Carrack's channel stayed his
        var log = Player("zzoega").Minutes(1).Boarded("Anvil Carrack", "zzatvo").Minutes(10).Boarded("Anvil C8R Pisces Rescue", "zzoega")
            .Minutes(3).LeftShip("Anvil C8R Pisces Rescue", "zzoega").Read();
        Assert.Equal(("Anvil C8R Pisces Rescue", "zzoega"), (ShipTracker.HeldAt(log, T.AddMinutes(-18))!.Ship, ShipTracker.HeldAt(log, T.AddMinutes(-18))!.Owner));
        Assert.Equal(("Anvil Carrack", "zzatvo"), (ShipTracker.HeldAt(log, T)!.Ship, ShipTracker.HeldAt(log, T)!.Owner));

        // he quit and started the game again: the channels were the game before's
        log.GameRuns["a.log"] = new GameRun(T.AddMinutes(-30), T.AddMinutes(-5));
        log.GameRuns["b.log"] = new GameRun(T.AddMinutes(-2), T);
        Assert.Null(ShipTracker.HeldAt(log, T));
        Assert.Equal("Anvil Carrack", ShipTracker.HeldAt(log, T.AddMinutes(-3))!.Ship);
    }
}
