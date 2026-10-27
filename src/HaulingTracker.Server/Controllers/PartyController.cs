using System.Text.RegularExpressions;
using HaulingTracker.Server.Editing;
using HaulingTracker.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>The admin's Party page: the Party table of the program (who is in it and active, the batches of his ship, his ship, drive, colour and location).</summary>
[Route("/admin/party")]
public sealed partial class PartyController : AdminControllerBase
{
    const string Back = "/admin/party";

    public PartyController(LiveTracker live) : base(live)
    {
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,40}$")]
    private static partial Regex GameName();

    [HttpGet("")]
    public IActionResult Index() => View(Live.Read(t => AdminPartyModel.Of(t, DateTime.UtcNow)));

    [HttpPost("add")]
    public IActionResult Add(string? name)
    {
        name = name?.Trim() ?? "";
        if (!GameName().IsMatch(name)) return Say("Type the name in the game (letters, digits and - _ . only).", Back);
        return Change(t => Edits.AddMember(t, name), Back);
    }

    [HttpPost("remove")]
    public IActionResult Remove(string name) => Change(t => Edits.RemoveMember(t, name), Back);

    [HttpPost("save")]
    public IActionResult Save(string name, bool active, string? batches, string? location, string? color, string? ship, string? drive)
    {
        var rgb = MeModel.ParseColor(color);
        var pickedShip = string.IsNullOrEmpty(ship) ? null : Ships.Find(ship);
        var pickedDrive = string.IsNullOrEmpty(drive) ? null : QuantumDrives.Find(drive);
        var place = string.IsNullOrEmpty(location) ? null : location;
        if (rgb == null || (place != null && MeModel.StationChoices().All(s => s.Value != place))
            || (!string.IsNullOrEmpty(ship) && pickedShip == null) || (!string.IsNullOrEmpty(drive) && pickedDrive == null))
            return Say("Nothing was saved: something in the row was not one of the choices.", Back);
        return Change(t => Edits.SetMember(t, name, active, batches ?? "", place, rgb.Value, pickedShip, pickedDrive, DateTime.UtcNow), Back);
    }
}
