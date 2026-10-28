using HaulingTracker.Server.Accounts;
using HaulingTracker.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>
/// The Tools page (admin only): which tools everybody sees on the landing page. A tool that is not shown is only for the admin: its card, and its page
/// (the others get the login, or "not for you"). A tick is saved as soon as it changes.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route("/admin/tools")]
public sealed class ToolsController : Controller
{
    readonly ToolAccess _tools;

    public ToolsController(ToolAccess tools) => _tools = tools;

    [HttpGet("")]
    public IActionResult Index() => View(SiteTools.All.Select(t => new ToolLine(t, _tools.IsPublic(t))).ToList());

    /// <summary>Shows this tool to everybody (value true) or only to the admin (the tick taken off: no value).</summary>
    [HttpPost("set")]
    public IActionResult Set(string id, bool value)
    {
        if (SiteTools.Find(id) is not { } tool)
        {
            TempData["Message"] = "There is no such tool.";
            return LocalRedirect("/admin/tools");
        }
        _tools.SetPublic(tool, value);
        TempData["Message"] = value ? tool.Name + " is on the front page for everybody now." : tool.Name + " is only for you now.";
        return LocalRedirect("/admin/tools");
    }
}
