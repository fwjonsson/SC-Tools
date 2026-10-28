using SCTools.Server.Editing;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The admin's Debts page: the Debts tab of the program for one member at a time (his balances, typed over to correct them, and his money transfers with
/// the Payment tick), every mission payout (what it really paid), and the party of the session.
/// </summary>
[Route("/admin/debts")]
public sealed class DebtsController : AdminControllerBase
{
    public DebtsController(LiveTracker live) : base(live)
    {
    }

    static string Back(string? me) => "/admin/debts" + (string.IsNullOrEmpty(me) ? "" : "?me=" + Uri.EscapeDataString(me));

    [HttpGet("")]
    public IActionResult Index(string? me) => View(Live.Read(t => AdminDebtsModel.Of(t, me)));

    [HttpPost("balance")]
    public IActionResult Balance(string me, string other, string? balance)
    {
        var v = Amounts.Parse(balance);
        if (v == null) return Say("Type the balance as a number (+ he owes " + me + ", - " + me + " owes him).", Back(me));
        return Change(t => Edits.SetBalance(t, me, other, v.Value), Back(me));
    }

    [HttpPost("payment")]
    public IActionResult Payment(string key, bool payment, string? me) => Change(t => Edits.SetPayment(t, key, payment), Back(me));

    [HttpPost("payout")]
    public IActionResult Payout(string id, string? payout, string? me)
    {
        var v = Amounts.Parse(payout);
        if (!string.IsNullOrWhiteSpace(payout) && (v == null || v < 0)) return Say("Type what the mission paid as a number (empty = the reward of its contract).", Back(me));
        return Change(t => Edits.SetPayout(t, id, v), Back(me));
    }

    [HttpPost("party")]
    public IActionResult Party(List<string> names, string? me) => Change(t => Edits.SetSessionParty(t, names), Back(me));
}
