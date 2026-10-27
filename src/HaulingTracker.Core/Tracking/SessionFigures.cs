using System.Globalization;

namespace HaulingTracker.Tracking;

/// <summary>The numbers of the Session table: how long the session has lasted, what it earned per hour, and the batches and missions.</summary>
public sealed class SessionFigures
{
    readonly TrackerState _s;

    public SessionFigures(TrackerState state) => _s = state;

    /// <summary>
    /// How long the session lasts. With no start time typed: the time with activity (a stretch of an hour without any does not count, the clock just stops), up to
    /// now while the clocks are running. With a start time typed: from that time until now (while running) or the last activity.
    /// </summary>
    public TimeSpan? Elapsed()
    {
        var current = _s.Current;
        var summary = _s.Summary;
        if (current == null || summary == null) return null;
        DateTime start = summary.Start;
        if (_s.Inputs?.Start is { Length: > 0 } typed && TryTime(typed, out var st))
        {
            var local = DateTime.Now.Date + st;
            if (local > DateTime.Now) local = local.AddDays(-1);
            start = local.ToUniversalTime();
        }
        if (_s.Inputs?.Start is not { Length: > 0 })
        {
            // a session can go on for days: a stretch with no activity for an hour does not count (the clock just stops, the session goes on)
            var running = !current.Stop.HasValue && _s.ClocksRunning;
            return SessionBuilder.ActiveTime(current, running ? DateTime.UtcNow : null);
        }
        var active = _s.ClocksRunning && (!summary.End.HasValue || DateTime.UtcNow - summary.End.Value < SessionBuilder.ClockIdleLimit);
        var end = active ? DateTime.UtcNow : summary.End ?? start;
        var d = end - start;
        return d > TimeSpan.Zero ? d : TimeSpan.Zero;
    }

    /// <summary>aUEC per hour of what was earned over the time of the session, or nothing.</summary>
    public static string HourlyText(TimeSpan? elapsed, decimal earned) =>
        elapsed.HasValue && elapsed.Value.TotalHours > 0 && earned > 0 ? (earned / (decimal)elapsed.Value.TotalHours).ToString("N0") : "";

    /// <summary>Batches Delivered: the batches of finished runs (not failed ones) out of every pickup stop of the session, "7/12".</summary>
    public string BatchesText()
    {
        var totalBatches = _s.Rows.Sum(r => r.Missions);
        var inp = _s.Inputs;
        var doneBatches = _s.Trips.Where(t => t.End.HasValue && inp != null && !CargoRuns.RunFailed(t, inp)).Sum(t => t.Pickups.Sum(p => p.Missions));
        return _s.Summary == null || totalBatches == 0 ? "" : Math.Min(doneBatches, totalBatches) + "/" + totalBatches;
    }

    /// <summary>Missions Completed.</summary>
    public string CompletedText() => _s.Summary == null || _s.Summary.Total == 0 ? "" : _s.Summary.Completed.ToString(CultureInfo.InvariantCulture);

    /// <summary>Active Missions, with how many of them have cargo missing.</summary>
    public string ActiveText()
    {
        var summary = _s.Summary;
        if (summary == null || summary.Total == 0) return "";   // no missions yet: empty, as the other figures
        var missing = _s.Missing.Count;
        return summary.Active.ToString(CultureInfo.InvariantCulture) + (missing > 0 ? "  (" + (missing == summary.Active ? "" : missing + " ") + "Cargo Missing)" : "");
    }
}
