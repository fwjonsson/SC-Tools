using System.Text.Json;

namespace HaulingTracker.Sender;

/// <summary>The tracker's settings, in %AppData%\HaulingTracker\sender.json.</summary>
public sealed class SenderSettings
{
    public const string DefaultServer = "https://haulingtracker.fwjonsson.dev";
    public const string DefaultFolder = @"C:\Program Files\Roberts Space Industries\StarCitizen";

    /// <summary>The address of the tracker website.</summary>
    public string ServerUrl { get; set; } = DefaultServer;

    /// <summary>The key Frank gave you: it says the logs are yours.</summary>
    public string Key { get; set; } = "";

    /// <summary>The Star Citizen folder (with LIVE, HOTFIX... in it), or a folder with a Game.log.</summary>
    public string StarCitizenFolder { get; set; } = DefaultFolder;

    /// <summary>Auto (the Game.log written last), or LIVE, HOTFIX, PTU...</summary>
    public string Channel { get; set; } = "Auto";

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HaulingTracker", "sender.json");

    public bool Complete => ServerUrl.Trim().Length > 0 && Key.Trim().Length > 0 && StarCitizenFolder.Trim().Length > 0;

    public static SenderSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<SenderSettings>(File.ReadAllText(FilePath)) ?? new SenderSettings();
        }
        catch (Exception)
        {
            // a damaged settings file: start with the defaults, the key is asked again
        }
        return new SenderSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }

    public SenderSettings Copy() => (SenderSettings)MemberwiseClone();
}
