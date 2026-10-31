using SCTools.Server.Accounts;
using SCTools.Server.Editing;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// What is changed on the Hauling Planner's Debts tab (/hauling/debts; see WebSnapshot.Debts), sent there in the background (hauling.js). The Payment tick
/// of a money transfer: the player who sent the money says whether it pays off a debt (Frank, 29 Oct 2026); only the sender can, and the admin; the other
/// players see the tick as text. The admin's alone (Frank, 31 Oct 2026: his Debts page went, he changes them on the tab): a balance typed over, what a
/// mission really paid, and who is in the party of the session.
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

    /// <summary>The answer: JSON for the page's background send (ok: it was saved), else back to the tab with the sentence.</summary>
    IActionResult Said(string said, bool ok)
    {
        if (Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader)) return Json(new { ok, said });
        TempData["Message"] = said;
        return LocalRedirect("/hauling/debts");
    }

    /// <summary>The admin's change: made, and said.</summary>
    IActionResult Admin(Func<Tracker, string> edit)
    {
        if (!User.IsInRole(Roles.Admin)) return Hidden();
        var said = "";
        _live.Edit(t => said = edit(t), DateTime.UtcNow);
        return Said(said, true);
    }

    /// <summary>A balance of me with other typed over (+ other owes me, - I owe him): the difference to what is worked out is kept.</summary>
    [HttpPost("balance")]
    public IActionResult Balance(string me, string other, string? balance)
    {
        var v = Amounts.Parse(balance);
        if (v == null) return User.IsInRole(Roles.Admin) ? Said("Type the balance as a number (+ he owes " + me + ", - " + me + " owes him).", false) : Hidden();
        return Admin(t => Edits.SetBalance(t, me ?? "", other ?? "", v.Value));
    }

    /// <summary>What a mission really paid (a short delivery pays less); empty: the reward of its contract again.</summary>
    [HttpPost("payout")]
    public IActionResult Payout(string id, string? payout)
    {
        var v = Amounts.Parse(payout);
        if (!string.IsNullOrWhiteSpace(payout) && (v == null || v < 0))
            return User.IsInRole(Roles.Admin) ? Said("Type what the mission paid as a number (empty = the reward of its contract).", false) : Hidden();
        return Admin(t => Edits.SetPayout(t, id ?? "", v));
    }

    /// <summary>Who was in the party of this session, also somebody who carried nothing: the reward of the session is split equally between them.</summary>
    [HttpPost("party")]
    public IActionResult Party(List<string> names) => Admin(t => Edits.SetSessionParty(t, names ?? new List<string>()));

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
        return Said(said, ok);
    }
}
