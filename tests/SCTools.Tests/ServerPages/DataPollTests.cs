using System.Net;
using System.Net.Http.Headers;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The live page's poll: data.json comes with an ETag, the page sends it back (If-None-Match), and the same data again is answered with 304 and no body, until
/// something changes (a piece of log comes in) and the data is made anew with a new ETag.
/// </summary>
public class DataPollTests
{
    static HttpRequestMessage Poll(EntityTagHeaderValue etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/data.json");
        request.Headers.IfNoneMatch.Add(etag);
        return request;
    }

    [Fact]
    public async Task The_same_data_again_is_304_until_a_piece_of_log_comes_in()
    {
        using var f = new ServerFactory();
        var c = f.Browser();
        var first = await c.GetAsync("/data.json");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());

        // the server's own refresh runs meanwhile and may just have made the data anew once: the poll after that one is 304
        HttpResponseMessage same = null!;
        for (var tries = 0; tries < 3; tries++)
        {
            same = await c.SendAsync(Poll(etag!));
            if (same.StatusCode == HttpStatusCode.NotModified) break;
            etag = same.Headers.ETag;
        }
        Assert.Equal(HttpStatusCode.NotModified, same.StatusCode);
        Assert.Equal(etag, same.Headers.ETag);   // the ETag comes with the 304 too
        Assert.Equal(0, (await same.Content.ReadAsByteArrayAsync()).Length);

        f.Upload(PlayedSession.Player, PlayedSession.Writer().Lines.ToList());
        var fresh = await c.SendAsync(Poll(etag!));
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.NotEqual(etag, fresh.Headers.ETag);
        Assert.Contains("\"visitor\":true", await fresh.Content.ReadAsStringAsync());   // a visitor's data: nobody's session (Audience)
    }

    [Fact]
    public void A_weak_etag_as_cloudflare_sends_it_back_matches_too()
    {
        // Cloudflare compresses data.json and then calls its ETag weak (W/"..."): the browser sends it back that way, and that is still the same data
        Assert.True(SCTools.Server.Controllers.PageController.Matches(new[] { "W/\"ab12-7\"" }, "\"ab12-7\""));
        Assert.True(SCTools.Server.Controllers.PageController.Matches(new[] { "\"x\", \"ab12-7\"" }, "\"ab12-7\""));
        Assert.True(SCTools.Server.Controllers.PageController.Matches(new[] { "*" }, "\"ab12-7\""));
        Assert.False(SCTools.Server.Controllers.PageController.Matches(new[] { "W/\"ab12-6\"" }, "\"ab12-7\""));
        Assert.False(SCTools.Server.Controllers.PageController.Matches(Array.Empty<string?>(), "\"ab12-7\""));
    }

    [Fact]
    public async Task The_status_line_has_its_own_address_and_is_not_in_data_json()
    {
        using var f = new ServerFactory();
        f.Upload(PlayedSession.Player, PlayedSession.Writer().Lines.ToList());
        var c = f.Browser();
        var status = System.Text.Json.JsonDocument.Parse(await c.GetStringAsync("/status.json")).RootElement;
        Assert.True(status.TryGetProperty("status", out _));
        Assert.True(status.TryGetProperty("key", out _));
        var planner = System.Text.Json.JsonDocument.Parse(await c.GetStringAsync("/data.json")).RootElement.GetProperty("planner");
        Assert.False(planner.TryGetProperty("status", out _));   // a new line every few seconds made the whole data.json again
    }
}
