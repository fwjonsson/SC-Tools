namespace SCTools;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (AppName.Planner.MoveAndRestart()) return;   // it was HaulingTracker.exe: started again under its new name
        _ = AppName.Planner.RemoveLeftoversAsync();   // HaulingPlanner.old.exe, the program before the last update
        _ = Task.Run(() => Updater.RemoveOldDownloads());   // the downloads of earlier updates still in Temp (45 MB each)
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
