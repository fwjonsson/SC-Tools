using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>
/// What the party code showed before the party tracker (recorded on 31 Oct 2026 from AutoParty as it was): who plays with whom, who leads, and when every
/// member was active, at several moments of one evening of three trackers. The party tracker has to give the same answers, apart from the differences it
/// means to make (each one is marked where it is asserted).
/// </summary>
public class PartiesAsBeforeTests
{
    static readonly DateTime T0 = GameLogWriter.Start;
    static readonly string[] Members = { "zzoega", "zzatvo", "zzoii", "bileh123" };

    static DateTime At(double minutes) => T0.AddMinutes(minutes);

    /// <summary>
    /// The evening: zzoega makes a party, zzatvo joins it, Bileh123 (no tracker) joins at 10 and leaves at 40; zzatvo is made leader at 30; zzatvo's game
    /// crashes at 60 and he is back at 64 (nothing about the party after that); zzoii makes a party of his own at 6 with Trila (no tracker), disbanded at 50.
    /// </summary>
    static IEnumerable<(string Player, GameLogWriter Log, DateTime Last)> Evening()
    {
        yield return ("zzoega", new GameLogWriter().Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo").NewLeader("zzoega")
            .Minutes(8).MemberJoined("Bileh123").Minutes(20).NewLeader("zzatvo").Minutes(10).MemberLeft("Bileh123").Minutes(50).Noise(), At(90));
        yield return ("zzatvo", new GameLogWriter().At(At(1.5)).Login("zzatvo").Minutes(0.5).PartyJoined().NewLeader("zzoega").Minutes(8).MemberJoined("Bileh123")
            .Minutes(20).NewLeader("zzatvo").Minutes(10).MemberLeft("Bileh123").Minutes(20).Noise(), At(60));
        yield return ("zzatvo", new GameLogWriter().At(At(64)).Login("zzatvo").Minutes(26).Noise(), At(90));
        yield return ("zzoii", new GameLogWriter().At(At(5)).Login("zzoii").Minutes(1).PartyCreated().Minutes(1).MemberJoined("Trila").Minutes(43).Disbanded()
            .Minutes(40).Noise(), At(90));
    }

    /// <summary>The trackers' uploads as they were at this time (every log up to then).</summary>
    static LogUploads UploadsAt(DateTime now)
    {
        var u = new LogUploads();
        foreach (var (player, log, last) in Evening())
        {
            var lines = log.Lines.Where(l => DateTime.Parse(l[1..25], null, System.Globalization.DateTimeStyles.AdjustToUniversal) <= now).ToList();
            if (lines.Count == 0) continue;
            var file = lines[0][1..24] + "Z";
            var cursor = u.Cursor(player, file);
            Assert.True(u.Add(player, file, cursor, cursor + 1000, lines, last < now ? last : now).Ok);
        }
        return u;
    }

    static string Groups(IEnumerable<PartyGroup> groups) => string.Join(" | ", groups.Select(g => g.Leader + ": " + string.Join(", ", g.Names)));

    static string Changes(List<(DateTime Time, bool Active)> changes) =>
        string.Join(", ", changes.Select(c => (c.Active ? "+" : "-") + (c.Time - T0).TotalMinutes.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(3, "zzoega: zzatvo, zzoega")]
    [InlineData(12, "zzoega: bileh123, zzatvo, zzoega | zzoii: Trila, zzoii")]
    [InlineData(35, "zzatvo: bileh123, zzatvo, zzoega | zzoii: Trila, zzoii")]
    [InlineData(45, "zzatvo: zzatvo, zzoega | zzoii: Trila, zzoii")]
    [InlineData(55, "zzatvo: zzatvo, zzoega | zzoii: zzoii")]
    [InlineData(62, "zzatvo: zzatvo, zzoega | zzoii: zzoii")]
    [InlineData(70, "zzatvo: zzatvo, zzoega | zzoii: zzoii")]
    [InlineData(91, "zzatvo: zzatvo, zzoega | zzoii: zzoii")]
    public void Who_plays_with_whom(double minutes, string groups)
    {
        var now = At(minutes);
        Assert.Equal(groups, Groups(PartyTracker.Work(UploadsAt(now).Logs, Members, now).Groups));
    }

    [Fact]
    public void When_every_member_was_active()
    {
        var r = PartyTracker.Work(UploadsAt(At(91)).Logs, Members, At(91));
        Assert.Equal("+0", Changes(r.Changes["zzoega"]));
        Assert.Equal("+1.5", Changes(r.Changes["zzatvo"]));   // the crash at 60 and the relog at 64: not gone
        Assert.Equal("+5", Changes(r.Changes["zzoii"]));
        Assert.Equal("+10, -40", Changes(r.Changes["bileh123"]));
    }

    [Fact]
    public void A_party_made_from_an_invite_names_nobody()
    {
        // zzoega invites zzatvo: his game says "You have created party", zzatvo's "You have joined party" three seconds later (their clocks differ), and
        // neither log names the other (Frank's logs of 17 Sep 2026, 18:48)
        var u = new LogUploads();
        var mine = new GameLogWriter().Login("zzoega").Minutes(2).PartyCreated().Minutes(30).Noise();
        var his = new GameLogWriter().Login("zzatvo").Minutes(2).Seconds(3).PartyJoined().Minutes(30).Noise();
        Assert.True(u.Add("zzoega", mine.Lines[0][1..24] + "Z", 0, 1000, mine.Lines.ToList(), At(40)).Ok);   // named by the time of its first line, as LogSender names it
        Assert.True(u.Add("zzatvo", his.Lines[0][1..24] + "Z", 0, 1000, his.Lines.ToList(), At(40)).Ok);
        // A DIFFERENCE MEANT: before the party tracker this was two parties of one ("zzatvo: zzatvo | zzoega: zzoega"); the two lines seconds apart are one party
        Assert.Equal("zzoega: zzatvo, zzoega", Groups(PartyTracker.Work(u.Logs, Members, At(40)).Groups));
    }
}
