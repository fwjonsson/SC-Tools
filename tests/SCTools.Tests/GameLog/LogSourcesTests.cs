using SCTools.Tests.Support;

namespace SCTools.Tests.GameLog;

/// <summary>Reading the Game.log files: opened by hand (one log per player) or followed live while the game writes them, with the backup logs.</summary>
public sealed class LogSourcesTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hauling-tests-" + Guid.NewGuid().ToString("N"));

    public LogSourcesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

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
}
