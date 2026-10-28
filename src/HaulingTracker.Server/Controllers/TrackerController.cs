using HaulingTracker.Updates;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>The SC Tools Tracker's page (for everybody, the landing page links to it): what it is, the download of the newest build, and how to set it up.</summary>
[Route("/tracker")]
public sealed class TrackerController : Controller
{
    /// <summary>The tracker in the "latest" release the build makes from main.</summary>
    public const string Download = "https://github.com/fwjonsson/SCHaulingTracker/releases/download/latest/" + TrackerName.Zip;

    [HttpGet("")]
    public IActionResult Index() => View();
}
