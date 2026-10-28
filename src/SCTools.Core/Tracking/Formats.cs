using System.Globalization;
using System.Text;

namespace SCTools.Tracking;

/// <summary>How numbers, times and distances are written in the tables, the status line and the web page (and how typed numbers are read).</summary>
public static class Formats
{
    /// <summary>A time of the log (UTC) as local hours and minutes: "13:05".</summary>
    public static string FormatTime(DateTime utc) =>
        utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>h:mm:ss, the hours going past 24: "1:05:09".</summary>
    public static string LongDuration(TimeSpan d) => (int)d.TotalHours + ":" + d.Minutes.ToString("00") + ":" + d.Seconds.ToString("00");

    /// <summary>h:mm: "1:05".</summary>
    public static string ShortDuration(TimeSpan d) => (int)d.TotalHours + ":" + d.Minutes.ToString("00");

    /// <summary>"23m 12s", "1h 05m 12s".</summary>
    public static string StatusSpan(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalHours >= 1) return (int)d.TotalHours + "h " + d.Minutes.ToString("00") + "m " + d.Seconds.ToString("00") + "s";
        if (d.TotalMinutes >= 1) return d.Minutes + "m " + d.Seconds.ToString("00") + "s";
        return d.Seconds + "s";
    }

    /// <summary>m:ss or h:mm:ss.</summary>
    public static string FlightSpan(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? (int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
    }

    /// <summary>A whole amount with separators, or nothing when there is none: "1,268,250".</summary>
    public static string Num(decimal v) => v > 0 ? v.ToString("N0", CultureInfo.InvariantCulture) : "";

    /// <summary>The value with the separators of the computer, or nothing when there is none.</summary>
    public static string ValueText(decimal v) => v <= 0 ? "" : Math.Round(v).ToString("N0");

    /// <summary>"+1,250", "-300", "0".</summary>
    public static string Signed(decimal v) => v.ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture);

    /// <summary>"3.2M" for big numbers, the full number below a million.</summary>
    public static string Millions(decimal v) =>
        v >= 1_000_000 ? (v / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M" : v.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>"12.5 Gm".</summary>
    public static string Gm(double v) => v.ToString("N1", CultureInfo.InvariantCulture) + " Gm";

    /// <summary>"12.5 Gm", or nothing for no distance.</summary>
    public static string Dist(double gm) => gm > 0 ? gm.ToString("N1", CultureInfo.InvariantCulture) + " Gm" : "";

    /// <summary>Gm per hour, or nothing.</summary>
    public static string GmPerHour(double gm, double seconds) =>
        gm > 0 && seconds > 0 ? (gm / (seconds / 3600.0)).ToString("N1", CultureInfo.InvariantCulture) : "";

    /// <summary>aUEC per hour, or nothing.</summary>
    public static string PerHour(decimal earned, double seconds) =>
        earned > 0 && seconds > 0 ? Num(earned / (decimal)(seconds / 3600.0)) : "";

    /// <summary>A typed time of day, "9:05" or "09:05".</summary>
    public static bool TryTime(string text, out TimeSpan value) =>
        TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out value);

    /// <summary>A typed amount: everything but the digits and the point is left out (spaces, commas, non-breaking spaces). 0 when there is no number.</summary>
    public static decimal ParseNumber(object? v)
    {
        var s = Convert.ToString(v, CultureInfo.CurrentCulture) ?? "";
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsDigit(ch) || ch == '.') sb.Append(ch);   // drops spaces, commas and non-breaking spaces
        }
        return decimal.TryParse(sb.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    /// <summary>A typed amount that can be negative ("-" or "−" anywhere in it).</summary>
    public static decimal ParseSigned(object? value)
    {
        var s = Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
        var negative = s.Contains('-') || s.Contains('−');
        return (negative ? -1m : 1m) * ParseNumber(s);
    }
}
