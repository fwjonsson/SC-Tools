using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Party tool: who is playing and in which in-game party, and everybody else the site knows with when he was last seen; a player's own page has what
/// there is to know about him (PartyService). For a logged in player or the admin, and only for the admin until it is shown (the Tools page).
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[Route("/party")]
public sealed class PartyToolController : Controller
{
    readonly PartyService _party;
    readonly FleetService _fleet;
    readonly ToolAccess _tools;

    public PartyToolController(PartyService party, FleetService fleet, ToolAccess tools)
    {
        _party = party;
        _fleet = fleet;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!_tools.Sees(User, SiteTools.Party)) return Hidden();
        var now = DateTime.UtcNow;
        var parties = _party.Parties(now);
        var roster = _party.Roster(now);
        var inParty = new HashSet<string>(parties.SelectMany(p => p.Names), StringComparer.OrdinalIgnoreCase);
        ViewData["Title"] = "Party";
        var ships = parties.SelectMany(p => p.Names).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(n => n, n => _fleet.Active(n).Text, StringComparer.OrdinalIgnoreCase);   // the ship each one flies
        return View("~/Views/PartyTool/Index.cshtml", new PartyToolPage(parties, roster.Where(r => !inParty.Contains(r.Name)).ToList(), ships, now));
    }

    [HttpGet("{name}")]
    public IActionResult Member(string name)
    {
        if (!_tools.Sees(User, SiteTools.Party)) return Hidden();
        var now = DateTime.UtcNow;
        var member = _party.Member(name, now);
        if (member == null) return NotFound();
        ViewData["Title"] = member.Name;
        return View("~/Views/PartyTool/Member.cshtml", new PartyMemberPage(member, now));
    }
}
