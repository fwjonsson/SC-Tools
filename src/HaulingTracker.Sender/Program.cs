using HaulingTracker.Updates;
using Microsoft.Win32;

namespace HaulingTracker.Sender;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // one tracker at a time (an update starts the new exe while the old one closes: it waits a moment for it). The name of the lock stays the old
        // one, so a tracker from before the new name and its update wait for each other too.
        using var single = new Mutex(false, "HaulingTrackerSender");
        bool mine;
        try
        {
            mine = single.WaitOne(TimeSpan.FromSeconds(15));
        }
        catch (AbandonedMutexException)
        {
            mine = true;
        }
        if (!mine) return;
        try
        {
            if (MovedToNewName()) return;   // started again under its new name
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp());
        }
        finally
        {
            single.ReleaseMutex();
        }
    }

    /// <summary>
    /// A tracker that is still HaulingTrackerSender.exe (it updated from before the new name) renames itself SCToolsTracker.exe (Windows lets a running exe
    /// be renamed), moves Start with Windows along, removes the backup of the old one, and starts again under the new name. True when it did.
    /// </summary>
    static bool MovedToNewName()
    {
        try
        {
            var me = Environment.ProcessPath;
            if (me == null || TrackerName.MovedPath(me) is not { } target || File.Exists(target)) return false;
            File.Move(me, target);
            TrayApp.MoveStartWithWindows(target);
            try { File.Delete(Path.ChangeExtension(me, ".old.exe")); }
            catch (Exception) { }   // the old tracker may still be closing: it goes with the next update
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target) ?? "" });
            return true;
        }
        catch (Exception)
        {
            return false;   // it keeps its old name and runs as it is
        }
    }
}
