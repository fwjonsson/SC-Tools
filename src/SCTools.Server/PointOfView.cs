using SCTools.Server.Accounts;

namespace SCTools.Server;

/// <summary>
/// The admin looking at the site as a player (Frank, 30 Oct 2026: a pick in the top bar, TopBar.ViewAs, so he can go through the tools from anybody's point of
/// view): the name in the sct-pov cookie, set by PovController, counts for the admin only; a cookie a player has is ignored. The pages that work for "me"
/// (the Hauling Planner's page, the Gauntlet Tracker, the Session tool, the landing page's cards) ask Name(); what a page lets the viewer change still goes
/// by who is really logged in.
/// </summary>
public static class PointOfView
{
    public const string Cookie = "sct-pov";

    /// <summary>The player the admin looks as, or null (his own view, or not the admin).</summary>
    public static string? Of(HttpContext context) =>
        context.User.IsInRole(Roles.Admin) && context.Request.Cookies.TryGetValue(Cookie, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;

    /// <summary>The name the pages work for: the player picked, else who is logged in (null: nobody).</summary>
    public static string? Name(HttpContext context) => Of(context) ?? (context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null);
}
