using System.Globalization;
using System.Text.RegularExpressions;

namespace HaulingTracker.GameLog;

/// <summary>One state change of a mission objective (INPROGRESS or COMPLETED).</summary>
public sealed class ObjEvent
{
    public DateTime Time;
    public string State = "";
}
