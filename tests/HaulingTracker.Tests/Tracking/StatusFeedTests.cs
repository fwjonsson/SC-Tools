using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.Tracking;

/// <summary>The team status line: news of the last moments first, then stats and some fun, every text for a while.</summary>
public class StatusFeedTests
{
    const string PatchCityTarget = "rs_ext_pyro3_l3";

    [Fact]
    public void Somebody_setting_course_is_news()
    {
        var selected = PlayedSession.LastPickup.AddMinutes(2);
        var (t, _) = PlayedSession.Load(more: w => w.Minutes(2).QuantumSelected(PatchCityTarget));
        var changes = new List<(string Text, bool Fade)>();
        t.Status.Changed += (text, fade) => changes.Add((text, fade));

        t.Status.Advance(selected.AddSeconds(5));
        Assert.Contains(PlayedSession.Player, t.Status.Text);
        Assert.Contains("Patch City", t.Status.Text);
        Assert.Equal(1, t.Status.Seq);
        Assert.Equal((t.Status.Text, true), Assert.Single(changes));   // a new text fades in
    }

    [Fact]
    public void Without_news_the_line_tells_stats_and_some_fun_and_changes_now_and_then()
    {
        var (t, _) = PlayedSession.Load();
        var now = PlayedSession.LastPickup.AddHours(2);   // nothing fresh any more
        t.Status.Advance(now);
        var first = t.Status.Text;
        Assert.False(string.IsNullOrEmpty(first));

        t.Status.Advance(now.AddSeconds(1));
        Assert.Equal(first, t.Status.Text);   // a text stays for a while
        Assert.Equal(1, t.Status.Seq);

        t.Status.Advance(now.AddSeconds(11));
        Assert.NotEqual(first, t.Status.Text);
        Assert.Equal(2, t.Status.Seq);
    }

    [Fact]
    public void Nothing_is_said_while_no_log_is_read()
    {
        var t = new Tracker(new AppData(), _ => { });
        t.Status.Advance(DateTime.UtcNow);
        Assert.Equal("", t.Status.Text);
        Assert.Equal(0, t.Status.Seq);
    }
}
