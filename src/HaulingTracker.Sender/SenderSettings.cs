using System.Text.Json;
using HaulingTracker.GameLog;

namespace HaulingTracker.Sender;

/// <summary>The tracker's settings, in %AppData%\HaulingTracker\sender.json.</summary>
public sealed class SenderSettings
{
    public const string DefaultServer = SiteAddress.Current;
    public const string DefaultFolder = @"C:\Program Files\Roberts Space Industries\StarCitizen";

    /// <summary>The address of the site (SC Tools), where the logs go.</summary>
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
        SenderSettings? settings = null;
        try
        {
            if (File.Exists(FilePath)) settings = JsonSerializer.Deserialize<SenderSettings>(File.ReadAllText(FilePath));
        }
        catch (Exception)
        {
            // a damaged settings file: start with the defaults, the key is asked again
        }
        if (settings == null) return new SenderSettings();

        // the site's old address (haulingtracker.fwjonsson.dev): the site's address now, saved so the settings show it
        var moved = SiteAddress.Moved(settings.ServerUrl);
        if (moved != settings.ServerUrl)
        {
            settings.ServerUrl = moved;
            try { settings.Save(); }
            catch (Exception) { }   // not saved: moved again the next time
        }
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }

    public SenderSettings Copy() => (SenderSettings)MemberwiseClone();
}
