using System.Text;
using System.Text.RegularExpressions;

namespace SCTools.GameLog;

/// <summary>The server's side of the old logs, as the tracker sees it (HttpLogServer over the internet, or a fake in the tests).</summary>
public interface IHistoryServer
{
    /// <summary>Which of these backup logs (by FileId) the server does not have yet, at the History version.</summary>
    Task<IReadOnlyList<string>> MissingAsync(IReadOnlyList<string> files, CancellationToken ct);

    /// <summary>
    /// A piece of an old log: its kept lines from line number from on (the first piece from 0). The last piece says done, with the time of the last line of
    /// the whole file (when the game ended) and the build in the file's name.
    /// </summary>
    Task<UploadResult> HistoryAsync(string file, long from, IReadOnlyList<string> lines, bool done, DateTime? last, string? build, CancellationToken ct);
}

/// <summary>
/// The old logs: every backup log in logbackups that the server does not have yet is sent once, so the tools that keep everything (the Gauntlet Tracker,
/// the playtime) know what was played before the tracker ran. The server says which it has, so the first time all of them go and after that only the new
/// ones. Only the lines the tracker reads are sent (LineFilter). The server keeps them apart from the live logs: the Hauling Planner never sees them.
/// </summary>
public sealed class HistorySender
{
    /// <summary>The version of what an old log on the server holds. One more when the tracker reads more kinds of lines: the server then asks for them again.</summary>
    public const int Version = 1;

    /// <summary>The most lines in one piece.</summary>
    public int PieceLines { get; init; } = 10_000;

    static readonly Regex BuildInName = new(@"Build\((?<b>\d+)\)", RegexOptions.Compiled);

    readonly IHistoryServer _server;

    public HistorySender(IHistoryServer server) => _server = server;

    /// <summary>The Star Citizen folder (or a folder with a Game.log, or the Game.log itself).</summary>
    public string Path { get; set; } = "";

    /// <summary>Auto (the Game.log written last), or LIVE, HOTFIX, PTU...</summary>
    public string? Channel { get; set; } = "Auto";

    /// <summary>How many old logs the server did not have this time, and how many of those are sent.</summary>
    public int Missing { get; private set; }
    public int Sent { get; private set; }

    /// <summary>How many backup logs there are in all (null: not looked yet, or no Game.log found).</summary>
    public int? Found { get; private set; }

    /// <summary>Called before every old log is sent (Sent is how many went before it).</summary>
    public Action? Progress { get; set; }

    /// <summary>Sends every backup log the server does not have, oldest first.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var live = LogSources.ResolveLog(Path, Channel);
        if (live == null) return;
        var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(live) ?? "", "logbackups");
        if (!Directory.Exists(dir)) { Found = 0; return; }

        var files = new List<(string Id, string Path)>();
        foreach (var path in Directory.GetFiles(dir, "*.log"))
        {
            var id = LogSender.FileId(path);
            if (id != null) files.Add((id, path));
        }
        files = files.GroupBy(f => f.Id, StringComparer.Ordinal).Select(g => g.First()).OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
        Found = files.Count;

        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in files.Chunk(500))
            foreach (var id in await _server.MissingAsync(chunk.Select(f => f.Id).ToList(), ct)) missing.Add(id);
        Missing = missing.Count;
        Sent = 0;

        foreach (var (id, path) in files.Where(f => missing.Contains(f.Id)))
        {
            ct.ThrowIfCancellationRequested();
            Progress?.Invoke();
            await SendAsync(id, path, ct);
            Sent++;
        }
    }

    async Task SendAsync(string id, string path, CancellationToken ct)
    {
        var filter = new LineFilter();
        var kept = new List<string>();
        DateTime? last = null;
        string text;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(fs, Encoding.UTF8))
            text = await reader.ReadToEndAsync(ct);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (filter.Keep(line)) kept.Add(line);
            if (line.Length > 0 && line[0] == '<') last = HaulingLog.TimeOf(line) ?? last;
        }
        var b = BuildInName.Match(System.IO.Path.GetFileName(path));
        var build = b.Success ? b.Groups["b"].Value : null;

        long from = 0;
        while (true)
        {
            var piece = kept.Skip((int)from).Take(PieceLines).ToList();
            var done = from + piece.Count >= kept.Count;
            var result = await _server.HistoryAsync(id, from, piece, done, done ? last : null, done ? build : null, ct);
            if (!result.Ok)
            {
                if (result.At == from) throw new IOException("The server did not take " + id + ".");
                from = Math.Clamp(result.At, 0, kept.Count);   // the server knows better where this file is: go on from there
                continue;
            }
            if (done) return;
            from += piece.Count;
        }
    }
}
