namespace HaulingTracker.Tests.GameLog;

/// <summary>A tracker with the site's old address saved moves to the new one by itself; any other address it keeps.</summary>
public class SiteAddressTests
{
    [Theory]
    [InlineData("https://haulingtracker.fwjonsson.dev")]
    [InlineData("https://haulingtracker.fwjonsson.dev/")]
    [InlineData(" HTTPS://HaulingTracker.fwjonsson.dev ")]
    [InlineData("http://haulingtracker.fwjonsson.dev/hauling")]
    [InlineData("haulingtracker.fwjonsson.dev")]
    public void The_old_address_becomes_the_site(string saved) => Assert.Equal("https://sct.fwjonsson.dev", SiteAddress.Moved(saved));

    [Theory]
    [InlineData("https://sct.fwjonsson.dev")]
    [InlineData("http://localhost:8780")]
    [InlineData("https://haulingtracker.example.com")]
    [InlineData("")]
    public void Any_other_address_stays(string saved) => Assert.Equal(saved, SiteAddress.Moved(saved));
}
