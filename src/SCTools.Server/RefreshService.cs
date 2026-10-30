using System.Diagnostics;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// Every two seconds: works out what the trackers uploaded (only when something new came in), moves the team status line on and makes data.json when anything
/// changed, then writes what changed to the database. Once a day a copy of the database is made. When the server stops, the last changes are written. The log
/// says how the ticks go: a slow one at once, and once an hour how many there were, how long they took and how often data.json was made.
/// </summary>
public sealed class RefreshService : BackgroundService
{
    readonly LiveTracker _live;
    readonly DataStore _store;
    readonly ServerPaths _paths;
    readonly GauntletService _gauntlet;
    readonly MissionLibrary _missions;
    readonly Playtime _playtime;
    readonly FleetService _fleet;
    readonly PlannerPositions _positions;
    readonly ILogger<RefreshService> _log;
    long _ticks, _totalMs, _slowestMs, _buildsReported;
    DateTime _reported = DateTime.UtcNow;

    /// <summary>A tick that takes this long is logged by itself (the server's log then says what is slow, and when).</summary>
    public static readonly TimeSpan SlowTick = TimeSpan.FromSeconds(1);

    public RefreshService(LiveTracker live, DataStore store, ServerPaths paths, GauntletService gauntlet, MissionLibrary missions, Playtime playtime,
        FleetService fleet, PlannerPositions positions, ILogger<RefreshService> log)
    {
        _fleet = fleet;
        _positions = positions;
        _gauntlet = gauntlet;
        _missions = missions;
        _playtime = playtime;
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
                var watch = Stopwatch.StartNew();
                Tick();
                Measure(watch.Elapsed);
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

    /// <summary>Keeps count of the ticks: a slow one goes to the log at once, and once an hour a line says how they went.</summary>
    void Measure(TimeSpan took)
    {
        var ms = (long)took.TotalMilliseconds;
        _ticks++;
        _totalMs += ms;
        if (ms > _slowestMs) _slowestMs = ms;
        if (took >= SlowTick) _log.LogWarning("The refresh took {Ms} ms", ms);
        var now = DateTime.UtcNow;
        if (now - _reported < TimeSpan.FromHours(1)) return;
        var builds = _live.Builds;
        _log.LogInformation("Refresh: {Ticks} ticks in the last hour, data.json made {Builds} times, {Average} ms on average, the slowest {Slowest} ms",
            _ticks, builds - _buildsReported, _ticks == 0 ? 0 : _totalMs / _ticks, _slowestMs);
        _buildsReported = builds;
        _ticks = 0;
        _totalMs = 0;
        _slowestMs = 0;
        _reported = now;
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
        try
        {
            _positions.Push(DateTime.UtcNow);   // where the map has everybody, for the Trip Planner's Current location
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Giving the Hauling Planner the map's places failed");
        }
        try
        {
            _fleet.SyncPlanner(DateTime.UtcNow);   // the ship of a quantum jump that just came in: the planner flies it
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Giving the Hauling Planner the ships of the fleets failed");
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
            _missions.Collect();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Saving the missions for the Mission Library failed");
        }
        try
        {
            _playtime.Collect();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Saving the games for the playtime failed");
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
