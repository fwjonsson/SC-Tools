using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// /hauling/jobs was a page of its own once, then the Other hauling board of the Hauling Planner (until 30 Oct 2026, when the hauling jobs of every system went
/// onto the Stops board and the SCU of a job's drop-off is typed on the Current Route, TripScuController): it goes to the Hauling Planner.
/// </summary>
[Route("/hauling/jobs")]
public sealed class HaulJobsController : Controller
{
    /// <summary>The page it was once: the Hauling Planner, where the jobs and their SCU fields are.</summary>
    [HttpGet("")]
    [AllowAnonymous]
    public IActionResult Index() => LocalRedirect("/hauling");

}
