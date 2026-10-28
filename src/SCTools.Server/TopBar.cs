using System.Net;
using SCTools.Server.Accounts;
using Microsoft.AspNetCore.Antiforgery;

namespace SCTools.Server;

/// <summary>
/// The bar at the top of every page of the site, the tools' own pages too (the Hauling Planner's live page): SC Tools (back to the cards of the landing
/// page), the tools this viewer sees (to go from one to the other; the one he is in is marked), and on the right the tracker (its download and how to set
/// it up), who is logged in, My page for a player, Admin for the admin, Log out, or Log in. On a phone the tools and the rest are behind a menu button.
/// Its look is topbar.css.
/// </summary>
public static class TopBar
{
    public static string Html(HttpContext context, ToolAccess tools, SiteTool? current = null)
    {
        var user = context.User;
        static string E(string s) => WebUtility.HtmlEncode(s);
        var html = new System.Text.StringBuilder("<header class=\"topbar\">\n  <a class=\"brand\" href=\"/\">SC Tools</a>\n")
            .Append("  <button class=\"menu-toggle\" type=\"button\" aria-label=\"Menu\" aria-expanded=\"false\" aria-controls=\"topbar-menu\" ")
            .Append("onclick=\"this.setAttribute('aria-expanded', this.parentNode.classList.toggle('open'))\">")
            .Append("<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" aria-hidden=\"true\"><path d=\"M4 7h16M4 12h16M4 17h16\"/></svg></button>\n")
            .Append("  <div class=\"menu-panel\" id=\"topbar-menu\">\n  <nav class=\"toolnav\">");
        foreach (var tool in SiteTools.All.Where(t => t.Path != null && tools.Sees(user, t)))
            html.Append("<a href=\"").Append(E(tool.Path!)).Append('"').Append(tool == current ? " class=\"on\"" : "").Append('>').Append(E(tool.Name)).Append("</a>");
        html.Append("</nav>\n  <div class=\"who\">")
            .Append("<a class=\"dl\" href=\"/tracker\"><svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" aria-hidden=\"true\"><path d=\"M12 3v12M7 10l5 5 5-5M4 20h16\"/></svg>Tracker</a>");
        if (user.Identity?.IsAuthenticated == true)
        {
            html.Append("<span>").Append(E(user.Identity.Name ?? "")).Append("</span>");
            if (user.IsInRole(Roles.Player)) html.Append("<a href=\"/me\">My page</a>");
            if (user.IsInRole(Roles.Admin)) html.Append("<a href=\"/admin\">Admin</a>");
            var token = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
            html.Append("<form method=\"post\" action=\"/logout\" class=\"inline\"><input type=\"hidden\" name=\"").Append(E(token.FormFieldName)).Append("\" value=\"")
                .Append(E(token.RequestToken ?? "")).Append("\"><button type=\"submit\" class=\"link\">Log out</button></form>");
        }
        else
        {
            html.Append("<a href=\"/login\">Log in</a>");
        }
        return html.Append("</div>\n  </div>\n</header>").ToString();
    }
}
