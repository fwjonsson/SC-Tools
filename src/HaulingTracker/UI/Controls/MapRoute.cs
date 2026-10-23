using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace HaulingTracker.UI.Controls;

/// <summary>One trip on the map: Ruin, the stops in flying order, back to Ruin.</summary>
sealed class MapRoute
{
    public string Label = "";   // shown in the legend (empty = not in the legend)
    public Color Color;
    public List<string> Stops = new();   // station ids
    public bool Bold;
    public string? From;   // a run in progress: the line starts at this station instead of at Ruin
    public int Passed;   // a run that is drawn by itself: how many of its stops are done, so the waves run on the leg after them (0 = the first leg)
    public bool Dashed;   // a trip that is not the next one: dotted line
    public object? Tag;
}
