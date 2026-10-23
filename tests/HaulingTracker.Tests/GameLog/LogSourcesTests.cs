using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.GameLog;

/// <summary>Reading the Game.log files: opened by hand (one log per player) or followed live while the game writes them, with the backup logs.</summary>
public sealed class LogSourcesTests : IDisposable
{
    static readonly string M = GameLogWriter.MissionId(51);
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hauling-tests-" + Guid.NewGuid().ToString("N"));

    public LogSourcesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    static string Text(GameLogWriter w) => string.Join("\r\n", w.Lines) + "\r\n";   // the game writes Windows line ends

    static GameLogWriter Played(string player) => new GameLogWriter().Login(player).Minutes(1).Accepted(M)
        .Marker(M, string.Format(GameLogWriter.HaulingContract, 2), "pickup_m_0", "900000005101", GameLogWriter.Gaslight);

    string Folder(params string[] parts)
    {
        var dir = Path.Combine(new[] { _dir }.Concat(parts).ToArray());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void A_watch_path_is_a_game_log_a_folder_with_one_or_the_StarCitizen_folder()
    {
        var root = Folder("StarCitizen");
        var live = Path.Combine(Folder("StarCitizen", "LIVE"), "Game.log");
        var hotfix = Path.Combine(Folder("StarCitizen", "HOTFIX"), "Game.log");
        File.WriteAllText(live, "");
        File.WriteAllText(hotfix, "");
        File.SetLastWriteTimeUtc(live, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(hotfix, DateTime.UtcNow);

        Assert.Equal(hotfix, LogSources.ResolveLog(root, "Auto"));     // the one written last
        Assert.Equal(hotfix, LogSources.ResolveLog(root, null));
        Assert.Equal(live, LogSources.ResolveLog(root, "live"));
        Assert.Null(LogSources.ResolveLog(root, "PTU"));               // that channel has no log
        Assert.Equal(live, LogSources.ResolveLog(Path.GetDirectoryName(live)!, "Auto"));
        Assert.Equal(live, LogSources.ResolveLog(live, "PTU"));        // a file is that file
        Assert.Null(LogSources.ResolveLog(Path.Combine(_dir, "nowhere"), "Auto"));
    }

    [Fact]
    public void A_live_source_reads_what_the_game_writes_and_waits_for_the_rest_of_a_line()
    {
        var file = Path.Combine(Folder("live"), "Game.log");
        var lines = Played("Pilot_A").Lines;
        File.WriteAllText(file, lines[0] + "\r\n" + lines[1][..20]);   // the login, and half of the next line

        var logs = new LogSources();
        logs.Watch(new[] { file });
        logs.Poll("Auto");
        var log = Assert.Single(logs.Logs);
        Assert.Equal("Pilot_A", log.Player);
        Assert.Empty(log.Missions);

        File.AppendAllText(file, lines[1][20..] + "\r\n" + lines[2] + "\r\n");
        var before = logs.Version;
        logs.Poll("Auto");
        Assert.True(log.Missions.ContainsKey(M));
        Assert.NotEqual(before, logs.Version);
        Assert.Equal(log.LastTime, logs.LastLine);
    }

    [Fact]
    public void A_new_game_log_is_read_from_the_top()
    {
        var file = Path.Combine(Folder("new"), "Game.log");
        File.WriteAllText(file, Text(Played("Pilot_A")));
        var logs = new LogSources();
        logs.Watch(new[] { file });
        logs.Poll(null);

        var other = GameLogWriter.MissionId(52);
        var w = new GameLogWriter().Minutes(90).Accepted(other).Marker(other, string.Format(GameLogWriter.HaulingContract, 2), "pickup_n_0", "900000005102", GameLogWriter.PatchCity);
        File.WriteAllText(file, Text(w));   // the game started again: a shorter, new Game.log
        logs.Poll(null);
        var log = Assert.Single(logs.Logs);
        Assert.True(log.Missions.ContainsKey(other));
        Assert.True(log.Missions.ContainsKey(M));   // what was read before is kept
    }

    [Fact]
    public void The_backup_logs_of_the_last_day_are_read_when_a_source_starts()
    {
        var dir = Folder("backfill");
        var backups = Folder("backfill", "logbackups");
        var recent = Path.Combine(backups, "Game Build(1) 21 Oct 26 (08 00 00).log");
        var old = Path.Combine(backups, "Game Build(1) 18 Oct 26 (08 00 00).log");
        File.WriteAllText(recent, Text(Played("Pilot_A")));
        var oldMission = GameLogWriter.MissionId(53);
        File.WriteAllText(old, Text(new GameLogWriter().Accepted(oldMission).Marker(oldMission, string.Format(GameLogWriter.HaulingContract, 2), "pickup_o_0", "900000005103", GameLogWriter.RodsFuel)));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
        File.WriteAllText(Path.Combine(dir, "Game.log"), "");

        var logs = new LogSources();
        logs.Watch(new[] { dir });
        logs.Poll("Auto");
        var log = Assert.Single(logs.Logs);
        Assert.True(log.Missions.ContainsKey(M));            // from the backup of today
        Assert.False(log.Missions.ContainsKey(oldMission));  // older than a day
    }

    [Fact]
    public void Log_files_opened_by_hand_are_one_log_per_player()
    {
        var a1 = Path.Combine(_dir, "a1.log");
        var a2 = Path.Combine(_dir, "a2.log");
        var b = Path.Combine(_dir, "b.log");
        File.WriteAllText(a1, Text(Played("Pilot_A")));
        File.WriteAllText(a2, Text(new GameLogWriter().Minutes(120).Login("Pilot_A")));
        File.WriteAllText(b, Text(Played("Pilot_B")));

        var logs = new LogSources();
        logs.LoadFiles(new[] { b, a2, a1 });
        Assert.Equal(2, logs.Logs.Count);
        Assert.Equal(new[] { "Pilot_A", "Pilot_B" }, logs.Logs.Select(l => l.Player).OrderBy(p => p));

        logs.Clear();
        Assert.Empty(logs.Logs);
        Assert.Equal(DateTime.MinValue, logs.LastLine);
    }
}
