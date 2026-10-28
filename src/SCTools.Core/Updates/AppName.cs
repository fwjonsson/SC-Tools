using System.Diagnostics;

namespace SCTools.Updates;

/// <summary>
/// A program of SC Tools in the release: what it is called, its exe and its zip, and the exe it was before the rename. The release still has a zip under
/// the old name (the same program), so a program from before updates as it always did, and then renames itself when it starts (MoveAndRestart).
/// </summary>
public sealed record AppName(string Title, string Name, string OldName)
{
    /// <summary>The SC Tools Tracker on the gaming PCs: it sends what every tool of the site needs. It was HaulingTrackerSender.</summary>
    public static readonly AppName Tracker = new("SC Tools Tracker", "SCToolsTracker", "HaulingTrackerSender");

    /// <summary>The Hauling Planner, the Windows program. It was HaulingTracker.</summary>
    public static readonly AppName Planner = new("Hauling Planner", "HaulingPlanner", "HaulingTracker");

    public string Exe => Name + ".exe";
    public string Zip => Name + ".zip";
    public string OldExe => OldName + ".exe";

    /// <summary>Where a program that is still under its old name moves to (the new name next to it), or null when it has its name already.</summary>
    public string? MovedPath(string exePath) =>
        string.Equals(Path.GetFileName(exePath), OldExe, StringComparison.OrdinalIgnoreCase) ? Path.Combine(Path.GetDirectoryName(exePath) ?? "", Exe) : null;

    /// <summary>
    /// A program still under its old name renames itself (Windows lets a running exe be renamed), says so (moved: Start with Windows goes along), removes
    /// the backup of the old one, and starts again under the new name. True when it did: the caller then ends. Anything in the way: it keeps its old name.
    /// </summary>
    public bool MoveAndRestart(Action<string>? moved = null)
    {
        try
        {
            var me = Environment.ProcessPath;
            if (me == null || MovedPath(me) is not { } target || File.Exists(target)) return false;
            File.Move(me, target);
            moved?.Invoke(target);
            try { File.Delete(Path.ChangeExtension(me, ".old.exe")); }
            catch (Exception) { }   // the old one may still be closing: it goes with the next update
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target) ?? "" });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
