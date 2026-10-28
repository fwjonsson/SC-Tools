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

/// <summary>The admin's Gauntlet page: the rewards.</summary>
public sealed record GauntletAdminPage(IReadOnlyDictionary<int, GauntletReward> Rewards);

/// <summary>The admin's Gauntlet form: the rewards (aUEC and scrip, field names a1..a8 and s1..s8).</summary>
public sealed class GauntletOptionsForm
{
    public long A1 { get; set; } public long A2 { get; set; } public long A3 { get; set; } public long A4 { get; set; }
    public long A5 { get; set; } public long A6 { get; set; } public long A7 { get; set; } public long A8 { get; set; }
    public long S1 { get; set; } public long S2 { get; set; } public long S3 { get; set; } public long S4 { get; set; }
    public long S5 { get; set; } public long S6 { get; set; } public long S7 { get; set; } public long S8 { get; set; }

    public long[] Auec => new[] { A1, A2, A3, A4, A5, A6, A7, A8 };
    public long[] Scrip => new[] { S1, S2, S3, S4, S5, S6, S7, S8 };
}
