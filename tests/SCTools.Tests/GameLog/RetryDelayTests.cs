namespace SCTools.Tests.GameLog;

/// <summary>How long the tracker waits between uploads: three seconds, longer and longer while the server cannot be reached, a minute with an unknown key.</summary>
public class RetryDelayTests
{
    [Fact]
    public void The_wait_doubles_with_every_failure_in_a_row_up_to_two_minutes_and_is_short_again_when_a_try_works()
    {
        var d = new RetryDelay();
        Assert.Equal(TimeSpan.FromSeconds(3), d.Next);

        var waits = new List<double>();
        for (var i = 0; i < 8; i++)
        {
            d.Failed();
            waits.Add(d.Next.TotalSeconds);
        }
        Assert.Equal(new[] { 6.0, 12, 24, 48, 96, 120, 120, 120 }, waits);
        Assert.Equal(8, d.Failures);

        d.Worked();
        Assert.Equal(TimeSpan.FromSeconds(3), d.Next);
        Assert.Equal(0, d.Failures);
    }

    [Fact]
    public void An_unknown_key_is_asked_about_again_in_a_minute()
    {
        var d = new RetryDelay();
        d.KeyUnknown();
        Assert.Equal(TimeSpan.FromMinutes(1), d.Next);
        d.KeyUnknown();
        Assert.Equal(TimeSpan.FromMinutes(1), d.Next);   // not longer: a key is fixed in Settings, which asks at once
        d.Worked();
        Assert.Equal(TimeSpan.FromSeconds(3), d.Next);
    }

    [Fact]
    public void The_menu_says_the_wait_in_seconds_or_minutes()
    {
        Assert.Equal("24 s", RetryDelay.Say(TimeSpan.FromSeconds(24)));
        Assert.Equal("2 min", RetryDelay.Say(TimeSpan.FromMinutes(2)));
    }
}
