namespace SCTools.GameLog;

/// <summary>
/// The player of a log went from one area of the game to another: "&lt;Update Inventory Location&gt; Player [zzoega] is changing location. Landing
/// [2026442305] -&gt; [0]. Location [2026442305] -&gt; [685899972]". The game's numbers, not names: Landing is the landing area he is at (0: none, he flew
/// off), Location the area around him (a pad, a station's area, open space). Leaving a station is one or two of them a few seconds apart (off the pad, out of
/// the station's area into space); the next one, a whole jump later, is the area of where he jumped to. From leaving to coming in, the same route takes the
/// same time to a few seconds (zzoega's logs, 31 Oct 2026), so these lines are when a quantum jump starts and ends (MapService.JumpTimes).
/// </summary>
public sealed record AreaChange(DateTime Time, long LandingFrom, long LandingTo, long From, long To)
{
    /// <summary>Into a landing area: he landed, or is on a pad or in a hangar.</summary>
    public bool Landed => LandingTo != 0;
}
