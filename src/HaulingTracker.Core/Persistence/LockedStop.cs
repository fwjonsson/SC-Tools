using System.Text.Json;

namespace HaulingTracker.Persistence;

/// <summary>One stop of a locked trip: the station (see PyroDistances) and the batches taken there.</summary>
public sealed class LockedStop
{
    public string Id { get; set; } = "";
    public int Batches { get; set; }
    public DateTime? Since { get; set; }   // set when the lock was made while the worker was flying a run: the time of his last pickup then (a pickup after it at another station ends the lock)
}
