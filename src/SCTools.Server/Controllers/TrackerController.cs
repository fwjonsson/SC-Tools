using SCTools.Updates;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>The SC Tools Tracker's page (for everybody, the landing page links to it): what it is, the download of the newest build, and how to set it up.</summary>
[Route("/tracker")]
public sealed class TrackerController : Controller
{
    /// <summary>The tracker in the "latest" release the build makes from main.</summary>
    public const string Download = "https://github.com/fwjonsson/SC-Tools/releases/download/latest/" + AppName.Tracker.Zip;

    [HttpGet("")]
    public IActionResult Index() => View();
}
