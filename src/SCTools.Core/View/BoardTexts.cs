using System.Globalization;

namespace SCTools.View;

/// <summary>Texts of the Session tab that the program and the web view show the same way.</summary>
public static class BoardTexts
{
    /// <summary>A session in the session list: "Session: 23 Oct 13:28 to now (running)  (9 missions)".</summary>
    public static string DescribeSession(SessionGroup g)
    {
        var s = SessionBuilder.Summarize(g);
        var start = s.Start.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
        if (g.Manual)
        {
            var until = g.Stop.HasValue ? g.Stop.Value.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture) : "now";
            return "Session: " + start + " to " + until + (g.Stop.HasValue ? "" : " (running)") + "  (" + g.Missions.Count + " missions)";
        }
        var end = s.End.HasValue ? s.End.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) : "ongoing";
        return start + " to " + end + "  (" + g.Missions.Count + " missions)";
    }

    /// <summary>Missions still waiting at this location / missions that have cargo there. 5 missions start as 5/5.</summary>
    public static string MissionsText(LocationRow r) => (r.Missions - r.Loaded) + "/" + r.Missions;

    /// <summary>The Handed In cell of a finished stop: when, or that cargo is missing, or how much is handed in so far.</summary>
    public static string DeliveredText(LocationRow r)
    {
        if (r.Missing > 0 && r.Missing >= r.Missions) return "Cargo Missing";
        if (r.DeliveredTime.HasValue) return FormatTime(r.DeliveredTime.Value) + (r.Missing > 0 ? "  (" + r.Missing + " missing)" : "");
        if (r.Delivered > 0) return "Partial " + r.Delivered + "/" + r.Missions;
        return "";
    }
}
