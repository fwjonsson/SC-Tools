using System.Net;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>On the server the hauling sessions start and stop by themselves (SCTools:FollowSessions).</summary>
public class AutoSessionPageTests
{
    [Fact]
    public async Task A_hauling_contract_accepted_starts_the_session_of_the_party()
    {
        using var f = new ServerFactory { FollowSessions = true };
        Assert.Empty(f.Live.Read(t => t.State.Data.ManualSessions.ToList()));

        var start = DateTime.UtcNow.AddMinutes(-20);
        var m1 = GameLogWriter.MissionId(1);
        f.Upload("zzoega", new GameLogWriter().At(start).Login("zzoega").Accepted(m1)
            .Marker(m1, string.Format(GameLogWriter.HaulingContract, 2), "pickup_aaaa_0", "900000000101", GameLogWriter.Gaslight).Lines.ToList(),
            start.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));

        var sessions = f.Live.Read(t => t.State.Data.ManualSessions.ToList());
        var session = Assert.Single(sessions);
        Assert.Equal((true, (DateTime?)null), (session.Auto, session.StopUtc));   // it runs
        Assert.Equal(start, session.StartUtc, TimeSpan.FromMilliseconds(1));   // (when it stops: AutoSessionsTests)
    }
}
