namespace SCTools.Planning;

/// <summary>What is already decided for one worker: his next stop, and the run he is flying.</summary>
public sealed class WorkerStart
{
    public string Worker = "";
    public string? FirstId;     // his next stop is locked to him (a group key, see TripPlanner.GroupKey): his first trip goes there first
    public int FirstBatches;    // the batches he takes there (they are not part of the batches left to divide)
    public int Loaded;          // batches he carries already in a run that is not handed in yet: they take room in his first trip
    public int LoadedScu;       // the same in SCU (an estimate), when the plan fills the ships by SCU
    public string? From;        // where that run is now: his first trip continues from there instead of from Ruin
    public List<string> LoadedTo = new();   // where the cargo he carries goes (stations; empty with cargo aboard = Ruin): his first trip ends there (stage 1b)

    /// <summary>The places the cargo aboard goes to: LoadedTo, or Ruin when he carries something and nobody said where it goes.</summary>
    public IEnumerable<string> Destinations => LoadedTo.Count > 0 ? LoadedTo : Loaded > 0 || LoadedScu > 0 ? new[] { PyroDistances.Ruin } : Array.Empty<string>();
}
