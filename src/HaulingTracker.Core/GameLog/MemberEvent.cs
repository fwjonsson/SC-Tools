using System.Globalization;
using System.Text.RegularExpressions;

namespace HaulingTracker.GameLog;

public sealed class MemberEvent
{
    public string Mission = "";
    public string Player = "";   // the player id
    public DateTime Time;
    public bool Left;
}
