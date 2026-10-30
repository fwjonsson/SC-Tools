namespace SCTools.Planning;

/// <summary>What is already decided for one worker: his next stop, and the run he is flying.</summary>
public sealed class WorkerStart
{
    public string Worker = "";
    public string? FirstId;     // his next stop is locked to him: his first trip goes there first
    public int FirstBatches;    // the batches he takes there (they are not part of the batches left to divide)
    public List<(string Id, int Batches)> Keep = new();   // the stops of his planned trip after the first, kept for him too (Frank, 30 Oct 2026: a stop of his went to another worker's plan)
    public int Loaded;          // batches he carries already in a run that is not handed in yet: they take room in his first trip
    public int LoadedScu;       // the same in SCU (an estimate), when the plan fills the ships by SCU
    public string? From;        // where that run is now: his first trip continues from there instead of from Ruin
}
