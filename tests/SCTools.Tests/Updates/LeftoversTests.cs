using SCTools.Updates;

namespace SCTools.Tests.Updates;

/// <summary>An update keeps the program it replaced as Name.old.exe next to the exe: the new one removes it when it starts.</summary>
public class LeftoversTests
{
    [Fact]
    public async Task The_copies_an_update_left_go_when_the_program_starts_and_nothing_else()
    {
        var dir = Directory.CreateTempSubdirectory("sctools-leftovers").FullName;
        try
        {
            var exe = Path.Combine(dir, "SCToolsTracker.exe");
            foreach (var name in new[] { "SCToolsTracker.exe", "SCToolsTracker.old.exe", "HaulingTrackerSender.old.exe", "HaulingPlanner.old.exe", "notes.old.txt" })
                File.WriteAllText(Path.Combine(dir, name), "x");

            await AppName.Tracker.RemoveLeftoversAsync(exe, tries: 1);

            Assert.Equal(new[] { "HaulingPlanner.old.exe", "SCToolsTracker.exe", "notes.old.txt" },
                Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void The_downloads_of_earlier_updates_go_too_but_not_one_going_on_right_now()
    {
        var temp = Directory.CreateTempSubdirectory("sctools-temp").FullName;
        try
        {
            var old = Path.Combine(temp, "SCToolsUpdate-0123456789abcdef");
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old, "SCToolsTracker.zip"), "x");
            Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
            var fresh = Path.Combine(temp, "SCToolsUpdate-fedcba9876543210");
            Directory.CreateDirectory(fresh);
            File.WriteAllText(Path.Combine(fresh, "SCToolsTracker.zip"), "x");
            var other = Path.Combine(temp, "SomethingElse");
            Directory.CreateDirectory(other);
            Directory.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddDays(-2));

            Assert.Equal(1, Updater.RemoveOldDownloads(temp));

            Assert.Equal(new[] { "SCToolsUpdate-fedcba9876543210", "SomethingElse" }, Directory.GetDirectories(temp).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }
}
