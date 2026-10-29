using System.Net;
using SCTools.Server.Accounts;
using Microsoft.AspNetCore.Antiforgery;

namespace SCTools.Server;

/// <summary>
/// The bar at the top of every page of the site, the tools' own pages too (the Hauling Planner's live page): SC Tools (back to the cards of the landing
/// page), the tools this viewer sees (to go from one to the other), and on the right the tracker (its download and how to set it up), who is logged in, his
/// Party button (who is playing now and with whom, in a panel under it), My page for a player, Admin for the admin, Log out, or Log in. The part of the site you are in is marked in its own colour: a tool in the colour of its card,
/// My page, Admin (the admin's pages and his account; the row of his pages under the bar has the same colour). On a phone the tools and the rest are behind
/// a menu button. Its look is topbar.css.
/// </summary>
public static class TopBar
{
    /// <summary>The colour of the admin's part of the site: Admin in the bar and the row of his pages under it.</summary>
    public const string AdminColor = "#e5534b";

    /// <summary>The colour of My page in the bar.</summary>
    public const string MeColor = "#57c785";

    /// <summary>Whether this page is in the admin's part of the site (his pages, his account).</summary>
    public static bool InAdmin(HttpContext context) =>
        context.User.IsInRole(Roles.Admin) && (context.Request.Path.StartsWithSegments("/admin") || context.Request.Path.StartsWithSegments("/account"));

    /// <summary>A link of the bar: marked (and its colour shown) when it is the part of the site you are in.</summary>
    static string Link(string href, string text, string color, bool on, string? cls = null)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var classes = string.Join(" ", new[] { cls, on ? "on" : null }.Where(c => c != null));
        return "<a href=\"" + E(href) + "\"" + (classes.Length > 0 ? " class=\"" + classes + "\"" : "") + (on ? " aria-current=\"page\"" : "") +
            " style=\"--tool:" + E(color) + "\">" + E(text) + "</a>";
    }

    /// <param name="current">the tool the page is (none: the tool whose address the page is under)</param>
    public static string Html(HttpContext context, ToolAccess tools, SiteTool? current = null)
    {
        var user = context.User;
        var path = context.Request.Path;
        current ??= SiteTools.All.FirstOrDefault(t => t.Path != null && path.StartsWithSegments(t.Path));
        static string E(string s) => WebUtility.HtmlEncode(s);
        var html = new System.Text.StringBuilder("<header class=\"topbar\">\n  <a class=\"brand\" href=\"/\">SC Tools</a>\n")
            .Append("  <button class=\"menu-toggle\" type=\"button\" aria-label=\"Menu\" aria-expanded=\"false\" aria-controls=\"topbar-menu\" ")
            .Append("onclick=\"this.setAttribute('aria-expanded', this.parentNode.classList.toggle('open'))\">")
            .Append("<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" aria-hidden=\"true\"><path d=\"M4 7h16M4 12h16M4 17h16\"/></svg></button>\n")
            .Append("  <div class=\"menu-panel\" id=\"topbar-menu\">\n  <nav class=\"toolnav\">");
        foreach (var tool in SiteTools.Cards.Where(t => t.Path != null && tools.Sees(user, t)))
            html.Append(Link(tool.Path!, tool.Name, tool.Color, tool == current));
        html.Append("</nav>\n  <div class=\"who\">")
            .Append("<a class=\"dl\" href=\"/tracker\"><svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" aria-hidden=\"true\"><path d=\"M12 3v12M7 10l5 5 5-5M4 20h16\"/></svg>Tracker</a>");
        if (user.Identity?.IsAuthenticated == true)
        {
            html.Append("<span>").Append(E(user.Identity.Name ?? "")).Append("</span>");
            if (tools.Sees(user, SiteTools.Party)) html.Append(PartyButton(path.StartsWithSegments(SiteTools.Party.Path!)));
            if (user.IsInRole(Roles.Player)) html.Append(Link("/me", "My page", MeColor, path.StartsWithSegments("/me"), "area"));
            if (user.IsInRole(Roles.Admin)) html.Append(Link("/admin", "Admin", AdminColor, InAdmin(context), "area"));
            var token = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
            html.Append("<form method=\"post\" action=\"/logout\" class=\"inline\"><input type=\"hidden\" name=\"").Append(E(token.FormFieldName)).Append("\" value=\"")
                .Append(E(token.RequestToken ?? "")).Append("\"><button type=\"submit\" class=\"link\">Log out</button></form>");
        }
        else
        {
            html.Append("<a href=\"/login\">Log in</a>");
        }
        html.Append("</div>\n  </div>\n</header>");
        if (user.Identity?.IsAuthenticated == true && tools.Sees(user, SiteTools.Party)) html.Append('\n').Append(PartyScript);
        return html.ToString();
    }

    /// <summary>
    /// The Party button beside the name (the Party tool is not a card on the landing page, Frank 29 Oct 2026): a panel opens under it with who is playing now
    /// and with whom, the ship each one flies, and a link to everybody's page. The panel is fetched when it opens (/party/panel, PartyToolController), so a
    /// page costs nothing for it until somebody looks; without JavaScript it is a link to the Party page. Marked (on) on the Party pages, in the tool's colour.
    /// </summary>
    static string PartyButton(bool on) =>
        "<details class=\"party\" style=\"--tool:" + SiteTools.Party.Color + "\" ontoggle=\"partyPanel(this)\"><summary class=\"area" + (on ? " on" : "") + "\""
        + (on ? " aria-current=\"page\"" : "") + ">Party</summary><div class=\"panel\"><p class=\"muted\"><a href=\"/party\">Open the Party page</a></p></div></details>";

    /// <summary>The panel of the Party button is fetched every time it opens (so it is what the logs say now), and an open panel closes on a click anywhere else.</summary>
    const string PartyScript = "<script>function partyPanel(d){if(!d.open)return;var p=d.lastElementChild;fetch('/party/panel',{credentials:'same-origin'})"
        + ".then(function(r){if(!r.ok)throw new Error(r.status);return r.text();}).then(function(t){p.innerHTML=t;})"
        + ".catch(function(){p.innerHTML='<p class=\"muted\">The party could not be fetched. <a href=\"/party\">Open the Party page</a></p>';});}"
        + "document.addEventListener('click',function(e){document.querySelectorAll('details.party[open]').forEach(function(d){if(!d.contains(e.target))d.removeAttribute('open');});});</script>";
}
