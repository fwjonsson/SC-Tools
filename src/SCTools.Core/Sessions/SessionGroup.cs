using System.Text.RegularExpressions;

namespace SCTools.Sessions;

/// <summary>A group of hauling missions that belong to the same hauling session.</summary>
public sealed class SessionGroup
{
    public List<Mission> Missions = new();
    public List<Mission> Failed = new();   // failed or abandoned hauling missions of the same time: not part of the session, but their cargo can be in a run
    public DateTime Start;      // UTC
    public DateTime End;        // UTC, used for grouping only
    public bool Manual;         // started with Start Session: one session from the start to the stop, however long
    public DateTime? Stop;      // UTC, when a manual session was stopped
    public string? CustomKey;   // the key of a manual session: its start time

    /// <summary>Stable id for saving your own notes: the id of the earliest mission (a manual session: its start time).</summary>
    public string Key => CustomKey ?? (Missions.Count > 0 ? Missions[0].Id : "");
}
