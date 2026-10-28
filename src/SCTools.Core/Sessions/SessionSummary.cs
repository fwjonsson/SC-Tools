using System.Text.RegularExpressions;

namespace SCTools.Sessions;

public sealed class SessionSummary
{
    public DateTime Start;      // UTC
    public DateTime? End;       // UTC, time of the last completed mission
    public int Completed;
    public int Active;          // missions accepted but not completed yet
    public int Total;
}
