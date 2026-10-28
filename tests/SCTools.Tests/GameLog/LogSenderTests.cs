using System.Net;
using System.Text;
using SCTools.Tests.Support;

namespace SCTools.Tests.GameLog;

/// <summary>The sending side of a tracker: the Game.log and the backup logs uploaded from the server's cursor, in whole lines, nothing twice.</summary>
public sealed class LogSenderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hauling-sender-" + Guid.NewGuid().ToString("N"));
    readonly string _live;
    readonly Server _server = new();

    public LogSenderTests()
    {
        _live = Path.Combine(_dir, "StarCitizen", "LIVE");
        Directory.CreateDirectory(Path.Combine(_live, "logbackups"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    /// <summary>The server, as its own LogUploads: every upload is Pilot_A's.</summary>
    sealed class Server : ILogServer
    {
        public LogUploads Uploads { get; } = new();
        public int Pieces;
        public List<DateTime?> Lasts { get; } = new();
        public Task<long> CursorAsync(string file, CancellationToken ct) => Task.FromResult(Uploads.Cursor("Pilot_A", file));
        public Task<UploadResult> UploadAsync(string file, long from, long to, IReadOnlyList<string> lines, DateTime? last, CancellationToken ct)
        {
            Pieces++;
            Lasts.Add(last);
            return Task.FromResult(Uploads.Add("Pilot_A", file, from, to, lines, last));
        }
        public HaulingLog Log => Uploads.Logs.Single();
    }

    /// <summary>The played session takes cargo out of a freight elevator three times (Gaslight, Patch City, Gaslight): a line read twice would count four.</summary>
    const int SessionUnstows = 3;

    LogSender Sender() => new(_server) { Path = Path.Combine(_dir, "StarCitizen"), Channel = "Auto" };

    static string Text(IEnumerable<string> lines) => string.Join("\r\n", lines) + "\r\n";

    string GameLog => Path.Combine(_live, "Game.log");

    [Fact]
    public void The_backup_logs_of_the_last_day_and_the_game_log_are_sent()
    {
        var older = GameLogWriter.MissionId(81);
        var backup = new GameLogWriter().At(GameLogWriter.Start.AddHours(-2)).Login("Pilot_A").Accepted(older)
            .Marker(older, string.Format(GameLogWriter.HaulingContract, 2), "pickup_o_0", "900000008101", GameLogWriter.RodsFuel);
        File.WriteAllText(Path.Combine(_live, "logbackups", "Game Build(1) 21 Oct 26 (06 00 00).log"), Text(backup.Lines));
        File.WriteAllText(GameLog, Text(PlayedSession.Writer().Lines));

        var sender = Sender();
        sender.TickAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(GameLog, sender.LiveFile);
        Assert.True(_server.Log.Missions.ContainsKey(older));             // from the backup log
        Assert.True(_server.Log.Missions.ContainsKey(PlayedSession.M2));  // from the Game.log
        Assert.True(sender.LinesSent < PlayedSession.Writer().Lines.Count + backup.Lines.Count + 1);
        Assert.Equal(new FileInfo(GameLog).Length, _server.Uploads.Cursor("Pilot_A", LogSender.FileId(GameLog)!));

        var pieces = _server.Pieces;
        sender.TickAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(pieces, _server.Pieces);   // nothing new: nothing sent
    }

    [Fact]
    public void A_line_the_game_is_still_writing_waits_for_the_rest()
    {
        var lines = PlayedSession.Writer().Lines;
        var head = Text(lines.Take(3));
        File.WriteAllText(GameLog, head + lines[3][..20]);   // half of the fourth line
        var sender = Sender();
        sender.TickAsync(CancellationToken.None).GetAwaiter().GetResult();
        var id = LogSender.FileId(GameLog)!;
        Assert.Equal(Encoding.UTF8.GetByteCount(head), _server.Uploads.Cursor("Pilot_A", id));

        File.AppendAllText(GameLog, lines[3][20..] + "\r\n" + Text(lines.Skip(4)));
        sender.TickAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(new FileInfo(GameLog).Length, _server.Uploads.Cursor("Pilot_A", id));
        Assert.True(_server.Log.Missions.ContainsKey(PlayedSession.M2));
        Assert.Equal(SessionUnstows, _server.Log.Unstows.Count);   // every line once
    }

    [Fact]
    public void When_the_game_starts_again_its_old_log_is_not_sent_twice()
    {
        var first = PlayedSession.Writer();
        File.WriteAllText(GameLog, Text(first.Lines));
        var sender = Sender();
        sender.TickAsync(CancellationToken.None).GetAwaiter().GetResult();

        // the game wrote one more line, closed, and its Game.log went to logbackups; the new game has a new Game.log
        File.AppendAllText(GameLog, Text(new GameLogWriter().At(first.Now.AddMinutes(1)).Unstow().Lines));
        File.Move(GameLog, Path.Combine(_live, "logbackups", "Game Build(1) 21 Oct 26 (09 30 00).log"));
        File.WriteAllText(GameLog, Text(new GameLogWriter().At(first.Now.AddHours(1)).Login("Pilot_A").Unstow().Lines));
        sender.TickAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(SessionUnstows + 1 + 1, _server.Log.Unstows.Count);   // the session's, the last line of the old log, the new log's one: none twice
    }

    [Fact]
    public void A_tracker_that_starts_again_goes_on_where_the_server_is()
    {
        var lines = PlayedSession.Writer().Lines;
        File.WriteAllText(GameLog, Text(lines));
        Sender().TickAsync(CancellationToken.None).GetAwaiter().GetResult();
        File.AppendAllText(GameLog, Text(new GameLogWriter().At(PlayedSession.LastPickup.AddMinutes(1)).Unstow().Lines));

        Sender().TickAsync(CancellationToken.None).GetAwaiter().GetResult();   // a new tracker (restarted): it asks the server how far the file is
        Assert.Equal(SessionUnstows + 1, _server.Log.Unstows.Count);
    }

    [Fact]
    public void The_time_of_the_last_line_goes_with_the_piece_also_when_that_line_is_not_sent()
    {
        var w = PlayedSession.Writer();
        w.Minutes(7).Noise();   // the game goes on writing: the tracker does not send this line, but its time says the game still ran
        File.WriteAllText(GameLog, Text(w.Lines));
        Sender().TickAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(w.Now, _server.Lasts[^1]);
        Assert.Equal(new GameRun(GameLogWriter.Start, w.Now), _server.Log.GameRuns[LogSender.FileId(GameLog)!]);
    }

    [Fact]
    public void A_file_is_named_by_the_time_of_its_first_line()
    {
        File.WriteAllText(GameLog, Text(PlayedSession.Writer().Lines));
        Assert.Equal("2026-10-21T08:00:00.000Z", LogSender.FileId(GameLog));
        File.WriteAllText(GameLog, "");
        Assert.Null(LogSender.FileId(GameLog));   // a new Game.log the game has not written to yet
    }

    sealed class Stub : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        public readonly List<string> Bodies = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Answer = _ => new HttpResponseMessage(HttpStatusCode.OK);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct));
            return Answer(request);
        }
    }

    static HttpResponseMessage Json(HttpStatusCode code, string json) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public void The_server_is_asked_with_the_key_and_a_refused_piece_says_where_to_go_on()
    {
        var stub = new Stub();
        using var server = new HttpLogServer("https://example.test/", "k3y-k3y-k3y-k3y-k3y", stub);

        stub.Answer = _ => Json(HttpStatusCode.OK, "{\"at\":120}");
        Assert.Equal(120, server.CursorAsync("2026-10-21T08:00:00.000Z", CancellationToken.None).GetAwaiter().GetResult());
        Assert.Equal("https://example.test/api/log/cursor?file=2026-10-21T08%3A00%3A00.000Z", stub.Requests[^1].RequestUri!.ToString());
        Assert.Equal("k3y-k3y-k3y-k3y-k3y", stub.Requests[^1].Headers.GetValues("X-Tracker-Key").Single());

        stub.Answer = _ => Json(HttpStatusCode.Conflict, "{\"at\":300}");
        Assert.Equal(new UploadResult(false, 300), server.UploadAsync("f", 120, 200, new[] { "x" }, null, CancellationToken.None).GetAwaiter().GetResult());

        stub.Answer = _ => Json(HttpStatusCode.OK, "{\"at\":200}");
        var last = new DateTime(2026, 10, 21, 8, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new UploadResult(true, 200), server.UploadAsync("f", 120, 200, new[] { "x" }, last, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("\"last\":\"2026-10-21T08:30:00Z\"", stub.Bodies[^1]);   // the time of the piece's last line goes with it

        stub.Answer = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        Assert.Null(server.PlayerAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Throws<UnauthorizedAccessException>(() => server.CursorAsync("f", CancellationToken.None).GetAwaiter().GetResult());

        stub.Answer = _ => Json(HttpStatusCode.OK, "{\"player\":\"zzoega\"}");
        Assert.Equal("zzoega", server.PlayerAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    [Fact]
    public void Open_my_page_gets_a_login_link_from_the_server()
    {
        var stub = new Stub();
        using var server = new HttpLogServer("https://example.test", "k3y-k3y-k3y-k3y-k3y", stub);
        stub.Answer = _ => Json(HttpStatusCode.OK, "{\"url\":\"/login/tracker?token=abc\"}");
        Assert.Equal("https://example.test/login/tracker?token=abc", server.LoginLinkAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Equal(HttpMethod.Post, stub.Requests[^1].Method);
        Assert.Equal("https://example.test/api/login-link", stub.Requests[^1].RequestUri!.ToString());

        stub.Answer = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        Assert.Null(server.LoginLinkAsync(CancellationToken.None).GetAwaiter().GetResult());
    }
}
