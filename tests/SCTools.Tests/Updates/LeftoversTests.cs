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
}
