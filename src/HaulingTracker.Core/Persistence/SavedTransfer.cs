using System.Text.Json;

namespace HaulingTracker.Persistence;

/// <summary>A money transfer found in a log. Payment = ticked by you as paying off a debt.</summary>
public sealed class SavedTransfer
{
    public DateTime Time { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Payment { get; set; }
}
