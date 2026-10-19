using System.Text.Json;

namespace HaulingTracker;

public sealed class RewardRow
{
    public decimal Reward { get; set; }
    public int Missions { get; set; }
}

public sealed class RowInputs
{
    public string Worker { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Scu { get; set; } = "";
    public bool PartialPickup { get; set; }
}

/// <summary>What you typed yourself for one session.</summary>
public sealed class SessionInputs
{
    public string? Start { get; set; }     // null = use the time from the log
    public string? End { get; set; }       // null = use the time from the log
    public List<RewardRow> Rewards { get; set; } = new();   // old format, no longer used
    public decimal Reward2 { get; set; }   // payout of a 2 stop contract
    public decimal Reward3 { get; set; }   // payout of a 3 stop contract
    public List<string> LostRuns { get; set; } = new();   // runs ticked as "lost cargo"
    public Dictionary<string, RowInputs> Rows { get; set; } = new();   // key = pickup location id
    public Dictionary<string, decimal> Carried { get; set; } = new();   // worker -> value of the locations he carried (saved for the Debts tab)
    public List<string> MissionIds { get; set; } = new();
    public List<string>? Party { get; set; }   // who was in the party this session (null = everyone who carried something), set with "Session party…"
    public List<string> Members { get; set; } = new();   // the party of the session as used for the split (saved with Carried)   // missions of the session, to avoid counting a session twice
}

/// <summary>A money transfer found in a log. Payment = ticked by you as paying off a debt.</summary>
public sealed class SavedTransfer
{
    public DateTime Time { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Payment { get; set; }
}

public sealed class AppData
{
    public string? GameLogPath { get; set; }
    public List<string> Party { get; set; } = new() { "zzoega", "zzatvo", "bildog123" };
    public string? Channel { get; set; }   // Auto, LIVE, HOTFIX...
    public List<string> WatchPaths { get; set; } = new();   // Game.log files to watch live, also on other computers
    public Dictionary<string, string> LocationNames { get; set; } = new();   // location id -> your name
    public Dictionary<string, SessionInputs> Sessions { get; set; } = new();
    public List<SavedTransfer> Transfers { get; set; } = new();
    public Dictionary<string, decimal> PairAdjust { get; set; } = new();   // "a|b" (a before b) -> amount b owes a, typed by you
}

public static class Storage
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    static string FilePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HaulingTracker");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "data.json");
        }
    }

    public static AppData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath), Options);
                if (data != null) return data;
            }
        }
        catch (Exception)
        {
            // A damaged settings file should never stop the program from starting.
        }
        return new AppData();
    }

    public static void Save(AppData data)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Options));
        }
        catch (Exception)
        {
            // Saving is best effort.
        }
    }
}
