using SCTools.Server.Accounts;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// A piece of a log file: its name (the time of its first line), the byte positions in the file it covers, its lines, and the time of its last line (also
/// of one that is not sent; an older tracker does not send it).
/// </summary>
public sealed record LogPiece(string File, long From, long To, List<string>? Lines, DateTime? Last = null);

/// <summary>The names of a tracker's backup logs (the time of each one's first line), to hear which the server does not have.</summary>
public sealed record HistoryList(List<string>? Files);

/// <summary>A piece of an old log (HistorySender): its lines from line number From on; the last piece is Done, with the time of the log's last line and its build.</summary>
public sealed record HistoryPiece(string File, long From, List<string>? Lines, bool Done, DateTime? Last = null, string? Build = null);

/// <summary>
/// Where the trackers upload their logs. Every request has the tracker's key in the X-Tracker-Key header: it says whose log it is. A tracker asks how far a
/// file is uploaded (cursor) and sends the rest from there; a piece that does not start at the cursor is refused (409) with the cursor to start from.
/// </summary>
[ApiController]
[Route("api")]
public sealed class LogController : ControllerBase
{
    readonly LogUploads _uploads;
    readonly TrackerKeys _keys;
    readonly LoginLinks _links;
    readonly HistoryStore _history;

    public LogController(LogUploads uploads, TrackerKeys keys, LoginLinks links, HistoryStore history)
    {
        _uploads = uploads;
        _keys = keys;
        _links = links;
        _history = history;
    }

    string? Player() => _keys.PlayerOf(Request.Headers["X-Tracker-Key"].FirstOrDefault());

    /// <summary>Whose key this is (so a tracker can tell its settings are right).</summary>
    [HttpGet("me")]
    public IActionResult Me() => Player() is { } player ? Ok(new { player }) : Unauthorized();

    /// <summary>A link that logs the player of this key in once, within two minutes ("Open my page" in the tracker). It is relative: the tracker puts its server in front.</summary>
    [HttpPost("login-link")]
    public IActionResult LoginLink() =>
        Player() is { } player ? Ok(new { url = "/login/tracker?token=" + _links.Make(player, DateTime.UtcNow) }) : Unauthorized();

    /// <summary>How far this log file is uploaded.</summary>
    [HttpGet("log/cursor")]
    public IActionResult Cursor([FromQuery] string file)
    {
        if (Player() is not { } player) return Unauthorized();
        if (string.IsNullOrWhiteSpace(file)) return BadRequest();
        return Ok(new { file, at = _uploads.Cursor(player, file) });
    }

    /// <summary>A piece of a log file.</summary>
    [HttpPost("log")]
    [RequestSizeLimit(8_000_000)]
    public IActionResult Upload([FromBody] LogPiece piece)
    {
        if (Player() is not { } player) return Unauthorized();
        if (string.IsNullOrWhiteSpace(piece.File)) return BadRequest();
        var result = _uploads.Add(player, piece.File, piece.From, piece.To, piece.Lines ?? new List<string>(), piece.Last?.ToUniversalTime());
        return result.Ok ? Ok(new { at = result.At }) : Conflict(new { at = result.At });
    }

    /// <summary>Which of these old logs the server does not have yet (the tracker sends those, once).</summary>
    [HttpPost("history/missing")]
    public IActionResult HistoryMissing([FromBody] HistoryList list)
    {
        if (Player() is not { } player) return Unauthorized();
        return Ok(new { version = HistorySender.Version, missing = _history.Missing(player, list.Files ?? new List<string>()) });
    }

    /// <summary>A piece of an old log; a piece that does not start where the server is gets 409 with where to start.</summary>
    [HttpPost("history")]
    [RequestSizeLimit(16_000_000)]
    public IActionResult History([FromBody] HistoryPiece piece)
    {
        if (Player() is not { } player) return Unauthorized();
        if (string.IsNullOrWhiteSpace(piece.File) || piece.From < 0) return BadRequest();
        try
        {
            var result = _history.Add(player, piece.File, piece.From, piece.Lines ?? new List<string>(), piece.Done, piece.Last?.ToUniversalTime(), piece.Build, DateTime.UtcNow);
            return result.Ok ? Ok(new { at = result.At }) : Conflict(new { at = result.At });
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }
    }
}
