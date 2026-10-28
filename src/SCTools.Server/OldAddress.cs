namespace SCTools.Server;

/// <summary>
/// The site has one address (Address, sc-tools.fwjonsson.dev). A page asked for at an old one (OldHosts: haulingtracker and sct.fwjonsson.dev) is sent to the same page at
/// the site's address for good (301), so there is one site, one login and one set of links. Only the trackers' uploads (api/...) are still taken at an old
/// address, so a tracker that has not updated itself yet keeps sending; an updated tracker moves to the site's address by itself (SiteAddress). Once every
/// tracker has updated, the old address can be removed from the Cloudflare tunnel.
/// </summary>
public static class OldAddress
{
    public static IApplicationBuilder UseOldAddresses(this IApplicationBuilder app, ServerSettings settings) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (settings.Address.Length > 0 && !request.Path.StartsWithSegments("/api")
                && settings.OldHosts.Any(h => string.Equals(h, request.Host.Host, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.Redirect(settings.Address.TrimEnd('/') + request.PathBase.ToUriComponent() + request.Path.ToUriComponent()
                    + request.QueryString.ToUriComponent(), permanent: true);
                return;
            }
            await next();
        });
}
