namespace SCTools.Parties;

/// <summary>
/// What the party tracker knows (PartyTracker.Work): the parties in the game now, every party before with who joined, left and led it, when every player was
/// active, and who has a tracker. Every tool asks this one answer, worked out once per refresh (LiveTracker.Parties).
/// </summary>
public sealed class PartyBook
{
    static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    /// <summary>The time it was worked out for.</summary>
    public DateTime Now { get; init; }

    /// <summary>
    /// Per player: when he became active and when not, in order (he was not active before the first one). Active is playing, or being in the party of
    /// somebody who plays (a player without a tracker is seen through the others' logs); gone for less than fifteen minutes (a crash, a relog) does not count.
    /// </summary>
    public Dictionary<string, List<(DateTime Time, bool Active)>> Changes { get; } = new(Same);

    /// <summary>The parties now: everybody playing (also players who are not in the Hauling Planner's party), each party with its leader, by leader.</summary>
    public List<PartyGroup> Groups { get; } = new();

    /// <summary>Every party the logs show, the oldest first; the ones going on now have no end.</summary>
    public List<PartyRecord> History { get; } = new();

    /// <summary>The players whose own log the server has (they run the SC Tools Tracker); the others are only known from the party lines of these.</summary>
    public HashSet<string> WithLog { get; } = new(Same);

    /// <summary>The latest time the logs showed each player: his own log's last line, a party line that names him, or being in a party of somebody playing.</summary>
    public Dictionary<string, DateTime> LastSeen { get; } = new(Same);

    /// <summary>Does this player run a tracker (the server has his own log)?</summary>
    public bool HasTracker(string name) => WithLog.Contains(name);

    /// <summary>The party this player is in now, or null when he is not playing.</summary>
    public PartyGroup? GroupOf(string name) => Groups.FirstOrDefault(g => g.Names.Any(n => Same.Equals(n, name)));

    /// <summary>What the logs say about this player at this time: active or not (see Changes).</summary>
    public bool ActiveAt(string member, DateTime utc)
    {
        var active = false;
        if (!Changes.TryGetValue(member, out var changes)) return false;
        foreach (var c in changes)
        {
            if (c.Time > utc) break;
            active = c.Active;
        }
        return active;
    }

    /// <summary>The party this player was in at this time, or null.</summary>
    public PartyRecord? PartyAt(string name, DateTime utc) => History.LastOrDefault(p => p.NamesAt(utc).Contains(name, Same));

    /// <summary>
    /// The players this one belongs with: himself, the party he is in now, and the groups he hauled with in the session (SessionInputs.Groups: the party he
    /// hauled with, also when one of them is not playing now). What a player may see of the others (the server's Audience) goes by it.
    /// </summary>
    public HashSet<string> Together(string name, IEnumerable<IEnumerable<string>>? hauled = null)
    {
        var set = new HashSet<string>(Same) { name };
        foreach (var g in Groups)
            if (g.Names.Any(n => PlayerNames.SameName(n, name))) set.UnionWith(g.Names);
        foreach (var g in hauled ?? Enumerable.Empty<IEnumerable<string>>())
        {
            var names = g.ToList();
            if (names.Any(n => PlayerNames.SameName(n, name))) set.UnionWith(names);
        }
        return set;
    }
}
