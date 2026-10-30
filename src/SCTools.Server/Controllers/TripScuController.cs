using SCTools.Server.Accounts;
using SCTools.Server.Editing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The SCU a hauler types for a drop-off of his own trip on the Trip Planner (hauling.js tripScuFields; Frank, 29 Oct 2026): how much goes to that stop,
/// so the boxes and the load follow it. Kept for that drop-off only (AppData.TypedScu, the same as the admin's SCU of the drop-offs on his Session page),
/// not in the Mission Library; the game's own number wins once it writes it. Only the hauler whose Current Route has the drop-off, or the admin. Sent in the
/// background (X-Autosave: JSON back), else a redirect to the tab.
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[AutoValidateAntiforgeryToken]
[Route("/hauling/trips")]
public sealed class TripScuController : Controller
{
    readonly LiveTracker _live;
    readonly ToolAccess _tools;

    public TripScuController(LiveTracker live, ToolAccess tools)
    {
        _live = live;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <summary>
    /// The Submit button at the right end of a worker's Current Route (Frank, 30 Oct 2026): the worker himself (or the admin) ends his run on the way; it is
    /// counted as it is, with what it picked up, and the plan goes on from where he is. Sent in the background (JSON back), else a redirect to the planner.
    /// </summary>
    /// <param name="name">the worker</param>
    [HttpPost("end")]
    public IActionResult End(string name)
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return Hidden();
        name ??= "";
        var me = User.IsInRole(Roles.Admin) ? null : User.Identity?.Name ?? "";
        var said = "";
        if (me != null && !PlayerNames.SameName(me, name)) said = "Only the hauler himself (or the admin) submits his route.";
        else _live.Edit(t => said = Edits.SubmitRoute(t, name), DateTime.UtcNow);
        var ok = said.Contains("is submitted", StringComparison.Ordinal);
        if (Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader)) return Json(new { ok, said });
        TempData["Message"] = said;
        return LocalRedirect("/hauling/planner");
    }

    /// <param name="key">the drop-off ("mission id|objective id")</param>
    /// <param name="value">the SCU; empty or 0 forgets what was typed</param>
    [HttpPost("scu")]
    public IActionResult Scu(string key, string? value)
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return Hidden();
        key ??= "";
        var me = User.IsInRole(Roles.Admin) ? null : User.Identity?.Name ?? "";
        var said = "";
        if (!int.TryParse((value ?? "").Replace(" ", "").Replace(",", "").Replace(".", ""), out var scu) && !string.IsNullOrWhiteSpace(value)) said = "Type a whole number of SCU.";
        else
        {
            _live.Edit(t =>
            {
                if (me != null && !t.Plan.GetPlan().Workers.Any(w => PlayerNames.SameName(w.Name, me) && w.Rows.Any(r => r.Lines.Any(l => l.Typable && l.Keys.Contains(key)))))
                    said = "Only the hauler whose trip goes there (or the admin) types the SCU.";
                else said = Edits.SetScu(t, key, scu);
            }, DateTime.UtcNow);
        }
        var ok = !said.StartsWith("Type ", StringComparison.Ordinal) && !said.StartsWith("Only ", StringComparison.Ordinal);   // refused: how to type it, or whose it is
        if (Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader)) return Json(new { ok, said });
        TempData["Message"] = said;
        return LocalRedirect("/hauling/planner");
    }
}
