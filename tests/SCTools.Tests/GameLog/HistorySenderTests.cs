using System.Text;
using SCTools.Tests.Support;

namespace SCTools.Tests.GameLog;

/// <summary>The old logs: every backup log the server does not have is sent once, in pieces of whole lines, only the lines the tracker reads.</summary>
public sealed class HistorySenderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "history-sender-" + Guid.NewGuid().ToString("N"));
    readonly string _backups;

    public HistorySenderTests()
    {
        var live = Path.Combine(_dir, "StarCitizen", "LIVE");
        _backups = Path.Combine(live, "logbackups");
        Directory.CreateDirectory(_backups);
        File.WriteAllText(Path.Combine(live, "Game.log"), Text(new GameLogWriter().At(GameLogWriter.Start.AddDays(3)).Login("zzoega").Lines));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    static string Text(IEnumerable<string> lines) => string.Join("\r\n", lines) + "\r\n";

    /// <summary>The server: what it has, and every piece it got.</summary>
    sealed class Server : IHistoryServer
    {
        public Dictionary<string, List<string>> Lines { get; } = new();
        public Dictionary<string, (DateTime? Last, string? Build)> Done { get; } = new();
        public List<(string File, long From, int Count)> Pieces { get; } = new();
        public int RefuseOnce = -1;   // a piece number the server refuses once (it lost the file): the tracker starts it again

        public Task<IReadOnlyList<string>> MissingAsync(IReadOnlyList<string> files, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(files.Where(f => !Done.ContainsKey(f)).ToList());

        public Task<UploadResult> HistoryAsync(string file, long from, IReadOnlyList<string> lines, bool done, DateTime? last, string? build, CancellationToken ct)
        {
            Pieces.Add((file, from, lines.Count));
            if (Pieces.Count - 1 == RefuseOnce)
            {
                Lines.Remove(file);
                return Task.FromResult(new UploadResult(false, 0));
            }
            var have = Lines.TryGetValue(file, out var l) ? l : Lines[file] = new List<string>();
            if (from != have.Count) return Task.FromResult(new UploadResult(false, have.Count));
            have.AddRange(lines);
            if (done) Done[file] = (last, build);
            return Task.FromResult(new UploadResult(true, have.Count));
        }
    }

    void Backup(string name, GameLogWriter w) => File.WriteAllText(Path.Combine(_backups, name), Text(w.Lines));

    HistorySender Sender(Server s, int piece = 10_000) => new(s) { Path = Path.Combine(_dir, "StarCitizen"), Channel = "Auto", PieceLines = piece };

    [Fact]
    public void Every_old_log_is_sent_once_with_its_end_and_build()
    {
        Backup("Game Build(9123456) 21 Oct 26 (08 00 00).log", new GameLogWriter().Login("zzoega").Minutes(30).Noise().Minutes(90).Noise());
        Backup("Game Build(9123456) 22 Oct 26 (08 00 00).log", new GameLogWriter().At(GameLogWriter.Start.AddDays(1)).Login("zzoega").Minutes(10).Noise());
        var server = new Server();

        var sender = Sender(server);
        sender.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal((2, 2, 2), (sender.Found!.Value, sender.Missing, sender.Sent));
        var first = LogSender.FileId(Path.Combine(_backups, "Game Build(9123456) 21 Oct 26 (08 00 00).log"))!;
        Assert.Equal(GameLogWriter.Start.AddMinutes(120), server.Done[first].Last);   // the last line of the log, also when it is not sent
        Assert.Equal("9123456", server.Done[first].Build);
        Assert.Single(server.Lines[first]);   // only the lines the tracker reads (the login), not the noise
        Assert.Contains("name zzoega", server.Lines[first][0]);

        var again = Sender(server);
        again.RunAsync(CancellationToken.None).GetAwaiter().GetResult();   // the next day: the server has them all
        Assert.Equal((0, 0), (again.Missing, again.Sent));
        Assert.Equal(2, server.Pieces.Count);

        Backup("Game Build(9123999) 23 Oct 26 (08 00 00).log", new GameLogWriter().At(GameLogWriter.Start.AddDays(2)).Login("zzoega"));
        var third = Sender(server);
        third.RunAsync(CancellationToken.None).GetAwaiter().GetResult();   // a new one: only that one
        Assert.Equal((3, 1, 1), (third.Found!.Value, third.Missing, third.Sent));
    }

    [Fact]
    public void A_long_log_goes_in_pieces_and_starts_again_when_the_server_lost_it()
    {
        var w = new GameLogWriter().Login("zzoega");
        for (var i = 0; i < 4; i++) w.Minutes(1).Login("zzoega");   // five lines the tracker reads
        Backup("Game Build(1) 21 Oct 26 (08 00 00).log", w);
        var server = new Server { RefuseOnce = 1 };

        Sender(server, piece: 2).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        var file = server.Done.Keys.Single();
        Assert.Equal(5, server.Lines[file].Count);
        Assert.Equal(new long[] { 0, 2, 0, 2, 4 }, server.Pieces.Select(p => p.From));   // the second piece was refused: from the start again
    }
}
