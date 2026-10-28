namespace SCTools.Gauntlet;

/// <summary>What one scenario of Gilly's Combat Gauntlet pays: aUEC and MG scrip.</summary>
public readonly record struct GauntletReward(long Auec, long Scrip);

/// <summary>The rewards of the eight scenarios (what the game pays when a run is completed) and the longest a run may take to count for the timings.</summary>
public static class GauntletRewards
{
    public const int Scenarios = 8;

    public static readonly IReadOnlyDictionary<int, GauntletReward> Defaults = new Dictionary<int, GauntletReward>
    {
        [1] = new(23_000, 1),
        [2] = new(36_000, 3),
        [3] = new(55_500, 5),
        [4] = new(79_750, 10),
        [5] = new(115_750, 12),
        [6] = new(171_250, 14),
        [7] = new(227_750, 16),
        [8] = new(349_750, 20),
    };

    /// <summary>A run longer than this is not timed (the reward still counts): 15 minutes, and 60 minutes for scenario 8.</summary>
    public static double MaxTimedSec(int scenario) => scenario == 8 ? 3600 : 900;

    /// <summary>The reward of a scenario: the player's own value when he set one, otherwise the default.</summary>
    public static GauntletReward Of(int scenario, IReadOnlyDictionary<int, GauntletReward>? custom)
    {
        if (custom != null && custom.TryGetValue(scenario, out var c)) return c;
        return Defaults.TryGetValue(scenario, out var d) ? d : default;
    }
}
