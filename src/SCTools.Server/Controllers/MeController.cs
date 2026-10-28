using SCTools.Server.Accounts;
using SCTools.Server.Editing;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// My page: a player sets where he is now and his colour, and keeps his fleet (his ships, the quantum drive of each and the quality of a crafted one) and
/// the ship he flies, for himself only (the name comes from his login, never from the form). The Hauling Planner flies his active ship (FleetService).
/// </summary>
[Authorize(Roles = Roles.Player)]
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

    string Player => User.Identity?.Name ?? "";

    [HttpGet("")]
    public IActionResult Index()
    {
        var m = _live.Read(t => MeModel.Of(t, Player));
        m.Fleet = _fleet.Fleet(Player);
        m.Active = _fleet.Active(Player);
        m.ShipNames = global::SCTools.GameData.Ships.All.Select(s => s.Name).ToList();
        return View(m);
    }

    /// <summary>Back to the fleet on My page, with what to say (nothing: it was saved).</summary>
    IActionResult Fleet(string? said)
    {
        if (said != null) TempData["Message"] = said;
        else _fleet.SyncPlanner(DateTime.UtcNow);   // the Hauling Planner flies it at once
        return LocalRedirect("/me#fleet");
    }

    [HttpPost("fleet/add")]
    public IActionResult AddShip(string? ship, string? drive, string? quality) => Fleet(_fleet.Add(Player, ship, drive, quality, DateTime.UtcNow));

    [HttpPost("fleet/set")]
    public IActionResult SetShip(int id, string? field, string? value) => Fleet(_fleet.Set(Player, id, field ?? "", value));

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
            return LocalRedirect("/me");
        }

        (List<string> Changed, string? Refused) result = (new List<string>(), null);
        // the ship and the drive come from his fleet now: they stay as they are
        _live.Edit(t => result = Edits.ApplySettings(t, player, location, color.Value, t.Crew.ShipOf(player), t.Crew.DriveOf(player)), DateTime.UtcNow);
        TempData["Message"] = Edits.Saved("", result);
        return LocalRedirect("/me");
    }
}
