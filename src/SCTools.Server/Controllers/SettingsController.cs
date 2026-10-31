using SCTools.Server.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The admin's Settings page (Frank, 31 Oct 2026; the Scrip Value page until then, "Scrip" before that): what one MG scrip and one Council scrip are worth
/// in aUEC for every tool (a field is saved as soon as it changes), and his lists of factions and categories the Mission Library's drop-downs pick from
/// (MissionLibrary.Choices: add one, or take one out; the box was above the library's table for a day). The old address /admin/scrip comes here.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route(Path)]
public sealed class SettingsController : Controller
{
    public const string Path = "/admin/settings";

    readonly ScripValues _scrip;
    readonly MissionLibrary _library;

    public SettingsController(ScripValues scrip, MissionLibrary library)
    {
        _scrip = scrip;
        _library = library;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Settings";
        ViewData["Factions"] = _library.Choices(MissionLibrary.FactionList);
        ViewData["Categories"] = _library.Choices(MissionLibrary.CategoryList);
        return View("~/Views/Settings/Index.cshtml", _scrip);
    }

    /// <summary>The Scrip Value page's address before 31 Oct 2026.</summary>
    [HttpGet("/admin/scrip")]
    public IActionResult OldScrip() => LocalRedirect(Path);

    [HttpPost("scrip")]
    public IActionResult Set(string field, string? value)
    {
        var key = field == "council" ? ScripValues.CouncilKey : field == "mg" ? ScripValues.MgKey : null;
        var text = (value ?? "").Replace(" ", "").Replace(",", "").Replace(".", "");
        if (key == null || !long.TryParse(text, out var n) || n < 0)
        {
            TempData["Message"] = "Not saved: a price is a whole number of aUEC, 0 or more.";
            return LocalRedirect(Path);
        }
        _scrip.Set(key, n, DateTime.UtcNow);
        TempData["Message"] = (key == ScripValues.MgKey ? "MG scrip" : "Council scrip") + " is worth " + n.ToString("N0") + " aUEC now, in every tool.";
        return LocalRedirect(Path);
    }

    /// <summary>Adds a faction or a category (list) to the lists the Mission Library's drop-downs pick from.</summary>
    [HttpPost("choices/add")]
    public IActionResult AddChoice(string? list, string? value)
    {
        if (!_library.AddChoice(list ?? "", value)) TempData["Message"] = "Not added: type a name.";
        return LocalRedirect(Path + "#lists");
    }

    /// <summary>Takes a faction or a category out of the lists (a mission that has it keeps it: it is only no longer offered).</summary>
    [HttpPost("choices/remove")]
    public IActionResult RemoveChoice(string? list, string? value)
    {
        _library.RemoveChoice(list ?? "", value);
        return LocalRedirect(Path + "#lists");
    }
}
