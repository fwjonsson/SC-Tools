using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.GameLog;

/// <summary>A "Deliver 519/551 SCU of Potassium" popup seen in the log.</summary>
public sealed class DeliverNote
{
    public DateTime Time;
    public int Delivered;
    public int Total;
}
