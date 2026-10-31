using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// Map places (admin only): the places of the Map tool as the admin wants them (MapPlaces). What the map does not know in the logs (a quantum destination,
/// a start name) he says is a place, or not a place to show; a place gets a name of his own; and he adds places the game data does not have. Saved at once,
/// and the map uses it at once.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route("/admin/places")]
public sealed class MapPlacesController : Controller
{
    const string Back = "/admin/places";
    readonly MapPlaces _places;
    readonly UnknownPlaceAlerts _alerts;

    public MapPlacesController(MapPlaces places, UnknownPlaceAlerts alerts)
    {
        _places = places;
        _alerts = alerts;
    }

    /// <summary>The admin's own player (his login: zzoega), whose accounts the notices and the Companion's cards go by.</summary>
    string Admin => User.Identity?.Name ?? "";

    IActionResult Said(string message, string anchor = "")
    {
        TempData["Message"] = message;
        return LocalRedirect(Back + anchor);
    }

    /// <param name="system">the system whose places are listed (all when empty)</param>
    /// <param name="add">a text of the log to add a place for (the Add a place form is filled in for it)</param>
    [HttpGet("")]
    public IActionResult Index(string? system, string? add)
    {
        ViewData["Title"] = "Map places";
        var shown = StarMap.Systems.Any(s => s.Id == system) ? system! : "";
        var entries = _places.Entries().Where(e => shown.Length == 0 || e.Place.System == shown).ToList();
        return View(new MapPlacesPage(_places.Unknown(), _places.Aliases(), entries, _places.Atlas, shown, add?.Trim() ?? "", DateTime.UtcNow));
    }

    /// <param name="back">the page to go back to (the Companion's card of an unknown place without its script, #414); else Map places</param>
    [HttpPost("alias")]
    public IActionResult Alias(string? text, string? key, string? back)
    {
        var said = _places.Alias(text, key, DateTime.UtcNow);
        // the Companion's card sends it in the background (alerts.js): he stays where he is, the card says what was done (Frank, 31 Oct 2026)
        if (Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader)) return Json(new { said });
        if (back != null && Url.IsLocalUrl(back) && back != Back)
        {
            TempData["Message"] = said;
            return LocalRedirect(back);
        }
        return Said(said, "#unknown");
    }

    /// <summary>
    /// The notices of unknown places and the Companion's cards (UnknownPlaceAlerts.News; alerts.js asks every few seconds on every page the admin has open):
    /// after: the number of the last notice the page has (none the first time). With the cards, every place to pick, as Map places has them.
    /// </summary>
    [HttpGet("unknown.json")]
    public IActionResult News(int? after)
    {
        Response.Headers.CacheControl = "no-store";
        var news = _alerts.News(Admin, after, DateTime.UtcNow);
        var atlas = _places.Atlas;
        return Json(new
        {
            seq = news.Seq,
            alerts = news.Alerts.Select(a => new { seq = a.Seq, text = a.Text, kind = a.KindText, player = a.Player, his = a.His, system = a.System }),
            here = news.Here.Select(s => new { text = s.Text, kindKey = s.Kind.ToString(), kind = s.KindText, player = s.Player, when = s.TimeUtc, system = s.System }),
            places = news.Here.Count == 0 ? null : StarMap.Systems.Select(sys => new
            {
                name = sys.Name,
                places = atlas.Of(sys.Id).Where(p => p.Kind != PlaceKind.Star).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new { key = p.Key, name = p.Name + (p.Short != p.Name ? " (" + p.Short + ")" : "") }),
            }),
            notAPlace = MapPlaces.NotAPlace,
        });
    }

    /// <summary>Later on a card of the Companion: it goes until his next visit there; the place stays on Map places (sent in the background, alerts.js).</summary>
    [HttpPost("later")]
    public IActionResult Later(string? text, string? kind)
    {
        if (string.IsNullOrWhiteSpace(text) || !Enum.TryParse<UnknownKind>(kind, out var k)) return BadRequest();
        _alerts.Later(Admin, text, k);
        return Json(new { ok = true });
    }

    [HttpPost("unalias")]
    public IActionResult Unalias(int id) => Said(_places.Unalias(id), "#aliases");

    [HttpPost("rename")]
    public IActionResult Rename(string key, string field, string? value, string? back)
    {
        TempData["Message"] = _places.Rename(key ?? "", field ?? "", value, DateTime.UtcNow);
        return LocalRedirect(back is { Length: > 0 } && back.StartsWith(Back, StringComparison.Ordinal) ? back : Back + "#places");
    }

    [HttpPost("add")]
    public IActionResult Add(string? system, string? name, string? shortName, string? kind, string? at, string? x, string? y, string? alias)
    {
        var (said, key) = _places.Add(system, name, shortName, kind, at, x, y, DateTime.UtcNow);
        if (key != null && !string.IsNullOrWhiteSpace(alias)) said += " " + _places.Alias(alias, key, DateTime.UtcNow);   // added for a text of the log: it is that place
        return Said(said, key != null ? "#places" : "#add");
    }

    [HttpPost("remove")]
    public IActionResult Remove(string key) => Said(_places.Remove(key ?? ""), "#places");
}
