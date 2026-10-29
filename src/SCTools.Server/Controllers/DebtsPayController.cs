using SCTools.Server.Accounts;
using SCTools.Server.Editing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Payment tick of a money transfer on the Hauling Planner's Debts tab (/hauling/debts; see WebSnapshot.Debts): the player who sent the money says
/// whether it pays off a debt, and app.js sends it here in the background (Frank, 29 Oct 2026). Only the sender can (the admin too, on his Debts page);
/// the other players see the tick as text.
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[AutoValidateAntiforgeryToken]
[Route("/hauling/debts")]
public sealed class DebtsPayController : Controller
{
    readonly LiveTracker _live;
    readonly ToolAccess _tools;

    public DebtsPayController(LiveTracker live, ToolAccess tools)
    {
        _live = live;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    /// <param name="key">the transfer (SavedTransfer.Key)</param>
    /// <param name="payment">true: it pays off a debt</param>
    [HttpPost("payment")]
    public IActionResult Payment(string key, bool payment)
    {
        if (!_tools.Sees(User, SiteTools.Hauling)) return Hidden();
        var by = User.IsInRole(Roles.Admin) ? null : User.Identity?.Name ?? "";   // a player: only his own transfers
        var said = "";
        _live.Edit(t => said = Edits.SetPayment(t, key ?? "", payment, by), DateTime.UtcNow);
        var ok = said.Contains("pays off a debt", StringComparison.Ordinal) || said.Contains("is not a payment", StringComparison.Ordinal);
        if (Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader)) return Json(new { ok, said });
        TempData["Message"] = said;
        return LocalRedirect("/hauling/debts");
    }
}
