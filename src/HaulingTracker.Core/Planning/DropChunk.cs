namespace HaulingTracker.Planning;

/// <summary>
/// The load for ONE station: its drop-offs of the from-Ruin missions (no more than a ship holds). The waste of different missions looks the same, so a trip only
/// ever leaves Ruin with the load for its first stop (and unloads all of it there).
/// </summary>
public sealed class DropChunk
{
    public string Station = "";
    public int Batches;
    public int Scu;            // how many SCU the load is (0 = not known)
    public List<string> Keys = new();   // "mission id|objective id" of every drop-off in it, in the same order as Parts
    public List<int> Parts = new();   // the SCU of every drop-off in it, one by one (0 = not known): the containers are cut for each of them
    public string Cargo = "";
    public string[] Allowed = Array.Empty<string>();   // the workers who have that mission (empty = everybody)
}
