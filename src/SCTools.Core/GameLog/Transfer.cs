using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.GameLog;

/// <summary>Money sent between two players. An empty From or To means "the player whose log this is".</summary>
public sealed class Transfer
{
    public DateTime Time;
    public string From = "";
    public string To = "";
    public decimal Amount;
}
