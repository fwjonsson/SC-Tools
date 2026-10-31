using SCTools.Server.Accounts;
using SCTools.Server.Editing;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// My page: a player sets where he is now and his colour, and keeps his fleet (his ships, the quantum drive of each and the quality of a crafted one) and
/// the ship he flies, for himself only (the name comes from his login, never from the form). The Hauling Planner flies his active ship (FleetService).
/// The admin opens anybody's from his Players page (?player=: the page as that player sees it, and what the admin changes there is that player's; Frank,
/// 31 Oct 2026); for a player the address names nobody else.
/// </summary>
[Authorize(Roles = Roles.Player + "," + Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route("/me")]
public sealed class MeController : Controller
{
    readonly LiveTracker _live;
    readonly FleetService _fleet;

    public MeController(LiveTracker live, FleetService fleet)
    {
        _live = live;
        _fleet = fleet;
    }

    /// <summary>Whose page it is: the player the admin opened (?player=, the admin only), else the one logged in.</summary>
    string Player => User.IsInRole(Roles.Admin) && Request.Query["player"].FirstOrDefault() is { Length: > 0 } other ? other : User.Identity?.Name ?? "";

    /// <summary>Back to this page: with ?player= when the admin has somebody else's open.</summary>
    string Back => string.Equals(Player, User.Identity?.Name, StringComparison.OrdinalIgnoreCase) ? "/me" : "/me?player=" + Uri.EscapeDataString(Player);

    [HttpGet("")]
    public IActionResult Index()
    {
        var m = _live.Read(t => MeModel.Of(t, Player));
        m.Query = Back == "/me" ? "" : Back["/me".Length..];   // the forms send to the same player's page
        m.Fleet = _fleet.Fleet(Player);
        m.Active = _fleet.Active(Player);
        m.ShipNames = ShipModels.All.Select(s => s.Name).ToList();   // every spaceship of the game data
        m.ShipSizes = ShipModels.All.Select(s => (s.Name, Size: FleetService.DriveSizeOf(s.Name))).Where(s => s.Size != null)
            .ToDictionary(s => s.Name.ToLowerInvariant(), s => s.Size!.Value);
        return View(m);
    }

    /// <summary>Back to the fleet on My page, with what to say (nothing: it was saved).</summary>
    IActionResult Fleet(string? said)
    {
        if (said != null) TempData["Message"] = said;
        else _fleet.SyncPlanner(DateTime.UtcNow);   // the Hauling Planner flies it at once
        return LocalRedirect(Back + "#fleet");
    }

    [HttpPost("fleet/add")]
    public IActionResult AddShip(string? ship, string? drive, string? quality, string? fuel) => Fleet(_fleet.Add(Player, ship, drive, quality, DateTime.UtcNow, fuel));

    /// <summary>
    /// One field of one of his ships. The qualities are sent in the background (autosave.js, X-Autosave): JSON back with what to say and the new drive speed
    /// and fuel use for their cells (show), so the arrows of a quality field do not load the page again at every click; the rest goes back to the page.
    /// </summary>
    [HttpPost("fleet/set")]
    public IActionResult SetShip(int id, string? field, string? value)
    {
        var said = _fleet.Set(Player, id, field ?? "", value);
        if (!Request.Headers.ContainsKey(EditMissionsController.AutosaveHeader)) return Fleet(said);
        if (said == null) _fleet.SyncPlanner(DateTime.UtcNow);
        var ship = _fleet.Fleet(Player).FirstOrDefault(s => s.Row.Id == id);
        var show = ship == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["speed-" + id] = ship.SpeedText, ["fuel-" + id] = ship.FuelText };
        return Json(new { ok = said == null, said = said ?? "Saved.", show });
    }

    [HttpPost("fleet/remove")]
    public IActionResult RemoveShip(int id) => Fleet(_fleet.Remove(Player, id) ? null : "That ship is not in your fleet.");

    /// <param name="id">one of his ships, or empty for Automatic</param>
    [HttpPost("fleet/active")]
    public IActionResult Active(string? id)
    {
        int? pick = int.TryParse(id, out var i) ? i : null;
        return Fleet(_fleet.Pick(Player, pick) ? null : "That ship is not in your fleet.");
    }

    [HttpPost("")]
    public IActionResult Save(MeForm form)
    {
        var player = Player;
        var stations = MeModel.StationChoices();
        var location = string.IsNullOrEmpty(form.Location) ? null : form.Location;
        var color = MeModel.ParseColor(form.Color);
        if (!ModelState.IsValid || color == null || (location != null && stations.All(s => s.Value != location)))
        {
            TempData["Message"] = "Nothing was saved: something on the page was not one of the choices.";
            return LocalRedirect(Back);
        }

        (List<string> Changed, string? Refused) result = (new List<string>(), null);
        // the ship and the drive come from his fleet now: they stay as they are
        _live.Edit(t => result = Edits.ApplySettings(t, player, location, color.Value, t.Crew.ShipOf(player), t.Crew.DriveOf(player)), DateTime.UtcNow);
        TempData["Message"] = Edits.Saved("", result);
        return LocalRedirect(Back);
    }
}
