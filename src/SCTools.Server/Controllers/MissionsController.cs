using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Mission Library: every kind of mission the players took (from their logs), where a logged in player types in what the log does not say: the name,
/// the aUEC, MG scrip and Council scrip it pays, the giver, notes. Every field is saved as soon as it changes (autosave.js).
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[AutoValidateAntiforgeryToken]
[Route("/missions")]
public sealed class MissionsController : Controller
{
    readonly MissionLibrary _library;
    readonly ToolAccess _tools;

    public MissionsController(MissionLibrary library, ToolAccess tools)
    {
        _library = library;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <param name="show">all, or todo: the ones without rewards yet</param>
    /// <param name="q">a part of the name, the giver or the ContractId</param>
    [HttpGet("")]
    public IActionResult Index(string? show, string? q)
    {
        if (!_tools.Sees(User, SiteTools.Missions)) return Hidden();
        _library.Collect();
        var all = _library.Entries();
        var list = all.AsEnumerable();
        if (show == "todo") list = list.Where(e => e.NeedsInfo);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            list = list.Where(e => e.Shown.Contains(t, StringComparison.OrdinalIgnoreCase) || (e.Row.Giver ?? e.Row.WikiGiver ?? "").Contains(t, StringComparison.OrdinalIgnoreCase)
                || e.Row.ContractId.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        ViewData["Title"] = "Mission Library";
        return View(new MissionLibraryPage(list.ToList(), all.Count, all.Count(e => e.NeedsInfo), show == "todo" ? "todo" : "all", q?.Trim() ?? ""));
    }

    [HttpPost("set")]
    public IActionResult Set(string id, string field, string? value, string? back)
    {
        if (!_tools.Sees(User, SiteTools.Missions)) return Hidden();
        var said = _library.Set(id ?? "", field ?? "", value, User.Identity?.Name ?? "", DateTime.UtcNow);
        if (said == null) TempData["Message"] = "Not saved: a reward is a whole number (no letters).";
        return LocalRedirect(back is { Length: > 0 } && back.StartsWith("/missions", StringComparison.Ordinal) ? back + "#m-" + id : "/missions#m-" + id);
    }
}
