using HaulingTracker.Server.Data;

namespace HaulingTracker.Server;

/// <summary>
/// Every two seconds: works out what the trackers uploaded (only when something new came in), moves the team status line on and makes data.json, then writes
/// what changed to the database. Once a day a copy of the database is made. When the server stops, the last changes are written.
/// </summary>
public sealed class RefreshService : BackgroundService
{
    readonly LiveTracker _live;
    readonly DataStore _store;
    readonly ServerPaths _paths;
    readonly GauntletService _gauntlet;
    readonly ILogger<RefreshService> _log;

    public RefreshService(LiveTracker live, DataStore store, ServerPaths paths, GauntletService gauntlet, ILogger<RefreshService> log)
    {
        _gauntlet = gauntlet;
        _live = live;
        _store = store;
        _paths = paths;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                Tick();
            }
            while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException)
        {
            // the server stops
        }
        finally
        {
            Save();
        }
    }

    void Tick()
    {
        try
        {
            _live.Refresh(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Working out the logs failed");
        }
        Save();
        try
        {
            _gauntlet.Collect();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Saving the gauntlet runs failed");
        }
        try
        {
            if (_store.BackupDaily(_paths.BackupDir, DateTime.Now)) _log.LogInformation("Made the daily copy of the database in {Dir}", _paths.BackupDir);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "The daily copy of the database failed");
        }
    }

    /// <summary>Writes what changed to the database (under the lock of the data, so nothing changes it meanwhile).</summary>
    void Save()
    {
        try
        {
            lock (_live.Uploads.Sync) _store.Flush(_live.Tracker.State.Data);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Writing to the database failed (tried again in two seconds)");
        }
    }
}
