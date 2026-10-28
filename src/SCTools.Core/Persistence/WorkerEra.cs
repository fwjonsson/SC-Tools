using System.Text.Json;

namespace SCTools.Persistence;

/// <summary>The workers assigned to the locations before a new set of missions was accepted. Kept so the old runs keep their workers.</summary>
public sealed class WorkerEra
{
    public DateTime Until { get; set; }   // UTC: pickups before this time belong to the old assignments
    public Dictionary<string, string> Workers { get; set; } = new();   // location id -> worker
}
