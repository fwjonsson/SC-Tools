using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>The numbers of the Session table.</summary>
public class SessionFiguresTests
{
    [Fact]
    public void Batches_delivered_are_the_ones_of_finished_runs_out_of_every_stop()
    {
        var (t, _) = PlayedSession.Load();
        Assert.Equal("2/4", t.Figures.BatchesText());   // the first mission's two stops; the second mission's two are still out
        Assert.Equal("1", t.Figures.CompletedText());
        Assert.Equal("1", t.Figures.ActiveText());
    }

    [Fact]
    public void Missions_with_missing_cargo_are_said_with_the_active_ones()
    {
        var (t, _) = PlayedSession.Load();
        t.State.Missing = new HashSet<string> { PlayedSession.M2 };
        Assert.Equal("1  (Cargo Missing)", t.Figures.ActiveText());
    }

    [Fact]
    public void A_session_that_is_not_watched_live_lasts_its_active_time()
    {
        var (t, _) = PlayedSession.Load();
        Assert.Equal(SessionBuilder.ActiveTime(t.State.Current!, null), t.Figures.Elapsed());
        t.ClearSession();
        Assert.Null(t.Figures.Elapsed());
        Assert.Equal("", t.Figures.BatchesText());
    }

    [Fact]
    public void Earnings_per_hour_need_time_and_earnings()
    {
        var perHour = SessionFigures.HourlyText(TimeSpan.FromHours(1), 2_000_000m);   // with the separators of the computer
        Assert.Equal("2000000", new string(perHour.Where(char.IsDigit).ToArray()));
        Assert.Equal("", SessionFigures.HourlyText(null, 2_000_000m));
        Assert.Equal("", SessionFigures.HourlyText(TimeSpan.Zero, 2_000_000m));
        Assert.Equal("", SessionFigures.HourlyText(TimeSpan.FromHours(1), 0));
    }
}
