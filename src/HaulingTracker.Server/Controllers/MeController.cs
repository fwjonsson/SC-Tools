using HaulingTracker.Server.Accounts;
using HaulingTracker.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>
/// My page: a player sets where he is now, his colour, his ship and its quantum drive, for himself only (the name comes from his login, never from the
/// form). The same changes as in the Party table and on the Trip Planner of the program.
/// </summary>
[Authorize(Roles = Roles.Player)]
[AutoValidateAntiforgeryToken]
[Route("/me")]
public sealed class MeController : Controller
{
    readonly LiveTracker _live;

    public MeController(LiveTracker live) => _live = live;

    string Player => User.Identity?.Name ?? "";

    [HttpGet("")]
    public IActionResult Index() => View(_live.Read(t => MeModel.Of(t, Player)));

    [HttpPost("")]
    public IActionResult Save(MeForm form)
    {
        var player = Player;
        var stations = MeModel.StationChoices();
        var location = string.IsNullOrEmpty(form.Location) ? null : form.Location;
        var color = MeModel.ParseColor(form.Color);
        var ship = string.IsNullOrEmpty(form.Ship) ? null : Ships.Find(form.Ship);
        var drive = string.IsNullOrEmpty(form.Drive) ? null : QuantumDrives.Find(form.Drive);
        if (!ModelState.IsValid || color == null || (location != null && stations.All(s => s.Value != location))
            || (!string.IsNullOrEmpty(form.Ship) && ship == null) || (!string.IsNullOrEmpty(form.Drive) && drive == null))
        {
            TempData["Message"] = "Nothing was saved: something on the page was not one of the choices.";
            return LocalRedirect("/me");
        }

        var changed = new List<string>();
        _live.Edit(t =>
        {
            var now = MeModel.Of(t, player);
            if ((location ?? "") != now.Location)
            {
                // his locked next stop is released, so the plan is free to start with a stop near him (as in the program)
                t.Whereabouts.SetLocation(player, location);
                t.Plan.ReleaseLock(player);
                changed.Add("location");
            }
            if (color.Value != MeModel.ColorOf(t, player))
            {
                t.Crew.SetColor(player, unchecked((int)0xFF000000) | color.Value);
                changed.Add("colour");
            }
            if ((ship?.Label ?? "") != now.Ship)
            {
                t.Crew.SetShip(player, ship?.Name);
                changed.Add("ship");
            }
            if ((drive?.Label ?? "") != now.Drive)
            {
                t.Crew.SetDrive(player, drive?.Key);
                changed.Add("quantum drive");
            }
        }, DateTime.UtcNow);
        TempData["Message"] = changed.Count == 0 ? "Nothing changed." : "Saved: " + string.Join(", ", changed) + ".";
        return LocalRedirect("/me");
    }
}
