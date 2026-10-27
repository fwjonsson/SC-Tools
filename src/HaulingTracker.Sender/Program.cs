namespace HaulingTracker.Sender;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // one tracker at a time (an update starts the new exe while the old one closes: it waits a moment for it)
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
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp());
        }
        finally
        {
            single.ReleaseMutex();
        }
    }
}
