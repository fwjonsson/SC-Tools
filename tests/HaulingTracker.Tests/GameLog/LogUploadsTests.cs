using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.GameLog;

/// <summary>The logs the trackers upload to the server: in pieces from the server's cursor, one log per player, kept on disk for a restart.</summary>
public sealed class LogUploadsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hauling-uploads-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    static IReadOnlyList<string> Lines => PlayedSession.Writer().Lines;

    [Fact]
    public void A_piece_is_only_taken_where_the_last_one_ended()
    {
        var u = new LogUploads();
        var first = Lines.Take(6).ToList();
        Assert.Equal(new UploadResult(true, 400), u.Add("Pilot_A", "f1", 0, 400, first));
        Assert.Equal(new UploadResult(false, 400), u.Add("Pilot_A", "f1", 0, 400, first));    // sent twice (a tracker that restarted): refused
        Assert.Equal(new UploadResult(false, 400), u.Add("Pilot_A", "f1", 500, 600, first));  // a gap: refused, send from 400
        Assert.Equal(400, u.Cursor("PILOT_A", "f1"));   // names in any capitals
        Assert.Equal(0, u.Cursor("Pilot_A", "f2"));     // another log file starts at 0

        var before = u.Version;
        Assert.True(u.Add("Pilot_A", "f1", 400, 9000, Lines.Skip(6).ToList()).Ok);
        Assert.NotEqual(before, u.Version);
        var log = Assert.Single(u.Logs);
        Assert.Equal(PlayedSession.Player, log.Player);
        Assert.True(log.Missions.ContainsKey(PlayedSession.M2));
    }

    [Fact]
    public void Every_player_has_a_log_of_his_own()
    {
        var u = new LogUploads();
        u.Add("Pilot_A", "f", 0, 10, Lines.ToList());
        u.Add("Pilot_B", "f", 0, 10, new GameLogWriter().Login("Pilot_B").Lines.ToList());
        Assert.Equal(2, u.Logs.Count);
        Assert.Equal(10, u.Cursor("Pilot_B", "f"));
    }

    [Fact]
    public void What_was_uploaded_is_read_again_after_a_restart()
    {
        var u = new LogUploads(_dir);
        u.Add("Pilot_A", "2026-10-21T08:00:00.000Z", 0, 400, Lines.Take(6).ToList());
        u.Add("Pilot_A", "2026-10-21T08:00:00.000Z", 400, 9000, Lines.Skip(6).ToList());

        var again = new LogUploads(_dir);
        again.LoadKept(TimeSpan.FromHours(36));
        Assert.Equal(9000, again.Cursor("Pilot_A", "2026-10-21T08:00:00.000Z"));
        var log = Assert.Single(again.Logs);
        Assert.True(log.Missions.ContainsKey(PlayedSession.M2));
        Assert.Equal(new UploadResult(true, 9100), again.Add("Pilot_A", "2026-10-21T08:00:00.000Z", 9000, 9100, new List<string>()));   // and it goes on from there
    }

    [Fact]
    public void A_name_is_safe_as_a_file_name()
    {
        Assert.Equal("2026-10-21T08_00_00.000Z", LogUploads.SafeName("2026-10-21T08:00:00.000Z"));
        Assert.Equal(".._etc_passwd", LogUploads.SafeName("../etc/passwd"));   // no folder separators: it stays in the player's folder
    }
}
