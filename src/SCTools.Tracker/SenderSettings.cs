using System.Text.Json;
using SCTools.GameLog;
using SCTools.Persistence;

namespace SCTools.Tracker;

/// <summary>The tracker's settings, in %AppData%\SCTools\tracker.json (they were sender.json: read from there once).</summary>
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

    static string FilePath => Path.Combine(AppFolder.Dir, "tracker.json");
    static string OldFilePath => Path.Combine(AppFolder.Dir, "sender.json");

    public bool Complete => ServerUrl.Trim().Length > 0 && Key.Trim().Length > 0 && StarCitizenFolder.Trim().Length > 0;

    public static SenderSettings Load()
    {
        SenderSettings? settings = null;
        var fromOldFile = false;
        try
        {
            if (File.Exists(FilePath)) settings = JsonSerializer.Deserialize<SenderSettings>(File.ReadAllText(FilePath));
            else if (File.Exists(OldFilePath))
            {
                settings = JsonSerializer.Deserialize<SenderSettings>(File.ReadAllText(OldFilePath));
                fromOldFile = true;
            }
        }
        catch (Exception)
        {
            // a damaged settings file: start with the defaults, the key is asked again
        }
        if (settings == null) return new SenderSettings();

        // an old address of the site (haulingtracker, sct): the site's address now; saved, so the settings show it (and as tracker.json from sender.json)
        var moved = SiteAddress.Moved(settings.ServerUrl);
        if (moved != settings.ServerUrl || fromOldFile)
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
