using SCTools.Tests.Support;

namespace SCTools.Tests.Parties;

/// <summary>
/// The party tracker: the parties now and before, with who joined, left and led them, from the party lines of everybody's log. The lines are the ones of
/// Frank's three accounts' logs of 4 Aug to 30 Oct 2026 (what the game writes when a party is made from an invite, "connected", "disconnected").
/// </summary>
public class PartyTrackerTests
{
    static readonly DateTime T0 = GameLogWriter.Start;
    static readonly string[] Members = { "zzoega", "zzatvo", "zzoii", "Bileh123" };

    static DateTime At(double minutes) => T0.AddMinutes(minutes);

    /// <summary>The log of one game of this player as his tracker uploaded it, the game still writing until last.</summary>
    static void Upload(LogUploads u, string player, GameLogWriter w, DateTime last)
    {
        var file = w.Lines[0][1..24] + "Z";   // named by the time of its first line, as LogSender names it
        var cursor = u.Cursor(player, file);
        Assert.True(u.Add(player, file, cursor, cursor + 1000, w.Lines.ToList(), last).Ok);
    }

    static string News(PartyRecord p) =>
        string.Join(", ", p.News.Select(n => (n.Time - T0).TotalMinutes.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " " + n.Kind + " " + n.Name));

    [Fact]
    public void The_history_has_who_joined_left_and_led_and_how_the_party_ended()
    {
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo").Minutes(8).MemberJoined("Bileh123")
            .Minutes(20).NewLeader("zzatvo").Minutes(10).MemberLeft("Bileh123").Minutes(20).Disbanded().Minutes(30).Noise(), At(100));
        Upload(u, "zzatvo", new GameLogWriter().At(At(1.5)).Login("zzatvo").Minutes(0.5).PartyJoined().Minutes(8).MemberJoined("Bileh123").Minutes(20)
            .NewLeader("zzatvo").Minutes(10).MemberLeft("Bileh123").Minutes(20).Disbanded().Minutes(30).Noise(), At(100));

        var book = PartyTracker.Work(u.Logs, Members, At(100));
        var party = Assert.Single(book.History);
        Assert.Equal("2 Joined zzoega, 2 Joined zzatvo, 2 Leader zzoega, 10 Joined Bileh123, 30 Leader zzatvo, 40 Left Bileh123", News(party));
        Assert.Equal(At(2), party.Started);   // a party is two players or more: from when zzatvo joined
        Assert.Equal(At(60), party.Ended);
        Assert.True(party.Disbanded);
        Assert.Equal("zzatvo's party", party.Title);
        Assert.Equal(new[] { "zzoega", "zzatvo", "Bileh123" }, party.Everybody);
        Assert.Equal(new[] { "zzoega", "zzatvo", "Bileh123" }, party.NamesAt(At(20)));
        Assert.Equal("zzoega", party.LeaderAt(At(20)));
        Assert.Equal("zzatvo", party.LeaderAt(At(35)));
        Assert.Same(party, book.PartyAt("bileh123", At(20)));
        Assert.Null(book.PartyAt("bileh123", At(45)));
        Assert.Equal(new[] { "zzatvo", "zzoega" }, book.Groups.Select(g => g.Title));   // each alone now
        Assert.True(book.HasTracker("zzoega"));
        Assert.False(book.HasTracker("Bileh123"));   // only the others' logs show him
        Assert.Equal(At(40), book.LastSeen["Bileh123"]);
    }

    [Fact]
    public void A_party_made_from_an_invite_is_one_party_when_both_lines_come_within_seconds()
    {
        // zzoega invites zzatvo: "You have created party" in his log, "You have joined party" in zzatvo's, neither names the other (17 Sep 2026, 18:48)
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(2).PartyCreated().Minutes(30).Noise(), At(40));
        Upload(u, "zzatvo", new GameLogWriter().Login("zzatvo").Minutes(2).Seconds(6).PartyJoined().Minutes(30).Noise(), At(40));   // his clock is 6 s behind
        // zzoii makes a party of his own a minute later: not the same one
        Upload(u, "zzoii", new GameLogWriter().Login("zzoii").Minutes(3).PartyCreated().Minutes(30).Noise(), At(40));

        var book = PartyTracker.Work(u.Logs, Members, At(40));
        Assert.Equal(new[] { "zzoega: zzatvo, zzoega", "zzoii: zzoii" }, book.Groups.Select(g => g.Leader + ": " + string.Join(", ", g.Names)));
        Assert.Equal("2 Joined zzoega, 2 Joined zzatvo, 2 Leader zzoega", News(Assert.Single(book.History)));
        // who is active is the same as before the party tracker: being paired with somebody makes nobody active
        Assert.Equal(new[] { (At(0), true) }, book.Changes["zzatvo"]);
    }

    [Fact]
    public void A_member_without_a_tracker_is_known_from_the_others_lines()
    {
        // zzatvo joins zzoega's party, where Bileh123 (no tracker) already is: no line says Bileh123 joined, the game says he connected
        var u = new LogUploads();
        Upload(u, "zzatvo", new GameLogWriter().Login("zzatvo").Minutes(1).PartyJoined().NewLeader("zzoega").Minutes(4).Connected("Bileh123")
            .Minutes(10).Disconnected("Bileh123").Minutes(2).Connected("Bileh123").Minutes(20).Disconnected("Bileh123").Minutes(6).MemberLeft("Bileh123")
            .Minutes(10).Noise(), At(60));

        var book = PartyTracker.Work(u.Logs, Members, At(60));
        var party = Assert.Single(book.History);
        Assert.Equal("1 Joined zzoega, 1 Joined zzatvo, 1 Leader zzoega, 5 Joined Bileh123, 15 Dropped Bileh123, 17 Back Bileh123, 37 Dropped Bileh123, "
            + "43 Left Bileh123", News(party));
        Assert.Equal(new[] { (At(5), true), (At(43), false) }, book.Changes["Bileh123"]);   // in the party from his connected line until he left
        Assert.Equal(At(43), book.LastSeen["Bileh123"]);
    }

    [Fact]
    public void Connected_changes_nothing_about_a_player_with_a_tracker()
    {
        // zzoega's game ended at 20 (quiet since); zzatvo's game still says zzoega connected at 25: zzoega's own log is what says when he played
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().Minutes(19).Noise(), At(20));
        Upload(u, "zzatvo", new GameLogWriter().Login("zzatvo").Minutes(1).Seconds(2).PartyJoined().Minutes(24).Connected("zzoega").Minutes(30).Noise(), At(60));
        var book = PartyTracker.Work(u.Logs, Members, At(60));
        Assert.Equal(new[] { (At(0), true), (At(20), false) }, book.Changes["zzoega"]);
    }

    [Fact]
    public void A_relog_or_a_new_log_file_does_not_end_the_party()
    {
        // zzatvo's game is quit at 30 and he is back at 31 (a relog): the party goes on, nobody left
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().Minutes(59).Noise(), At(60));
        Upload(u, "zzatvo", new GameLogWriter().Login("zzatvo").Minutes(1).Seconds(2).PartyJoined().Minutes(28).Quit(), At(29));
        Upload(u, "zzatvo", new GameLogWriter().At(At(31)).Login("zzatvo").Minutes(29).Noise(), At(60));
        var book = PartyTracker.Work(u.Logs, Members, At(60));
        var party = Assert.Single(book.History);
        Assert.Null(party.Ended);
        Assert.Equal("1 Joined zzoega, 1 Joined zzatvo, 1 Leader zzoega", News(party));
    }

    [Fact]
    public void Two_parties_at_once_are_two_histories()
    {
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().MemberJoined("Bileh123").Minutes(30).Noise(), At(40));
        Upload(u, "zzoii", new GameLogWriter().Login("zzoii").Minutes(5).PartyCreated().MemberJoined("Trila").Minutes(30).Noise(), At(40));
        var book = PartyTracker.Work(u.Logs, Members, At(40));
        Assert.Equal(new[] { "zzoega's party", "zzoii's party" }, book.History.Select(p => p.Title));
        Assert.All(book.History, p => Assert.Null(p.Ended));
        Assert.Equal(new[] { "Bileh123", "zzoega" }, book.Together("zzoega").Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(new[] { "Bileh123", "zzoega", "zzoii" }, book.Together("zzoega", new[] { new[] { "zzoega", "zzoii" } }).Order(StringComparer.OrdinalIgnoreCase));   // he hauled with zzoii
    }

    [Fact]
    public void It_is_worked_out_once_and_every_page_reads_that_answer_until_something_new_comes_in()
    {
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().MemberJoined("Bileh123"), At(10));
        var live = new LiveTracker(new AppData { Party = { "zzoega" } }, null, u);
        var first = live.Parties(At(10));
        Assert.Same(first, live.Parties(At(10.2)));   // the same answer for every page
        Assert.Equal(new[] { "zzoega" }, first.Changes.Keys.Where(k => k == "zzoega"));   // the planner's party and the players of the logs
        Upload(u, "zzoega", new GameLogWriter().At(At(10)).MemberLeft("Bileh123"), At(10.5));
        var second = live.Parties(At(10.5));
        Assert.NotSame(first, second);                // a new piece of log: worked out again
        Assert.Equal(new[] { "zzoega" }, second.GroupOf("zzoega")!.Names);
        Assert.NotSame(second, live.Parties(At(11.1)));   // half a minute later: again (a game that stopped writing ends by the clock)
        live.Players = () => new[] { "zzoega", "ZZATVO" };
        Assert.True(live.Parties(At(11.2)).Changes.ContainsKey("ZZATVO"));   // the server's players: worked out again for them
    }

    [Fact]
    public void Connected_and_disconnected_lines_are_read_and_do_not_reload_the_session()
    {
        var log = new HaulingLog();
        var w = new GameLogWriter().Login("zzoega").Minutes(1).Connected("zzatvo").Disconnected("Bileh123");
        foreach (var line in w.Lines.Take(1)) log.ProcessLine(line);
        var version = log.Version;
        foreach (var line in w.Lines.Skip(1)) log.ProcessLine(line);
        Assert.Equal(new[] { (PartyEventKind.Connected, (string?)"zzatvo"), (PartyEventKind.Disconnected, "Bileh123") }, log.PartyEvents.Select(e => (e.Kind, e.Name)));
        Assert.Equal(version, log.Version);   // the hauling session is not read again for these (they come every few minutes)
        Assert.All(w.Lines.Where(l => l.Contains("connected.: \" [") && l.Contains("to queue")), l => Assert.True(new LineFilter().Keep(l)));   // the trackers send them already
    }
}
