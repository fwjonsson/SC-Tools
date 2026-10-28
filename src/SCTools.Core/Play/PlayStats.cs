namespace SCTools.Play;

/// <summary>A game a player played: from its log's first line to the last time it was seen. Quit: the game was quit the normal way (false: it crashed, null: not known).</summary>
public sealed record PlayedGame(string File, DateTime StartUtc, DateTime EndUtc, bool? Quit, bool Running)
{
    public TimeSpan Length => EndUtc > StartUtc ? EndUtc - StartUtc : TimeSpan.Zero;

    /// <summary>It ended without the quit line.</summary>
    public bool Crashed => !Running && Quit == false;
}

/// <summary>A mission a player completed, with what the Mission Library says it pays (null: nobody typed it in yet).</summary>
public sealed record EarnedMission(DateTime EndedUtc, string Kind, bool Shared, long? Auec, long? Mg, long? Council)
{
    public bool HasReward => Auec != null || Mg != null || Council != null;
}

/// <summary>A stretch of time to add up: today, this week, this month, all of it.</summary>
public sealed record PlayPeriod(string Name, DateTime FromUtc, DateTime ToUtc);

/// <summary>What a period adds up to: the time played and the games, the missions completed and what they earned (the scrip at its value in aUEC).</summary>
public sealed record PlayTotals(TimeSpan Played, int Games, int Crashes, int Completed, int WithoutReward, long Auec, long Mg, long Council, double Value)
{
    /// <summary>aUEC (the scrip counted in) per hour played, or null when hardly anything was played.</summary>
    public double? PerHour => Played.TotalMinutes >= 1 ? Value / Played.TotalHours : null;
}

/// <summary>Adds up the games and the earnings of a player (the Session tool). Everything is worked out again every time, so a reward typed in later counts for the past too.</summary>
public static class PlayStats
{
    /// <summary>Today, this week (from Monday), this month and all of it, in this time zone.</summary>
    public static IReadOnlyList<PlayPeriod> Periods(DateTime nowUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date;
        DateTime Utc(DateTime day) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), zone);
        var monday = local.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        return new[]
        {
            new PlayPeriod("Today", Utc(local), nowUtc),
            new PlayPeriod("This week", Utc(monday), nowUtc),
            new PlayPeriod("This month", Utc(new DateTime(local.Year, local.Month, 1)), nowUtc),
            new PlayPeriod("All time", DateTime.MinValue, nowUtc),
        };
    }

    public static PlayTotals For(DateTime fromUtc, DateTime toUtc, IEnumerable<PlayedGame> games, IEnumerable<EarnedMission> missions, long mgValue, long councilValue)
    {
        var played = TimeSpan.Zero;
        int count = 0, crashes = 0;
        foreach (var g in games)
        {
            var a = g.StartUtc > fromUtc ? g.StartUtc : fromUtc;
            var b = g.EndUtc < toUtc ? g.EndUtc : toUtc;
            if (b > a) played += b - a;
            if (g.StartUtc >= fromUtc && g.StartUtc < toUtc)
            {
                count++;
                if (g.Crashed) crashes++;
            }
        }
        int completed = 0, without = 0;
        long auec = 0, mg = 0, council = 0;
        foreach (var m in missions)
        {
            if (m.EndedUtc < fromUtc || m.EndedUtc >= toUtc) continue;
            completed++;
            if (!m.HasReward) without++;
            auec += m.Auec ?? 0;
            mg += m.Mg ?? 0;
            council += m.Council ?? 0;
        }
        return new PlayTotals(played, count, crashes, completed, without, auec, mg, council, auec + (double)mg * mgValue + (double)council * councilValue);
    }

    public static PlayTotals For(PlayPeriod p, IEnumerable<PlayedGame> games, IEnumerable<EarnedMission> missions, long mgValue, long councilValue) =>
        For(p.FromUtc, p.ToUtc, games, missions, mgValue, councilValue);
}
