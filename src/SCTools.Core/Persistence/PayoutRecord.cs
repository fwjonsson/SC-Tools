using System.Text.Json;

namespace SCTools.Persistence;

/// <summary>A completed mission and how its payout was shared. Kept so the Mission Payouts list of the Debts tab survives the logs.</summary>
public sealed class PayoutRecord
{
    public string MissionId { get; set; } = "";
    public DateTime Time { get; set; }          // UTC: when the mission completed (paid out)
    public string Contract { get; set; } = "";  // for example "Potassium, 3 stops"
    public decimal Payout { get; set; }
    public List<string> Members { get; set; } = new();   // who was active and shared it
    public decimal Each { get; set; }
}
