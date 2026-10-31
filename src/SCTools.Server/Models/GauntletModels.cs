namespace SCTools.Server.Models;

/// <summary>A grind session in the list: when it was, and what it earned.</summary>
public sealed record GrindLine(GauntletSessionStats Stats, bool Running);

/// <summary>
/// The Start time row of a session's table (Frank, 31 Oct 2026): the game or the grind session, the start the logs give it (From), the one it counts from
/// (Shown: the admin's when he set one), and whether the viewer may change it (the admin).
/// </summary>
public sealed record GauntletStartRow(string Kind, DateTime From, DateTime Shown, bool Editable)
{
    public bool Set => Shown != From;
}

/// <summary>Everything the Gauntlet Tracker pages show for one player: his in-game party's runs (Party: everybody in it; alone: himself), and the leaderboard.</summary>
public sealed class GauntletPage
{
    public string Tab { get; init; } = "session";
    public string Player { get; init; } = "";

    /// <summary>Not logged in: the Leaderboard is everybody's to see, the other tabs ask to log in.</summary>
    public bool Visitor { get; init; }

    /// <summary>Whose runs the Session, Stats and History tabs show: the viewer's in-game party now, or himself alone.</summary>
    public IReadOnlyList<string> Party { get; init; } = Array.Empty<string>();

    /// <summary>Whose run each mission is (by its id), for the top 10 of a party.</summary>
    public IReadOnlyDictionary<string, string> RunBy { get; init; } = new Dictionary<string, string>();

    /// <summary>The tabs show a party of more than one.</summary>
    public bool InParty => Party.Count > 1;
    public bool IsAdmin { get; init; }
    public double ScripValue { get; init; }
    public IReadOnlyDictionary<int, GauntletReward> Rewards { get; init; } = new Dictionary<int, GauntletReward>();
    public GauntletTotals Lifetime { get; init; } = new(0, 0, 0, 0, 0);
    public GauntletSessionStats? Game { get; init; }
    public GrindLine? Grind { get; init; }

    /// <summary>The starts the logs give the Game Session and the running Grind Session (the admin's Start time says which session it is for).</summary>
    public DateTime? GameFrom { get; init; }
    public DateTime? GrindFrom { get; init; }
    public IReadOnlyList<GrindLine> PastGrinds { get; init; } = Array.Empty<GrindLine>();
    public IReadOnlyList<GauntletScenarioStats> Scenarios { get; init; } = Array.Empty<GauntletScenarioStats>();
    public IReadOnlyList<GauntletScenarioStats> Ranking { get; init; } = Array.Empty<GauntletScenarioStats>();
    public IReadOnlyList<GauntletLeader> Leaderboard { get; init; } = Array.Empty<GauntletLeader>();
    public int Selected { get; init; } = 1;
    public int RunCount { get; init; }
}
