using HaulingTracker.Server.Editing;
using HaulingTracker.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>The admin's Contracts page: the Contracts tab of the program (who has every mission, and Gone / Bring back).</summary>
[Route("/admin/contracts")]
public sealed class ContractsController : AdminControllerBase
{
    const string Back = "/admin/contracts";

    public ContractsController(LiveTracker live) : base(live)
    {
    }

    [HttpGet("")]
    public IActionResult Index() => View(Live.Read(AdminContractsModel.Of));

    [HttpPost("holder")]
    public IActionResult Holder(string id, string member, bool has) => Change(t => Edits.SetHolder(t, id, member, has), Back);

    [HttpPost("gone")]
    public IActionResult Gone(string id) => Change(t => Edits.ToggleGone(t, id), Back);
}
