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
[Route("/party")]
public sealed class PartyToolController : Controller
{
    readonly PartyService _party;
    readonly FleetService _fleet;
    readonly LiveTracker _live;
    readonly ToolAccess _tools;
    readonly Audience _audience;

    public PartyToolController(PartyService party, FleetService fleet, LiveTracker live, ToolAccess tools, Audience audience)
    {
        _audience = audience;
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
        if (Audience.Of(HttpContext).Visitor) return View("~/Views/Shared/LogInNote.cshtml", "Who plays with you, and the ships your party flies, are here once you are logged in.");
        return View("~/Views/PartyTool/Index.cshtml", Page(DateTime.UtcNow));
    }

    /// <summary>The panel of the Party button in the top bar: the parties now as tables (member, ship, drive), and who is not playing (an HTML fragment, fetched when it opens).</summary>
    [HttpGet("panel")]
    public IActionResult Panel()
    {
        if (!_tools.Sees(User, SiteTools.Party)) return Hidden();
        Response.Headers.CacheControl = "no-store";
        if (Audience.Of(HttpContext).Visitor) return PartialView("~/Views/Shared/LogInNote.cshtml", "Your party is here once you are logged in.");
        return PartialView("~/Views/PartyTool/_Panel.cshtml", Page(DateTime.UtcNow));
    }

    /// <summary>The parties now with the ship each one flies, its drive and his colour, and everybody else: the Party page and the panel show the same.</summary>
    PartyToolPage Page(DateTime now)
    {
        // a player sees his own party (the in-game one and the one he hauls with), the admin everybody (Audience; Frank, 30 Oct 2026)
        var viewer = Audience.Of(HttpContext);
        var allowed = viewer.Everything ? null : _audience.Allowed(viewer, now);
        var parties = _party.Parties(now).Where(p => allowed == null || p.Names.Any(allowed.Contains)).ToList();
        var roster = _party.Roster(now).Where(r => allowed == null || allowed.Contains(r.Name)).ToList();
        var inParty = new HashSet<string>(parties.SelectMany(p => p.Names), StringComparer.OrdinalIgnoreCase);
        var ships = parties.SelectMany(p => p.Names).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(n => n, n => Line(n, now), StringComparer.OrdinalIgnoreCase);
        return new PartyToolPage(parties, roster.Where(r => !inParty.Contains(r.Name)).ToList(), ships, now);
    }

    /// <summary>
    /// A member's ship (his fleet's active one, or the class of his last quantum jump), its drive, his colour (Edits.ColorOf: the Hauling Planner's), and
    /// whether the planner has him active (null when he is not in its party).
    /// </summary>
    PartyMemberLine Line(string name, DateTime now)
    {
        var active = _fleet.Active(name);
        var (color, inPlanner) = _live.Read(t => (Edits.ColorOf(t, name),
            t.State.Data.Party.Any(n => PlayerNames.SameName(n, name)) ? t.Crew.IsActiveAt(name, now) : (bool?)null));
        return new PartyMemberLine(active.Text, active.Ship?.DriveText ?? "", ViewPalette.Hex(color), ViewPalette.Hex(ViewPalette.TextOn(color)), inPlanner);
    }

    /// <summary>
    /// The Active tick of a member in the panel (the admin, and the leader of a party for his members): off when he stopped playing and the logs still
    /// count him in the Hauling Planner's party (the planner stops planning trips for him at once), on to bring him back. The same as In and Out on the
    /// admin's Party page: it holds until the logs change their mind about him. Sent in the background (X-Autosave: JSON back), else a redirect to the
    /// Party page.
    /// </summary>
    [HttpPost("active")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Player}")]
    [AutoValidateAntiforgeryToken]
    public IActionResult Active(string name, bool active)
    {
        if (!_tools.Sees(User, SiteTools.Party)) return Hidden();
        var now = DateTime.UtcNow;
        name ??= "";
        var said = "";
        if (!User.IsInRole(Roles.Admin) && !Leads(User.Identity?.Name ?? "", name, now)) said = "Only the leader of his party (or the admin) ticks a member.";
        else _live.Edit(t => said = Edits.SetActive(t, name, active, now), now);
        var ok = said.Contains("joined", StringComparison.Ordinal) || said.Contains("left", StringComparison.Ordinal) || said == "Nothing changed.";
        if (Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader)) return Json(new { ok, said });
        TempData["Message"] = said;
        return LocalRedirect("/party");
    }

    /// <summary>Is this player the leader of the in-game party the member is in now?</summary>
    bool Leads(string player, string member, DateTime now) =>
        _party.Parties(now).Any(p => PlayerNames.SameName(p.Leader, player) && p.Names.Any(n => PlayerNames.SameName(n, member)));

    [HttpGet("{name}")]
    public IActionResult Member(string name)
    {
        if (!_tools.Sees(User, SiteTools.Party)) return Hidden();
        var now = DateTime.UtcNow;
        var viewer = Audience.Of(HttpContext);
        if (!viewer.Everything && !(_audience.Allowed(viewer, now) ?? new HashSet<string>()).Contains(name ?? "")) return NotFound();   // not of his party
        var member = _party.Member(name, now);
        if (member == null) return NotFound();
        ViewData["Title"] = member.Name;
        return View("~/Views/PartyTool/Member.cshtml", new PartyMemberPage(member, now));
    }
}
