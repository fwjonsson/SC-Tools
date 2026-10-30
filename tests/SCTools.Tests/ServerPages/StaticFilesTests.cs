namespace SCTools.Tests.ServerPages;

/// <summary>The site's scripts and styles: linked with their version in the address, so a browser keeps them a year; anything else a day.</summary>
public class StaticFilesTests
{
    [Fact]
    public async Task A_versioned_file_is_kept_a_year_and_any_other_a_day()
    {
        using var f = new ServerFactory();
        var c = f.Browser();
        var versioned = await c.GetAsync("/site.css?v=abc123");
        Assert.Equal("public, max-age=31536000, immutable", versioned.Headers.CacheControl?.ToString());
        var plain = await c.GetAsync("/site.css");
        Assert.Equal("public, max-age=86400", plain.Headers.CacheControl?.ToString());
    }
}
