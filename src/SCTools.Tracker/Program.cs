using SCTools.Updates;

namespace SCTools.Tracker;

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
            if (AppName.Tracker.MoveAndRestart(TrayApp.MoveStartWithWindows)) return;   // it was HaulingTrackerSender.exe: started again under its new name
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp());
        }
        finally
        {
            single.ReleaseMutex();
        }
    }
}
