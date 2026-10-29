using SCTools.Server.Accounts;
using SCTools.Server.Editing;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The Party tool: who is playing and in which in-game party, and everybody else the site knows with when he was last seen; a player's own page has what
/// there is to know about him (PartyService). For a logged in player or the admin, and only for the admin until it is shown (the Tools page). Not a card
/// on the landing page: the Party button beside the name in the top bar (TopBar.PartyButton) opens the panel (Panel) with the same parties as a table like
/// the Hauling Planner's old Party table (Frank, 29 Oct 2026: Member, Ship, QT Drive, every member's row in his colour), and links here.
/// </summary>
[Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
[Route("/party")]
public sealed class PartyToolController : Controller
{
    readonly PartyService _party;
    readonly FleetService _fleet;
    readonly LiveTracker _live;
    readonly ToolAccess _tools;

    public PartyToolController(PartyService party, FleetService fleet, LiveTracker live, ToolAccess tools)
    {
        _party = party;
        _fleet = fleet;
        _live = live;
        _tools = tools;
    }

    IActionResult Hidden() => User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!_tools.Sees(User, SiteTools.Party)) return Hidden();
        ViewData["Title"] = "Party";
        return View("~/Views/PartyTool/Index.cshtml", Page(DateTime.UtcNow));
    }

    /// <summary>The panel of the Party button in the top bar: the parties now as tables (member, ship, drive), and who is not playing (an HTML fragment, fetched when it opens).</summary>
    [HttpGet("panel")]
    public IActionResult Panel()
    {
        if (!_tools.Sees(User, SiteTools.Party)) return Hidden();
        Response.Headers.CacheControl = "no-store";
        return PartialView("~/Views/PartyTool/_Panel.cshtml", Page(DateTime.UtcNow));
    }

    /// <summary>The parties now with the ship each one flies, its drive and his colour, and everybody else: the Party page and the panel show the same.</summary>
    PartyToolPage Page(DateTime now)
    {
        var parties = _party.Parties(now);
        var roster = _party.Roster(now);
        var inParty = new HashSet<string>(parties.SelectMany(p => p.Names), StringComparer.OrdinalIgnoreCase);
        var ships = parties.SelectMany(p => p.Names).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(n => n, n => Line(n), StringComparer.OrdinalIgnoreCase);
        return new PartyToolPage(parties, roster.Where(r => !inParty.Contains(r.Name)).ToList(), ships, now);
    }

    /// <summary>A member's ship (his fleet's active one, or the class of his last quantum jump), its drive, and his colour (Edits.ColorOf: the Hauling Planner's).</summary>
    PartyMemberLine Line(string name)
    {
        var active = _fleet.Active(name);
        var color = _live.Read(t => Edits.ColorOf(t, name));
        return new PartyMemberLine(active.Text, active.Ship?.DriveText ?? "", ViewPalette.Hex(color), ViewPalette.Hex(ViewPalette.TextOn(color)));
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
