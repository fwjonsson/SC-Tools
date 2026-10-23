using System.Text.Json;

namespace HaulingTracker.Persistence;

/// <summary>Someone joined (Active) or left the party at this time. Kept so mission payouts are split between who was there when they paid out.</summary>
public sealed class PartyChange
{
    public string Name { get; set; } = "";
    public DateTime TimeUtc { get; set; }
    public bool Active { get; set; }
}
