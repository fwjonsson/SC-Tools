namespace SCTools.Persistence;

/// <summary>
/// The folder of SC Tools on a PC, %AppData%\SCTools: the tracker's settings (tracker.json) and the Hauling Planner's (location.txt, data.json...). It was
/// %AppData%\HaulingTracker: the first time, that folder is copied here and kept as it was (a backup). When the copy cannot be made, the old folder is used.
/// </summary>
public static class AppFolder
{
    static string? _dir;

    public static string Dir => _dir ??= Make(Path.Combine(Root, "SCTools"), Path.Combine(Root, "HaulingTracker"));

    static string Root => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>The folder (dir), copied from the old one the first time. The tracker and the Hauling Planner may both do it at once: one copy wins.</summary>
    public static string Make(string dir, string old)
    {
        try
        {
            if (!Directory.Exists(dir) && Directory.Exists(old))
            {
                var moving = dir + ".moving-" + Guid.NewGuid().ToString("N");
                Copy(old, moving);
                try { Directory.Move(moving, dir); }
                catch (IOException) { Directory.Delete(moving, true); }   // the other program was first
            }
        }
        catch (Exception)
        {
            // no copy (a file in use, no room): the old folder below
        }
        return Directory.Exists(dir) || !Directory.Exists(old) ? dir : old;
    }

    static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var sub in Directory.GetDirectories(from)) Copy(sub, Path.Combine(to, Path.GetFileName(sub)));
    }
}
