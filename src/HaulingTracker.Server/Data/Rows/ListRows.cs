namespace HaulingTracker.Server.Data;

/// <summary>A row of a list that keeps its order: its place in the list is its key.</summary>
public interface IListRow
{
    int Position { get; set; }
}

/// <summary>Someone joined (Active) or left the party at this time.</summary>
public sealed class PartyChangeRow : IListRow
{
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public DateTime TimeUtc { get; set; }
    public bool Active { get; set; }
}

/// <summary>A session started (and stopped) with the Start Session / Stop Session buttons.</summary>
public sealed class ManualSessionRow : IListRow
{
    public int Position { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime? StopUtc { get; set; }
}

/// <summary>A money transfer found in a log. Payment = ticked as paying off a debt.</summary>
public sealed class TransferRow : IListRow
{
    public int Position { get; set; }
    public DateTime Time { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Payment { get; set; }
}

/// <summary>A completed mission and how its payout was shared.</summary>
public sealed class PayoutRow : IListRow
{
    public int Position { get; set; }
    public string MissionId { get; set; } = "";
    public DateTime Time { get; set; }
    public string Contract { get; set; } = "";
    public decimal Payout { get; set; }
    public List<string> Members { get; set; } = new();
    public decimal Each { get; set; }
}
