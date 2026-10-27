namespace HaulingTracker.Server;

/// <summary>Every two seconds: works out what the trackers uploaded (only when something new came in), moves the team status line on and makes data.json.</summary>
public sealed class RefreshService : BackgroundService
{
    readonly LiveTracker _live;
    readonly ILogger<RefreshService> _log;

    public RefreshService(LiveTracker live, ILogger<RefreshService> log)
    {
        _live = live;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            try
            {
                _live.Refresh(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Working out the logs failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stop));
    }
}
