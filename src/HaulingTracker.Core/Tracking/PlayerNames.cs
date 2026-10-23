namespace HaulingTracker.Tracking;

/// <summary>Player names: the logs, the party table and what was typed can write the same name with other capitals.</summary>
public static class PlayerNames
{
    /// <summary>The same player (capitals do not matter).</summary>
    public static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
