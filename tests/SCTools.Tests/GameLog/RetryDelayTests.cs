namespace SCTools.Tests.GameLog;

/// <summary>How long the tracker waits between its upload rounds: 3 seconds, twice as long after every failure in a row, a minute while the key is unknown.</summary>
public sealed class RetryDelayTests
{
    static readonly TimeSpan Seconds3 = TimeSpan.FromSeconds(3);

    [Fact]
    public void While_all_goes_well_the_next_round_is_in_3_seconds()
    {
        var delay = new RetryDelay();
        Assert.Equal(Seconds3, delay.Next(SendOutcome.Ok));
        Assert.Equal(Seconds3, delay.Next(SendOutcome.Ok));
    }

    [Fact]
    public void Failures_in_a_row_wait_twice_as_long_each_time_up_to_2_minutes_and_the_first_success_goes_back_to_3_seconds()
    {
        var delay = new RetryDelay();
        var waits = Enumerable.Range(0, 8).Select(_ => delay.Next(SendOutcome.Failed).TotalSeconds).ToArray();
        Assert.Equal(new double[] { 3, 6, 12, 24, 48, 96, 120, 120 }, waits);

        Assert.Equal(Seconds3, delay.Next(SendOutcome.Ok));
        Assert.Equal(Seconds3, delay.Next(SendOutcome.Failed));   // a new run of failures starts at 3 seconds again
        Assert.Equal(TimeSpan.FromSeconds(6), delay.Next(SendOutcome.Failed));
    }

    [Fact]
    public void A_key_the_server_does_not_know_is_asked_about_once_a_minute()
    {
        var delay = new RetryDelay();
        delay.Next(SendOutcome.Failed);
        delay.Next(SendOutcome.Failed);
        Assert.Equal(TimeSpan.FromMinutes(1), delay.Next(SendOutcome.UnknownKey));
        Assert.Equal(TimeSpan.FromMinutes(1), delay.Next(SendOutcome.UnknownKey));
        Assert.Equal(Seconds3, delay.Next(SendOutcome.Failed));   // the server answered: the failures before are forgotten
    }

    [Fact]
    public void New_settings_start_over()
    {
        var delay = new RetryDelay();
        for (var i = 0; i < 5; i++) delay.Next(SendOutcome.Failed);
        delay.Reset();
        Assert.Equal(Seconds3, delay.Next(SendOutcome.Failed));
    }
}
