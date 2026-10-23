using System.Globalization;
using System.Text;

namespace HaulingTracker.UI.Controls;

/// <summary>A table that paints without flicker (the session clock changes every second).</summary>
sealed class BufferedTable : TableLayoutPanel
{
    public BufferedTable()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}
