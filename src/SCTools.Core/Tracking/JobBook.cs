using SCTools.Missions;

namespace SCTools.Tracking;

/// <summary>
/// What the Mission Library says about a kind of mission, for the hauling jobs: its SCU (typed in there: it wins), the SCU suggested last (by a hauler for a
/// mission of the kind, or what the game said for one) and who suggested it, and the aUEC it pays.
/// </summary>
public sealed record JobKind(long? Scu, long? Suggested, string? SuggestedBy, long? Auec)
{
    /// <summary>The library's SCU for a mission of this kind: the one typed in, else the suggestion.</summary>
    public long? ScuToUse => Scu ?? Suggested;
}

/// <summary>
/// What the site knows about the hauling jobs besides the logs: the SCU a hauler typed in for a mission (only that one, by mission id), the Mission Library's
/// kinds (by key, see MissionKinds.TitleKey), and the places of the map as the admin has them. The server makes a new one whenever one of them changes (it
/// is never changed itself, so the tracker can read it without a lock); the program has none.
/// </summary>
public sealed class JobBook
{
    readonly Func<Atlas> _atlas;

    public static readonly JobBook Empty = new(new Dictionary<string, long>(), new Dictionary<string, JobKind>(), () => Atlas.Default);

    public JobBook(IReadOnlyDictionary<string, long> missionScu, IReadOnlyDictionary<string, JobKind> kinds, Func<Atlas> atlas)
    {
        MissionScu = missionScu;
        Kinds = kinds;
        _atlas = atlas;
    }

    /// <summary>The SCU a hauler typed in for a mission, by mission id (small letters).</summary>
    public IReadOnlyDictionary<string, long> MissionScu { get; }

    /// <summary>The Mission Library's kinds, by key.</summary>
    public IReadOnlyDictionary<string, JobKind> Kinds { get; }

    /// <summary>The places of the map now.</summary>
    public Atlas Atlas => _atlas();

    /// <summary>The SCU a hauler typed in for this mission, or null.</summary>
    public long? HaulerScu(Mission m) => MissionScu.TryGetValue(m.Id.ToLowerInvariant(), out var s) ? s : null;

    /// <summary>The library's kind of this mission (by its title), or null.</summary>
    public JobKind? KindOf(Mission m) => MissionKinds.TitleKey(m.Title) is { } key && Kinds.TryGetValue(key, out var k) ? k : null;
}
