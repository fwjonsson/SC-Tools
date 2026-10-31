using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// What every page of the site asks for itself, not one tool: whether the site answers (/live, the live dot of the top bar, live.js) and the team status
/// line (/status.json, the line that rolls in the gameplay tools' row of tabs, ticker.js). Both were the Hauling Planner's until 31 Oct 2026 (Frank: "a better
/// place than coming from hauling's class").
/// </summary>
public sealed class SiteStatusController : Controller
{
    readonly LiveTracker _live;
    readonly Audience _audience;

    public SiteStatusController(LiveTracker live, Audience audience)
    {
        _live = live;
        _audience = audience;
    }

    /// <summary>The site answers: nothing else. A page that does not poll data of its own asks this every few seconds for its live dot.</summary>
    [HttpGet("/live")]
    public IActionResult Live()
    {
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    /// <summary>
    /// The team status line: it changes every few seconds while somebody plays, and some of its lines are clocks ("has spent 12m 04s on this trip"), so it has
    /// an address of its own (in data.json it made the whole data be made anew every few seconds for one sentence). It is about the session's players: only
    /// the admin and a player whose party is the whole of the session's get it (Frank, 30 Oct 2026); anybody else an empty line.
    /// </summary>
    [HttpGet("/status.json")]
    public IActionResult Status()
    {
        Response.Headers.CacheControl = "no-store";
        var viewer = Audience.Of(HttpContext);
        var (status, key) = _live.Read(t => (t.Status.Text, t.Status.Seq));
        if (!viewer.Everything)
        {
            var allowed = _audience.Allowed(viewer, DateTime.UtcNow)!;
            var mine = allowed.Count > 0 && _live.Read(t => t.State.Current != null && t.State.Data.Party.Count > 0 && t.State.Data.Party.All(n => allowed.Contains(n)));
            if (!mine) status = "";
        }
        return Json(new { status, key });
    }
}
