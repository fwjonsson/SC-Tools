using SCTools.Server.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>The admin's Scrip Value page ("Scrip" until 31 Oct 2026): what one MG scrip and one Council scrip are worth in aUEC for every tool. A field is saved as soon as it changes.</summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route("/admin/scrip")]
public sealed class ScripController : Controller
{
    readonly ScripValues _scrip;

    public ScripController(ScripValues scrip) => _scrip = scrip;

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Scrip Value";
        return View("~/Views/Scrip/Index.cshtml", _scrip);
    }

    [HttpPost("set")]
    public IActionResult Set(string field, string? value)
    {
        var key = field == "council" ? ScripValues.CouncilKey : field == "mg" ? ScripValues.MgKey : null;
        var text = (value ?? "").Replace(" ", "").Replace(",", "").Replace(".", "");
        if (key == null || !long.TryParse(text, out var n) || n < 0)
        {
            TempData["Message"] = "Not saved: a price is a whole number of aUEC, 0 or more.";
            return LocalRedirect("/admin/scrip");
        }
        _scrip.Set(key, n, DateTime.UtcNow);
        TempData["Message"] = (key == ScripValues.MgKey ? "MG scrip" : "Council scrip") + " is worth " + n.ToString("N0") + " aUEC now, in every tool.";
        return LocalRedirect("/admin/scrip");
    }
}
