using System.Text.Json;

namespace HaulingTracker.Persistence;

/// <summary>A run that was ended by hand (all cargo lost), or (Left) one that stopped because the worker left the party.</summary>
public sealed class ManualEnd
{
    public string Worker { get; set; } = "";
    public DateTime Time { get; set; }
    public bool Left { get; set; }   // ended because he left the party: the run just stops, it is not a failed run
}
