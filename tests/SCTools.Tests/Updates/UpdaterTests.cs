using System.IO.Compression;
using SCTools.Updates;

namespace SCTools.Tests.Updates;

/// <summary>An update takes the new exe out of the downloaded zip, next to the program (Name.new.exe); a zip without the exe is refused.</summary>
public class UpdaterTests
{
    [Fact]
    public void The_exe_is_taken_out_of_the_zip_and_nothing_else()
    {
        var dir = Directory.CreateTempSubdirectory("sctools-update").FullName;
        try
        {
            var zip = Path.Combine(dir, "SCToolsTracker.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(archive.CreateEntry("SCToolsTracker.exe").Open())) w.Write("the new tracker");
                using (var w = new StreamWriter(archive.CreateEntry("notes.txt").Open())) w.Write("notes");
            }
            var target = Path.Combine(dir, "SCToolsTracker.new.exe");
            File.WriteAllText(target, "an earlier attempt");

            Assert.Equal(target, Updater.Unpack(zip, "SCToolsTracker.exe", target));
            Assert.Equal("the new tracker", File.ReadAllText(target));
            Assert.Equal(new[] { "SCToolsTracker.new.exe", "SCToolsTracker.zip" }, Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());

            var ex = Assert.Throws<FileNotFoundException>(() => Updater.Unpack(zip, "HaulingPlanner.exe", target));
            Assert.Contains("HaulingPlanner.exe", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
