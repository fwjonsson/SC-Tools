using SCTools.Play;

namespace SCTools.Tests.Play;

/// <summary>The Companion's game session (#380): from when the game started until an hour after it was closed, crashed or quit and not started again.</summary>
public class GameSessionTests
{
    static DateTime U(int hour, int minute = 0) => new(2026, 10, 30, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void A_game_started_again_within_the_hour_is_the_same_session_and_one_after_it_a_new_one()
    {
        var games = new[]
        {
            new PlayedGame("a", U(10), U(12), true, false),            // an earlier evening: quit at 12
            new PlayedGame("b", U(14), U(15), false, false),           // more than an hour later: a new session; crashed at 15
            new PlayedGame("c", U(15, 20), U(16, 30), null, true),     // started again 20 minutes after the crash: the same session, running now
        };
        var s = GameSession.Latest(games, U(16, 30))!;
        Assert.Equal(U(14), s.StartUtc);
        Assert.Equal(new[] { "b", "c" }, s.Games.Select(g => g.File));
        Assert.Equal("c", s.Running?.File);
        Assert.Null(s.EndUtc);   // a game runs: it has not ended
        Assert.False(s.Over);
        Assert.Equal(TimeSpan.FromHours(1), s.PlayedBefore);   // the crashed game; the browser counts the running one on from its start
        Assert.Equal(1, s.Crashes);
    }

    [Fact]
    public void The_session_ends_an_hour_after_the_last_game_and_is_still_the_latest_then()
    {
        var games = new[] { new PlayedGame("a", U(20), U(22), true, false) };
        var s = GameSession.Latest(games, U(22, 30))!;
        Assert.False(s.Over);   // half an hour after the game was closed: the session goes on (he may start it again)
        Assert.Equal(U(23), s.EndUtc);
        s = GameSession.Latest(games, U(23, 30))!;
        Assert.True(s.Over);   // an hour after: over, and still the one shown (marked ended) until the next game
        Assert.Equal(U(20), s.StartUtc);
        Assert.Null(GameSession.Latest(Array.Empty<PlayedGame>(), U(12)));
    }
}
