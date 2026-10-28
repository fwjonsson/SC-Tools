namespace SCTools.Server.Models;

/// <summary>A grind session in the list: when it was, and what it earned.</summary>
public sealed record GrindLine(GauntletSessionStats Stats, bool Running);

/// <summary>Everything the Gauntlet Tracker pages show for one player.</summary>
public sealed class GauntletPage
{
    public string Tab { get; init; } = "session";
    public string Player { get; init; } = "";
    public bool IsAdmin { get; init; }
    public double ScripValue { get; init; }
    public IReadOnlyDictionary<int, GauntletReward> Rewards { get; init; } = new Dictionary<int, GauntletReward>();
    public GauntletTotals Lifetime { get; init; } = new(0, 0, 0, 0, 0);
    public GauntletSessionStats? Game { get; init; }
    public GrindLine? Grind { get; init; }
    public IReadOnlyList<GrindLine> PastGrinds { get; init; } = Array.Empty<GrindLine>();
    public IReadOnlyList<GauntletScenarioStats> Scenarios { get; init; } = Array.Empty<GauntletScenarioStats>();
    public IReadOnlyList<GauntletScenarioStats> Ranking { get; init; } = Array.Empty<GauntletScenarioStats>();
    public IReadOnlyList<GauntletLeader> Leaderboard { get; init; } = Array.Empty<GauntletLeader>();
    public int Selected { get; init; } = 1;
    public int RunCount { get; init; }
}

/// <summary>The admin's Gauntlet page: the rewards (from the Mission Library).</summary>
public sealed record GauntletAdminPage(IReadOnlyDictionary<int, GauntletReward> Rewards);
