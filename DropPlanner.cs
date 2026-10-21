namespace HaulingTracker;

/// <summary>
/// The load of ONE from-Ruin mission for ONE station (no more than a ship holds). The waste of different missions looks the same, so a trip only ever leaves Ruin
/// with the load for its first stop.
/// </summary>
public sealed class DropChunk
{
    public string Station = "";
    public int Batches;
    public string Cargo = "";
    public string[] Allowed = Array.Empty<string>();   // the workers who have that mission (empty = everybody)
}
