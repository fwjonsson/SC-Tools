using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// The admin's point of view (TopBar.ViewAs): the player he looks at the site as, kept in the sct-pov cookie (PointOfView); an empty name is Admin, his own
/// view of everybody. He comes back to the page he was on, its tab too (back: the address with its #, which a browser keeps through the redirect).
/// </summary>
[Route("/admin/pov")]
public sealed class PovController : AdminControllerBase
{
    public PovController(LiveTracker live) : base(live) { }

    [HttpPost("")]
    public IActionResult Set(string? name, string? back)
    {
        if (string.IsNullOrWhiteSpace(name)) Response.Cookies.Delete(PointOfView.Cookie);
        else Response.Cookies.Append(PointOfView.Cookie, name.Trim(), new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, HttpOnly = true });
        return LocalRedirect(Url.IsLocalUrl(back) ? back! : "/");
    }
}
