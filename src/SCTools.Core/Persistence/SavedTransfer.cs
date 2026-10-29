using System.Text.Json;

namespace SCTools.Persistence;

/// <summary>A money transfer found in a log. Payment = ticked by you as paying off a debt.</summary>
public sealed class SavedTransfer
{
    public DateTime Time { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Payment { get; set; }

    /// <summary>The transfer on a page (it has no id of its own): its time, who to whom, and the amount.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => Time.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + From + "|" + To + "|" + Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
