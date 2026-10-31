namespace SCTools.Parties;

/// <summary>
/// One party from when it was made to when it ended, as the logs show it (PartyTracker): who was in it and when, who led it, and how it ended. A party is two
/// players or more; one who is left alone ends it.
/// </summary>
public sealed class PartyRecord
{
    static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    internal PartyRecord(int id, DateTime started, string leader, IEnumerable<string> names)
    {
        Id = id;
        Started = started;
        Leader = leader;
        Names = names.ToList();
    }

    /// <summary>Its number, in the order the parties were made (1 is the oldest the logs show).</summary>
    public int Id { get; }

    /// <summary>When it was made (the first log that showed it together).</summary>
    public DateTime Started { get; }

    /// <summary>When it ended, or null while it goes on.</summary>
    public DateTime? Ended { get; internal set; }

    /// <summary>The game said "The party has been disbanded." when it ended (else everybody left it, or stopped playing).</summary>
    public bool Disbanded { get; internal set; }

    /// <summary>Its leader now, or when it ended.</summary>
    public string Leader { get; internal set; }

    /// <summary>Who is in it now, or was when it ended.</summary>
    public List<string> Names { get; internal set; }

    /// <summary>What happened in it, in order: the first members joining when it was made (its leader first, with his Leader), who joined and left later, the new leaders, whose game went out and came back.</summary>
    public List<PartyNews> News { get; } = new();

    /// <summary>"zzoega's party".</summary>
    public string Title => Leader + "'s party";

    /// <summary>Everybody who was ever in it, in the order they joined.</summary>
    public List<string> Everybody => News.Where(n => n.Kind == PartyNewsKind.Joined).Select(n => n.Name).Distinct(Same).ToList();

    /// <summary>Was this player ever in it?</summary>
    public bool Had(string name) => News.Any(n => n.Kind == PartyNewsKind.Joined && Same.Equals(n.Name, name));

    /// <summary>Who was in it at this time (empty before it was made and after it ended).</summary>
    public List<string> NamesAt(DateTime utc)
    {
        var names = new List<string>();
        if (utc < Started || utc >= Ended) return names;
        foreach (var n in News.TakeWhile(n => n.Time <= utc))
        {
            if (n.Kind == PartyNewsKind.Joined && !names.Contains(n.Name, Same)) names.Add(n.Name);
            else if (n.Kind == PartyNewsKind.Left) names.RemoveAll(x => Same.Equals(x, n.Name));
        }
        return names;
    }

    /// <summary>Who led it at this time.</summary>
    public string LeaderAt(DateTime utc) => News.LastOrDefault(n => n.Kind == PartyNewsKind.Leader && n.Time <= utc)?.Name ?? Leader;
}
