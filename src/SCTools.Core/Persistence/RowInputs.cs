using System.Text.Json;

namespace SCTools.Persistence;

public sealed class RowInputs
{
    public string Worker { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Scu { get; set; } = "";
    public bool PartialPickup { get; set; }
}
