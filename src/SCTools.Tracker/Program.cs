using SCTools.Updates;

namespace SCTools.Tracker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
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
            if (AppName.Tracker.MoveAndRestart(TrayApp.MoveStartWithWindows)) return;   // it was HaulingTrackerSender.exe: started again under its new name
            _ = AppName.Tracker.RemoveLeftoversAsync();   // SCToolsTracker.old.exe, the tracker before the last update
            _ = Task.Run(() => Updater.RemoveOldDownloads());   // the downloads of earlier updates still in Temp (45 MB each)
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp(args.Contains("--updated")));
        }
        finally
        {
            single.ReleaseMutex();
        }
    }
}
