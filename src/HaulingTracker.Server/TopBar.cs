using System.Net;
using HaulingTracker.Server.Accounts;
using Microsoft.AspNetCore.Antiforgery;

namespace HaulingTracker.Server;

/// <summary>
/// The bar at the top of every page of the site, the tools' own pages too (the hauling tracker's live page): SC Tools (back to the cards of the landing
/// page), the tools this viewer sees (to go from one to the other; the one he is in is marked), and on the right who is logged in, My page for a player,
/// Admin for the admin, Log out, or Log in. Its look is topbar.css.
/// </summary>
public static class TopBar
{
    public static string Html(HttpContext context, ToolAccess tools, SiteTool? current = null)
    {
        var user = context.User;
        static string E(string s) => WebUtility.HtmlEncode(s);
        var html = new System.Text.StringBuilder("<header class=\"topbar\">\n  <a class=\"brand\" href=\"/\">SC Tools</a>\n  <nav class=\"toolnav\">");
        foreach (var tool in SiteTools.All.Where(t => t.Path != null && tools.Sees(user, t)))
            html.Append("<a href=\"").Append(E(tool.Path!)).Append('"').Append(tool == current ? " class=\"on\"" : "").Append('>').Append(E(tool.Name)).Append("</a>");
        html.Append("</nav>\n  <div class=\"who\">");
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
        return html.Append("</div>\n</header>").ToString();
    }
}
