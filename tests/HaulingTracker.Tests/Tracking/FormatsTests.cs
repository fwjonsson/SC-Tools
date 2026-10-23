namespace HaulingTracker.Tests.Tracking;

/// <summary>How times, amounts and distances are written, and how typed numbers are read.</summary>
public class FormatsTests
{
    [Fact]
    public void Durations_are_written_in_hours_minutes_and_seconds()
    {
        Assert.Equal("1:05:09", Formats.LongDuration(new TimeSpan(1, 5, 9)));
        Assert.Equal("26:00:00", Formats.LongDuration(TimeSpan.FromHours(26)));   // the hours go past a day
        Assert.Equal("1:05", Formats.ShortDuration(new TimeSpan(1, 5, 9)));
        Assert.Equal("23m 12s", Formats.StatusSpan(new TimeSpan(0, 23, 12)));
        Assert.Equal("1h 05m 12s", Formats.StatusSpan(new TimeSpan(1, 5, 12)));
        Assert.Equal("0s", Formats.StatusSpan(TimeSpan.FromSeconds(-5)));
        Assert.Equal("1:05", Formats.FlightSpan(65));
        Assert.Equal("1:02:05", Formats.FlightSpan(3725));
    }

    [Fact]
    public void Typed_amounts_are_read_whatever_the_separators()
    {
        Assert.Equal(1268250m, Formats.ParseNumber("1,268,250"));
        Assert.Equal(1268250m, Formats.ParseNumber("1 268 250"));
        Assert.Equal(0m, Formats.ParseNumber("abc"));
        Assert.Equal(0m, Formats.ParseNumber(null));
        Assert.Equal(-300m, Formats.ParseSigned("−300"));
        Assert.Equal(-1250m, Formats.ParseSigned("-1,250"));
        Assert.Equal(1250m, Formats.ParseSigned("+1,250"));
    }

    [Fact]
    public void Amounts_are_written_with_separators_and_nothing_for_none()
    {
        Assert.Equal("", Formats.Num(0));
        Assert.Equal("1,268,250", Formats.Num(1268250m));
        Assert.Equal("+1,250", Formats.Signed(1250));
        Assert.Equal("-300", Formats.Signed(-300));
        Assert.Equal("0", Formats.Signed(0));
        Assert.Equal("3.2M", Formats.Millions(3_200_000m));
        Assert.Equal("950,000", Formats.Millions(950_000m));
        Assert.Equal("1,000", Formats.PerHour(1000, 3600));
        Assert.Equal("", Formats.PerHour(1000, 0));
    }

    [Fact]
    public void Distances_are_written_in_gigameters()
    {
        Assert.Equal("12.3 Gm", Formats.Dist(12.34));
        Assert.Equal("", Formats.Dist(0));
        Assert.Equal("20.0", Formats.GmPerHour(10, 1800));
        Assert.Equal("", Formats.GmPerHour(10, 0));
    }

    [Fact]
    public void A_typed_time_of_day_is_hours_and_minutes()
    {
        Assert.True(Formats.TryTime("9:05", out var t));
        Assert.Equal(new TimeSpan(9, 5, 0), t);
        Assert.True(Formats.TryTime(" 09:05 ", out _));
        Assert.False(Formats.TryTime("25:00", out _));
        Assert.False(Formats.TryTime("soon", out _));
    }
}
