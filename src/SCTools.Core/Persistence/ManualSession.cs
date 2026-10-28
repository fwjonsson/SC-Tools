using System.Text.Json;

namespace SCTools.Persistence;

/// <summary>
/// A session: it reads the logs from the start to the stop, or to now while it runs. Started and stopped by hand (the program's buttons), or by itself on the
/// server (Auto, see AutoSessions: it starts with the first contract accepted and stops after an hour without anything happening).
/// </summary>
public sealed class ManualSession
{
    public DateTime StartUtc { get; set; }
    public DateTime? StopUtc { get; set; }   // null = still running

    /// <summary>Started or stopped by AutoSessions: it may go on again when a log that came late shows it did not end.</summary>
    public bool Auto { get; set; }
}
