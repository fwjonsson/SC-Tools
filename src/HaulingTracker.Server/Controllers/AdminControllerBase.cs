using HaulingTracker.Server.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

/// <summary>
/// The admin's pages: only for the admin, every form checked against forgery. A change runs inside LiveTracker.Edit (under the lock, saved, worked out
/// again at once), and the page shows what it did after it reloads.
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
public abstract class AdminControllerBase : Controller
{
    protected AdminControllerBase(LiveTracker live) => Live = live;

    protected LiveTracker Live { get; }

    /// <summary>Makes the change, and goes back to the page with the sentence it said.</summary>
    protected IActionResult Change(Func<Tracker, string> edit, string back)
    {
        var said = "";
        Live.Edit(t => said = edit(t), DateTime.UtcNow);
        TempData["Message"] = said;
        return LocalRedirect(back);
    }

    /// <summary>Goes back to the page with this sentence, without changing anything.</summary>
    protected IActionResult Say(string message, string back)
    {
        TempData["Message"] = message;
        return LocalRedirect(back);
    }
}
