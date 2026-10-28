namespace HaulingTracker.Updates;

/// <summary>
/// The tracker on the gaming PCs is the SC Tools Tracker (SCToolsTracker.exe, SCToolsTracker.zip in the release): it sends what every tool of the site
/// needs, not only the hauling tracker. It was HaulingTrackerSender.exe; the release still has HaulingTrackerSender.zip (the same tracker under its old
/// name) for the trackers that update from before, and such a tracker renames itself when it starts.
/// </summary>
public static class TrackerName
{
    public const string Name = "SC Tools Tracker";
    public const string Exe = "SCToolsTracker.exe";
    public const string Zip = "SCToolsTracker.zip";
    public const string OldExe = "HaulingTrackerSender.exe";

    /// <summary>Where a tracker that is still HaulingTrackerSender.exe moves to (SCToolsTracker.exe next to it), or null when it has its name already.</summary>
    public static string? MovedPath(string exePath) =>
        string.Equals(Path.GetFileName(exePath), OldExe, StringComparison.OrdinalIgnoreCase) ? Path.Combine(Path.GetDirectoryName(exePath) ?? "", Exe) : null;
}
