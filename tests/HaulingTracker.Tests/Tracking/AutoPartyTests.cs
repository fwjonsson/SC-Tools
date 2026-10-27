using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.Tracking;

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

        var r = AutoParty.Work(u.Logs, Members, At(125));
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
        var party = Assert.Single(AutoParty.Work(u.Logs, Members, At(31)).Groups);
        Assert.Equal("bileh123", party.Leader);   // written as in the party table
        Assert.Equal("bileh123's party", party.Title);
        Assert.Equal(new[] { "bileh123", "zzatvo", "zzoega" }, party.Names);
    }

    [Fact]
    public void A_crash_or_a_relog_of_less_than_ten_minutes_does_not_count()
    {
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().Minutes(4).MemberJoined("zzatvo")
            .Minutes(25).MemberLeft("zzatvo").Minutes(2).MemberJoined("zzatvo")      // zzatvo crashed and came back
            .Minutes(20).MemberLeft("Bileh123"), last: At(60));                      // not in the party
        var r = AutoParty.Work(u.Logs, Members, At(61));
        Assert.Equal(Changes((5, true)), r.Changes["zzatvo"]);

        Upload(u, "zzoega", new GameLogWriter().At(At(58)).MemberLeft("zzatvo"), last: At(61));
        Assert.Equal(Changes((5, true)), AutoParty.Work(u.Logs, Members, At(62)).Changes["zzatvo"]);   // gone for 4 minutes so far: maybe he comes back
        Assert.Equal(Changes((5, true), (58, false)), AutoParty.Work(u.Logs, Members, At(70)).Changes["zzatvo"]);   // 12 minutes: gone since he left
    }

    [Fact]
    public void A_quiet_tracker_is_a_game_that_ended_unless_somebody_playing_still_has_him_in_his_party()
    {
        var u = new LogUploads();
        Upload(u, "bileh123", new GameLogWriter().Login("Bileh123").Minutes(30).Noise(), last: At(30));   // alone, his game quiet since 30
        Upload(u, "zzatvo", new GameLogWriter().Login("zzatvo").Minutes(1).PartyJoined(), last: At(30));    // his tracker off at 30, still in the party
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().MemberJoined("zzatvo"), last: At(90));

        var r = AutoParty.Work(u.Logs, Members, At(90));
        Assert.Equal(Changes((0, true), (30, false)), r.Changes["bileh123"]);   // ended with the last time his tracker saw
        Assert.Equal(Changes((0, true)), r.Changes["zzatvo"]);                  // zzoega's game still has him in the party: the logs win
        Assert.Equal(new[] { "zzatvo", "zzoega" }, Assert.Single(r.Groups).Names);

        Assert.Equal(Changes((0, true)), AutoParty.Work(u.Logs, Members, At(38)).Changes["bileh123"]);   // quiet for eight minutes: still playing
    }

    [Fact]
    public void A_disbanded_party_leaves_everybody_alone()
    {
        var u = new LogUploads();
        Upload(u, "zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().MemberJoined("Bileh123").Minutes(30).MemberLeft("Bileh123").Disbanded(),
            last: At(60));
        var r = AutoParty.Work(u.Logs, Members, At(60));
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
        var r = new AutoParty.Result();
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
        Assert.True(AutoParty.Apply(data, r, At(21)));        // he is not active now
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
        var r = new AutoParty.Result();
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
