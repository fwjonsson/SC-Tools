namespace SCTools;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (AppName.Planner.MoveAndRestart()) return;   // it was HaulingTracker.exe: started again under its new name
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
