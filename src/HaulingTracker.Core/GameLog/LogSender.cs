using System.Globalization;
using System.Text;

namespace HaulingTracker.GameLog;

/// <summary>The server's side of an upload, as the tracker sees it (HttpLogServer over the internet, or the server's own LogUploads in the tests).</summary>
public interface ILogServer
{
    /// <summary>How far this log file is uploaded.</summary>
    Task<long> CursorAsync(string file, CancellationToken ct);

    /// <summary>A piece of a log file: the lines the tracker reads between the byte positions from and to.</summary>
    Task<UploadResult> UploadAsync(string file, long from, long to, IReadOnlyList<string> lines, CancellationToken ct);
}

/// <summary>
/// The sending side of a tracker: follows the Game.log of a Star Citizen folder (LIVE, HOTFIX... as LogSources picks it) while the game writes it, and the backup
/// logs of the last day, and uploads every file from where the server's cursor is, in pieces of whole lines (a line the game is still writing waits). Only the
/// lines the tracker reads are sent (LineFilter). A file is named by the time of its first line, so when the game starts again and its Game.log moves to
/// logbackups, that copy goes on where the upload was and is not sent twice.
/// </summary>
public sealed class LogSender
{
    /// <summary>The most bytes of a log file in one piece.</summary>
    public const int PieceBytes = 512 * 1024;

    sealed class FileState
    {
        public long At;
        public LineFilter Filter = new();
    }

    readonly ILogServer _server;
    readonly Dictionary<string, FileState> _files = new(StringComparer.Ordinal);
    readonly HashSet<string> _finished = new(StringComparer.Ordinal);   // backup logs sent completely (they do not grow any more)

    public LogSender(ILogServer server) => _server = server;

    /// <summary>The Star Citizen folder (or a folder with a Game.log, or the Game.log itself).</summary>
    public string Path { get; set; } = "";

    /// <summary>Auto (the Game.log written last), or LIVE, HOTFIX, PTU...</summary>
    public string? Channel { get; set; } = "Auto";

    /// <summary>The Game.log being followed (null: none found).</summary>
    public string? LiveFile { get; private set; }

    /// <summary>When a piece was last taken by the server (UTC), and how many lines were sent since the tracker started.</summary>
    public DateTime? LastUpload { get; private set; }
    public int LinesSent { get; private set; }

    /// <summary>The name of a log file for the server: the time of its first line ("2026-10-23T13:09:38.304Z"), or null while it has none.</summary>
    public static string? FileId(string path) =>
        HaulingLog.FirstTimestamp(path)?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>Sends what is new: the backup logs of the last day first (in the order they were written), then the Game.log.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        LiveFile = LogSources.ResolveLog(Path, Channel);
        if (LiveFile == null) return;
        foreach (var backup in Backups(LiveFile)) await SendAsync(backup, false, ct);
        await SendAsync(LiveFile, true, ct);
    }

    /// <summary>The logs in the logbackups folder next to the Game.log that were written in the last day, oldest first.</summary>
    static IEnumerable<string> Backups(string liveFile)
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(liveFile) ?? "", "logbackups");
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            var since = DateTime.UtcNow - LogSources.BackupAge;
            return Directory.GetFiles(dir, "*.log")
                .Where(f => File.GetLastWriteTimeUtc(f) >= since)
                .OrderBy(f => HaulingLog.FirstTimestamp(f) ?? DateTime.MaxValue)
                .ToList();
        }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    async Task SendAsync(string path, bool live, CancellationToken ct)
    {
        var id = FileId(path);
        if (id == null || (!live && _finished.Contains(id))) return;
        if (!_files.TryGetValue(id, out var state))
        {
            state = new FileState { At = await _server.CursorAsync(id, ct) };
            _files[id] = state;
        }

        long length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            length = fs.Length;
            var buffer = new byte[PieceBytes];
            while (state.At < length)
            {
                fs.Seek(state.At, SeekOrigin.Begin);
                var read = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(PieceBytes, length - state.At)), ct);
                if (read <= 0) break;
                var end = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
                if (end < 0)
                {
                    if (read < PieceBytes) break;   // the game is still writing this line: it goes with the next piece
                    end = read - 1;                 // a line longer than a piece (never seen): send it as it is
                }
                var consumed = end + 1;
                var lines = Encoding.UTF8.GetString(buffer, 0, consumed).Split('\n');
                var kept = new List<string>();
                for (var i = 0; i < lines.Length - 1; i++)   // the last part is empty: the piece ends with a line end
                {
                    var line = lines[i].TrimEnd('\r');
                    if (state.Filter.Keep(line)) kept.Add(line);
                }
                var result = await _server.UploadAsync(id, state.At, state.At + consumed, kept, ct);
                if (result.Ok)
                {
                    LinesSent += kept.Count;
                    LastUpload = DateTime.UtcNow;
                }
                else
                {
                    state.Filter = new LineFilter();   // the server knows better where this file is: go on from there
                }
                state.At = result.At;
            }
        }
        if (!live && state.At >= length) _finished.Add(id);
    }
}
