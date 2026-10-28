using System.Text.RegularExpressions;

namespace SCTools.Runs;

/// <summary>One run: from the previous hand-in (or the first accepted contract) to a hand-in at Ruin.</summary>
public sealed class Trip
{
    public int Number;
    public DateTime Start;      // UTC
    public DateTime? End;       // UTC, null = still on the way
    public List<TripPickup> Pickups = new();
    public int HandedIn;        // missions that had a hand-in in this run
}
