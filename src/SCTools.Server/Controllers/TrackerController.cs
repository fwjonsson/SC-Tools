using SCTools.Server.Models;
using SCTools.Updates;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The SC Tools Tracker's page (for everybody, the landing page links to it): what it is, the download of the newest build, and how to set it up. For a
/// logged in player also his own tracker: which version it runs and whether that is the newest (so he sees that it updated).
/// </summary>
[Route("/tracker")]
public sealed class TrackerController : Controller
{
    /// <summary>The tracker in the "latest" release the build makes from main.</summary>
    public static readonly string Download = "https://github.com/fwjonsson/SC-Tools/releases/download/latest/" + AppName.Tracker.Zip;

    readonly TrackerVersions _versions;

    public TrackerController(TrackerVersions versions) => _versions = versions;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var me = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
        if (me == null) return View(new TrackerPage(null, null, null));
        var now = DateTime.UtcNow;
        var (line, newest) = TrackerVersions.Describe(_versions.Of(me), await _versions.NewestAsync(now), now);
        return View(new TrackerPage(me, line, newest));
    }
}
