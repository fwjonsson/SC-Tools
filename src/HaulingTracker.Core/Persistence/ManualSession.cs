using System.Text.Json;

namespace HaulingTracker.Persistence;

/// <summary>A session you started (and stopped) by hand: it reads the logs from the start to the stop, or to now while it runs. It can go on for days.</summary>
public sealed class ManualSession
{
    public DateTime StartUtc { get; set; }
    public DateTime? StopUtc { get; set; }   // null = still running
}
