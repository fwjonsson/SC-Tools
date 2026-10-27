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

    /// <summary>
    /// One field of a member's row, sent as soon as it is changed on the page (there is no Save button): active, batches, ship, drive, colour or location.
    /// Only that field changes, so a row that was open for a while does not put back the others as they were then.
    /// </summary>
    [HttpPost("set")]
    public IActionResult Set(string name, string? field, string? value)
    {
        value = string.IsNullOrEmpty(value) ? null : value;
        switch (field)
        {
            case "active":
                return Change(t => Edits.SetActive(t, name, value == "true", DateTime.UtcNow), Back);
            case "batches":
                return Change(t => Edits.SetBatches(t, name, value ?? ""), Back);
            case "ship":
                var ship = value == null ? null : Ships.Find(value);
                if (value != null && ship == null) break;
                return Change(t => Edits.SetOwn(t, name, s => s with { Ship = ship }), Back);
            case "drive":
                var drive = value == null ? null : QuantumDrives.Find(value);
                if (value != null && drive == null) break;
                return Change(t => Edits.SetOwn(t, name, s => s with { Drive = drive }), Back);
            case "color":
                if (MeModel.ParseColor(value) is not { } rgb) break;
                return Change(t => Edits.SetOwn(t, name, s => s with { Color = rgb }), Back);
            case "location":
                if (value != null && MeModel.StationChoices().All(s => s.Value != value)) break;
                return Change(t => Edits.SetOwn(t, name, s => s with { Location = value }), Back);
        }
        return Say("Nothing was saved: that was not one of the choices.", Back);
    }
}
