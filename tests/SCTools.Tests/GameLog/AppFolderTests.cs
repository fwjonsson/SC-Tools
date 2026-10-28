namespace SCTools.Tests.GameLog;

/// <summary>%AppData%\SCTools: the first time it is a copy of %AppData%\HaulingTracker (the old one stays as it was).</summary>
public sealed class AppFolderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "sctools-folder-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    [Fact]
    public void The_old_folder_is_copied_the_first_time_and_kept()
    {
        var old = Path.Combine(_root, "HaulingTracker");
        var dir = Path.Combine(_root, "SCTools");
        Directory.CreateDirectory(Path.Combine(old, "Backups"));
        File.WriteAllText(Path.Combine(old, "sender.json"), "{\"Key\":\"abc\"}");
        File.WriteAllText(Path.Combine(old, "Backups", "data-2026-10-20.json"), "{}");

        Assert.Equal(dir, AppFolder.Make(dir, old));
        Assert.Equal("{\"Key\":\"abc\"}", File.ReadAllText(Path.Combine(dir, "sender.json")));
        Assert.True(File.Exists(Path.Combine(dir, "Backups", "data-2026-10-20.json")));
        Assert.True(File.Exists(Path.Combine(old, "sender.json")));   // the old one is a backup
        Assert.Equal(new[] { dir, old }.OrderBy(x => x), Directory.GetDirectories(_root).OrderBy(x => x));   // nothing half made left behind

        File.WriteAllText(Path.Combine(dir, "tracker.json"), "new");
        Assert.Equal(dir, AppFolder.Make(dir, old));   // the second time: nothing is copied again
        Assert.Equal("new", File.ReadAllText(Path.Combine(dir, "tracker.json")));
    }

    [Fact]
    public void Without_an_old_folder_it_is_the_new_one()
    {
        var dir = Path.Combine(_root, "SCTools");
        Assert.Equal(dir, AppFolder.Make(dir, Path.Combine(_root, "HaulingTracker")));
    }
}
