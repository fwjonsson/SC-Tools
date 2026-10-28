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

    public MapPlacesController(MapPlaces places) => _places = places;

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

    [HttpPost("alias")]
    public IActionResult Alias(string? text, string? key) => Said(_places.Alias(text, key, DateTime.UtcNow), "#unknown");

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
