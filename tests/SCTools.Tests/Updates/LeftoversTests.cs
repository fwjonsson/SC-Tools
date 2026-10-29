using SCTools.Updates;

namespace SCTools.Tests.Updates;

/// <summary>
/// An update keeps the program it replaced as Name.old.exe next to the exe, and leaves the new one as Name.new.exe when it got no further; the updates before
/// 29 Oct 2026 left a folder with the zip and the exe in Temp. The new program removes them when it starts.
/// </summary>
public class LeftoversTests
{
    [Fact]
    public async Task The_copies_an_update_left_go_when_the_program_starts_and_nothing_else()
    {
        var dir = Directory.CreateTempSubdirectory("sctools-leftovers").FullName;
        try
        {
            var exe = Path.Combine(dir, "SCToolsTracker.exe");
            foreach (var name in new[] { "SCToolsTracker.exe", "SCToolsTracker.old.exe", "HaulingTrackerSender.old.exe", "SCToolsTracker.new.exe", "HaulingPlanner.old.exe", "notes.old.txt" })
                File.WriteAllText(Path.Combine(dir, name), "x");

            await AppName.Tracker.RemoveLeftoversAsync(exe, tries: 1, tempPath: Path.Combine(dir, "no-such-temp"));

            Assert.Equal(new[] { "HaulingPlanner.old.exe", "SCToolsTracker.exe", "notes.old.txt" },
                Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task What_updates_left_in_temp_goes_too_once_it_is_a_day_old()
    {
        var dir = Directory.CreateTempSubdirectory("sctools-leftovers").FullName;
        try
        {
            var temp = Path.Combine(dir, "Temp");
            var stale = DateTime.UtcNow.AddDays(-2);
            var folder = Directory.CreateDirectory(Path.Combine(temp, "SCToolsUpdate-0123456789abcdef0123456789abcdef")).FullName;   // an update before 29 Oct 2026
            File.WriteAllText(Path.Combine(folder, "SCToolsTracker.zip"), "zip");
            File.WriteAllText(Path.Combine(folder, "SCToolsTracker.exe"), "exe");
            Directory.SetLastWriteTimeUtc(folder, stale);
            var zip = Path.Combine(temp, "SCToolsUpdate-fedcba9876543210fedcba9876543210.zip");   // an update that was cut short
            File.WriteAllText(zip, "zip");
            File.SetLastWriteTimeUtc(zip, stale);
            File.WriteAllText(Path.Combine(temp, "SCToolsUpdate-00000000000000000000000000000000.zip"), "zip");   // an update running right now
            var other = Directory.CreateDirectory(Path.Combine(temp, "SomethingElse-0123")).FullName;
            Directory.SetLastWriteTimeUtc(other, stale);

            await AppName.Tracker.RemoveLeftoversAsync(Path.Combine(dir, "SCToolsTracker.exe"), tries: 1, tempPath: temp);

            Assert.Equal(new[] { "SCToolsUpdate-00000000000000000000000000000000.zip", "SomethingElse-0123" },
                Directory.GetFileSystemEntries(temp).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
