using System.Text.Json;

namespace HaulingTracker.Persistence;

public static class Storage
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HaulingTracker");

    // A small file next to the program's settings says where the data file is (for example a folder in OneDrive).
    static string PointerFile => Path.Combine(DefaultDir, "location.txt");

    static string? _path;

    /// <summary>Full path of the data file in use.</summary>
    public static string FilePath
    {
        get
        {
            if (_path != null) return _path;
            try
            {
                Directory.CreateDirectory(DefaultDir);
                if (File.Exists(PointerFile))
                {
                    var p = File.ReadAllText(PointerFile).Trim();
                    if (p.Length > 0)
                    {
                        _path = p;
                        return _path;
                    }
                }
            }
            catch (Exception)
            {
                // fall back to the normal place
            }
            _path = Path.Combine(DefaultDir, "data.json");
            return _path;
        }
    }

    /// <summary>Use this data file, without remembering it anywhere (the server says in its settings where its data is).</summary>
    public static void UseFile(string path) => _path = path;

    /// <summary>Use another data file from now on. It is remembered the next time the program starts.</summary>
    public static void SetLocation(string path)
    {
        Directory.CreateDirectory(DefaultDir);
        File.WriteAllText(PointerFile, path);
        _path = path;
    }

    /// <summary>Raised with a short text when the data file could not be saved (for example a OneDrive folder that is not available).</summary>
    public static event Action<string>? SaveFailed;

    /// <summary>Raised when the data file was saved (so a message about a failed save can go away).</summary>
    public static event Action? SaveOk;

    /// <summary>The Backups folder next to the data file.</summary>
    public static string BackupDir => Path.Combine(Path.GetDirectoryName(FilePath) ?? DefaultDir, "Backups");

    const int BackupsToKeep = 30;

    /// <summary>
    /// A dated copy of the data file (data-2026-10-20.json) made the first time the program starts each day, so it is the state
    /// from before you played today. The newest 30 copies are kept. A problem here never stops the program.
    /// </summary>
    static void Backup()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length == 0) return;
            Directory.CreateDirectory(BackupDir);
            var target = Path.Combine(BackupDir, "data-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json");
            if (!File.Exists(target)) File.Copy(FilePath, target);
            foreach (var old in Directory.GetFiles(BackupDir, "data-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(BackupsToKeep))
                File.Delete(old);
        }
        catch (Exception)
        {
            // no backup today, the data itself is untouched
        }
    }

    public static AppData Load()
    {
        Backup();
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
            // A damaged settings file should never stop the program from starting. Keep a copy so nothing is lost.
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch (Exception) { }
        }
        return new AppData();
    }

    public static void Save(AppData data)
    {
        try
        {
            var path = FilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // write to a temporary file first, so a sync client never sees a half written file
            var tmp = path + ".tmp";
            var json = JsonSerializer.Serialize(data, Options);
            // a sync client (OneDrive) can hold the file for a moment while it uploads it: "access denied". Try again a few times before giving up;
            // the data stays in memory, so the next save writes it anyway
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, path, true);
                    SaveOk?.Invoke();
                    return;
                }
                catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(150 * (attempt + 1));
                }
            }
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke(ex.Message);
        }
    }
}
