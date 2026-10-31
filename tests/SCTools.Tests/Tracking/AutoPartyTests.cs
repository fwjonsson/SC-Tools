using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>
/// Who of the party is active comes from the logs: the party in the game (its notifications) and the trackers (whose game is running). The formats of the
/// notifications are copied from Frank's logs of 15 to 26 Oct 2026.
/// </summary>
public class AutoPartyTests
{
    static readonly DateTime T0 = GameLogWriter.Start;
    static readonly string[] Members = { "zzoega", "zzatvo", "bileh123" };

    static DateTime At(double minutes) => T0.AddMinutes(minutes);

    /// <summary>The log of one game of this player as his tracker uploaded it, the game still writing at last (null: until its last line).</summary>
    static void Upload(LogUploads u, string player, GameLogWriter w, DateTime? last = null)
    {
        var file = w.Lines[0][1..24] + "Z";   // named by the time of its first line, as LogSender names it
        var cursor = u.Cursor(player, file);
        Assert.True(u.Add(player, file, cursor, cursor + 1000, w.Lines.ToList(), last).Ok);
    }

    static List<(DateTime Time, bool Active)> Changes(params (double Minutes, bool Active)[] changes) => changes.Select(c => (At(c.Minutes), c.Active)).ToList();

    [Fact]
    public void The_party_notifications_are_read_and_nothing_else()
    {
        var log = new HaulingLog();
        var w = new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo").NewLeader("zzoega").ChannelJoined("zzatvo")
            .Minutes(1).MemberLeft("zzatvo").PartyLaunch("zzoega").Disbanded();
        foreach (var line in w.Lines) log.ProcessLine(line);
        Assert.Equal(
            new[] { (PartyEventKind.Self, (string?)null), (PartyEventKind.Joined, "zzatvo"), (PartyEventKind.Leader, "zzoega"), (PartyEventKind.Left, "zzatvo"),
                (PartyEventKind.Leader, "zzoega"), (PartyEventKind.Disbanded, null) },
            log.PartyEvents.Select(e => (e.Kind, e.Name)));   // the echoes of a notification and a ship's channel are not the party
        Assert.Equal(At(2), log.PartyEvents[1].Time);
        Assert.True(w.Lines.Where(LineFilterKeeps).Count() >= 6);   // and the trackers send these lines
    }

    static bool LineFilterKeeps(string line) => new LineFilter().Keep(line) && line.Contains("party", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Who_plays_and_with_whom()
    {
        var u = new LogUploads();
        // zzoega makes the party, zzatvo joins it, Bileh (no tracker) joins and leaves it an hour later; both trackers are still sending
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().Minutes(4).MemberJoined("zzatvo").Minutes(5).MemberJoined("Bileh123")
            .Minutes(50).MemberLeft("Bileh123"), last: At(120));
        Upload(u, "zzatvo", new GameLogWriter().At(At(4)).Login("zzatvo").Minutes(1).PartyJoined(), last: At(120));

        var r = PartyTracker.Work(u.Logs, Members, At(125));
        Assert.Equal(Changes((0, true)), r.Changes["zzoega"]);
        Assert.Equal(Changes((4, true)), r.Changes["zzatvo"]);
        Assert.Equal(Changes((10, true), (60, false)), r.Changes["bileh123"]);   // seen through zzoega's log
        var party = Assert.Single(r.Groups);
        Assert.Equal("zzoega", party.Leader);   // he made it
        Assert.Equal(new[] { "zzatvo", "zzoega" }, party.Names);
        Assert.Equal("zzoega's party", party.Title);
        Assert.Null(r.GroupOf("bileh123"));
    }

    [Fact]
    public void The_leader_is_the_one_the_game_named_last()
    {
        var u = new LogUploads();
        Upload(u, "zzatvo", new GameLogWriter().Login("zzatvo").Minutes(1).PartyJoined().NewLeader("Bileh123").MemberJoined("zzoega"), last: At(30));
        var party = Assert.Single(PartyTracker.Work(u.Logs, Members, At(31)).Groups);
        Assert.Equal("bileh123", party.Leader);   // written as in the party table
        Assert.Equal("bileh123's party", party.Title);
        Assert.Equal(new[] { "bileh123", "zzatvo", "zzoega" }, party.Names);
    }

    [Fact]
    public void A_crash_or_a_relog_of_less_than_fifteen_minutes_does_not_count()
    {
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().Minutes(4).MemberJoined("zzatvo")
            .Minutes(25).MemberLeft("zzatvo").Minutes(2).MemberJoined("zzatvo")      // zzatvo crashed and came back
            .Minutes(20).MemberLeft("Bileh123"), last: At(60));                      // not in the party
        var r = PartyTracker.Work(u.Logs, Members, At(61));
        Assert.Equal(Changes((5, true)), r.Changes["zzatvo"]);

        Upload(u, "zzoega", new GameLogWriter().At(At(58)).MemberLeft("zzatvo"), last: At(61));
        Assert.Equal(Changes((5, true)), PartyTracker.Work(u.Logs, Members, At(62)).Changes["zzatvo"]);   // gone for 4 minutes so far: maybe he comes back
        Assert.Equal(Changes((5, true)), PartyTracker.Work(u.Logs, Members, At(70)).Changes["zzatvo"]);   // 12 minutes: the game still keeps a crashed player
        Assert.Equal(Changes((5, true), (58, false)), PartyTracker.Work(u.Logs, Members, At(75)).Changes["zzatvo"]);   // 17 minutes: gone since he left
    }

    [Fact]
    public void A_crash_keeps_the_party_when_the_game_says_nothing_about_it()
    {
        // 28 Oct: zzatvo made the party, zzoega joined it and was made its leader; zzatvo's game crashed and he came back a minute later: nothing about the
        // party in either log after that (zzoega's log never named zzatvo), and in the game they were still in it
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(4).PartyJoined().Minutes(2).NewLeader("zzoega").PartyLaunch("zzoega"), last: At(40));
        Upload(u, "zzatvo", new GameLogWriter().At(At(2)).Login("zzatvo").Minutes(2).PartyCreated().Minutes(2).NewLeader("zzoega").Minutes(1).Noise(),
            last: At(7));                                                                              // crashed at 7
        Upload(u, "zzatvo", new GameLogWriter().At(At(8.5)).Login("zzatvo").Minutes(20).Noise(), last: At(40));   // back at 8.5

        var r = PartyTracker.Work(u.Logs, Members, At(40));
        var party = Assert.Single(r.Groups);
        Assert.Equal("zzoega", party.Leader);
        Assert.Equal(new[] { "zzatvo", "zzoega" }, party.Names);
        Assert.Equal(Changes((2, true)), r.Changes["zzatvo"]);

        // the next day (more than an hour later) a game starts alone, until the game says he is in a party
        Upload(u, "zzatvo", new GameLogWriter().At(At(200)).Login("zzatvo").Minutes(5).Noise(), last: At(210));
        Upload(u, "zzoega", new GameLogWriter().At(At(200)).Login("zzoega").Minutes(5).Noise(), last: At(210));
        Assert.Equal(2, PartyTracker.Work(u.Logs, Members, At(210)).Groups.Count);
    }

    [Fact]
    public void A_game_that_was_quit_ended_at_once()
    {
        // 28 Oct: zzoii quit his game (the quit line), and the site still had him playing (it waited for his log to be quiet for a while)
        var u = new LogUploads();
        Upload(u, "zzoii", new GameLogWriter().Login("zzoii").Minutes(29).Quit(), last: At(29));
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(30).Noise(), last: At(31));
        var r = PartyTracker.Work(u.Logs, new[] { "zzoega", "zzoii" }, At(31));   // two minutes after he quit
        Assert.Equal(new[] { "zzoega" }, r.Groups.Select(g => g.Title));   // not playing any more
        Assert.True(Assert.Single(u.Games()["zzoii"]).Run.Quit);
        Assert.Equal(Changes((0, true)), r.Changes["zzoii"]);   // for the payouts gone for less than fifteen minutes is not gone (he may relog)
        Upload(u, "zzoega", new GameLogWriter().At(At(31)).Minutes(14).Noise(), last: At(45));
        Assert.Equal(Changes((0, true), (29, false)), PartyTracker.Work(u.Logs, new[] { "zzoega", "zzoii" }, At(45)).Changes["zzoii"]);
    }

    [Fact]
    public void A_member_who_left_is_out_of_the_party_in_his_own_log_too()
    {
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(4).PartyJoined().NewLeader("zzoega").Minutes(16).MemberLeft("zzatvo"), last: At(40));
        Upload(u, "zzatvo", new GameLogWriter().Login("zzatvo").Minutes(4).PartyCreated().NewLeader("zzoega").Minutes(30).Noise(), last: At(40));   // his game says nothing
        var groups = PartyTracker.Work(u.Logs, Members, At(40)).Groups;
        Assert.Equal(new[] { "zzatvo", "zzoega" }, groups.Select(g => g.Title));   // each alone
    }

    [Fact]
    public void A_quiet_tracker_is_a_game_that_ended_unless_somebody_playing_still_has_him_in_his_party()
    {
        var u = new LogUploads();
        Upload(u, "bileh123", new GameLogWriter().Login("Bileh123").Minutes(30).Noise(), last: At(30));   // alone, his game quiet since 30
        Upload(u, "zzatvo", new GameLogWriter().Login("zzatvo").Minutes(1).PartyJoined(), last: At(30));    // his tracker off at 30, still in the party
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().MemberJoined("zzatvo"), last: At(90));

        var r = PartyTracker.Work(u.Logs, Members, At(90));
        Assert.Equal(Changes((0, true), (30, false)), r.Changes["bileh123"]);   // ended with the last time his tracker saw
        Assert.Equal(Changes((0, true)), r.Changes["zzatvo"]);                  // zzoega's game still has him in the party: the logs win
        Assert.Equal(new[] { "zzatvo", "zzoega" }, Assert.Single(r.Groups).Names);

        Assert.Equal(Changes((0, true)), PartyTracker.Work(u.Logs, Members, At(38)).Changes["bileh123"]);   // quiet for eight minutes: still playing
    }

    [Fact]
    public void A_disbanded_party_leaves_everybody_alone()
    {
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().MemberJoined("Bileh123").Minutes(30).MemberLeft("Bileh123").Disbanded(),
            last: At(60));
        var r = PartyTracker.Work(u.Logs, Members, At(60));
        Assert.Equal(Changes((1, true), (31, false)), r.Changes["bileh123"]);
        var alone = Assert.Single(r.Groups);
        Assert.Equal(new[] { "zzoega" }, alone.Names);
        Assert.Equal("zzoega", alone.Title);   // playing alone
    }

    [Fact]
    public void The_logs_take_over_and_the_admin_can_override_them_until_they_say_something_new()
    {
        var data = new AppData { Party = Members.ToList() };
        var crew = new Crew(new TrackerState(data, _ => { }));
        crew.SetMemberActive("bileh123", true, At(-60));   // ticked in the program before
        var r = new PartyBook();
        r.Changes["zzoega"] = Changes((0, true));
        r.Changes["zzatvo"] = Changes();
        r.Changes["bileh123"] = Changes();

        Assert.True(AutoParty.Apply(data, r, At(10)));
        Assert.Equal(At(10), data.AutoPartySince);
        Assert.True(crew.IsActiveAt("zzoega", At(11)));
        Assert.False(crew.IsActiveAt("bileh123", At(11)));   // not playing: no longer active, however he was ticked
        Assert.True(crew.IsActiveAt("bileh123", At(5)));     // the history before stays
        Assert.Equal(new[] { "bileh123", "zzatvo" }, data.InactiveParty.OrderBy(n => n));
        Assert.All(Members, m => Assert.Null(crew.OverrideOf(m)));
        Assert.False(AutoParty.Apply(data, r, At(11)));       // nothing new: nothing changes (nothing is written to the database)

        r.Changes["bileh123"] = Changes((15, true));          // he joins
        Assert.True(AutoParty.Apply(data, r, At(16)));
        Assert.True(crew.IsActiveAt("bileh123", At(16)));
        Assert.False(crew.IsActiveAt("bileh123", At(14)));

        crew.SetMemberActive("bileh123", false, At(20), PartyChange.ByAdmin);   // the admin: Out
        Assert.False(AutoParty.Apply(data, r, At(21)));       // the admin's Out is in place as it is: the logs change nothing
        Assert.False(crew.IsActiveAt("bileh123", At(21)));
        Assert.Equal(false, crew.OverrideOf("bileh123"));
        r.Changes["bileh123"] = Changes((15, true), (30, false));   // he leaves: what Out says already
        AutoParty.Apply(data, r, At(41));
        Assert.Equal(false, crew.OverrideOf("bileh123"));
        r.Changes["bileh123"] = Changes((15, true), (30, false), (50, true));   // he plays again: the logs decide again
        Assert.True(AutoParty.Apply(data, r, At(51)));
        Assert.True(crew.IsActiveAt("bileh123", At(51)));
        Assert.False(crew.IsActiveAt("bileh123", At(45)));
        Assert.Null(crew.OverrideOf("bileh123"));

        crew.SetMemberActive("zzoega", false, At(60), PartyChange.ByAdmin);   // Out, then back to Auto: what the logs say
        crew.SetMemberActive("zzoega", false, At(62), PartyChange.BackToLogs);
        Assert.True(AutoParty.Apply(data, r, At(63)));
        Assert.False(crew.IsActiveAt("zzoega", At(61)));
        Assert.True(crew.IsActiveAt("zzoega", At(63)));
        Assert.Null(crew.OverrideOf("zzoega"));
        Assert.False(AutoParty.Apply(data, r, At(64)));
    }

    [Fact]
    public void A_payout_is_shared_by_who_the_logs_had_active_when_it_paid_out()
    {
        var data = new AppData { Party = Members.ToList() };
        var crew = new Crew(new TrackerState(data, _ => { }));
        var r = new PartyBook();
        r.Changes["zzoega"] = Changes((0, true));
        r.Changes["zzatvo"] = Changes((0, true), (40, false));
        r.Changes["bileh123"] = Changes((20, true));
        AutoParty.Apply(data, r, At(0));
        AutoParty.Apply(data, r, At(60));
        Assert.Equal(new[] { "zzatvo", "zzoega" }, crew.PartyAt(At(10)).OrderBy(n => n));
        Assert.Equal(new[] { "bileh123", "zzatvo", "zzoega" }, crew.PartyAt(At(30)).OrderBy(n => n));
        Assert.Equal(new[] { "bileh123", "zzoega" }, crew.PartyAt(At(50)).OrderBy(n => n));
        Assert.Equal(new[] { "zzatvo" }, data.InactiveParty);   // the Trip Planner plans for the others
    }
}
