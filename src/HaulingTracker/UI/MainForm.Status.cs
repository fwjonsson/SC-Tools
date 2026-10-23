namespace HaulingTracker.UI;

// The small team status line under the map of the Trip Planner (and the same line on the web page): what the crew is doing right now, taken from the logs
// (who sets course for which station, who arrives, loads, delivers, hands in, dies), mixed with some stats and a bit of fun. It only talks about what the logs show:
// a member without a log of his own is only in the lines that come from the runs. What it says comes from StatusFeed (in the core); this is the line itself.
public sealed partial class MainForm
{
    StatusLine? _statusLine;              // the line on the Trip Planner page, when it is on screen

    /// <summary>The line under the map: the text of the team status in small muted letters, fading over to the next text when it changes.</summary>
    sealed class StatusLine : Control
    {
        string _text = "";
        string _next = "";
        int _alpha = 255;
        int _dir;   // -1 = fading out, 1 = fading in, 0 = still
        readonly System.Windows.Forms.Timer _fade = new() { Interval = 40 };

        public StatusLine()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 11f, FontStyle.Italic);
            Height = 30;
            TabStop = false;
            _fade.Tick += (_, _) => Step();
        }

        /// <summary>Shows a text: at once, or fading the old text out and the new one in.</summary>
        public void ShowText(string text, bool fade)
        {
            if (!fade || !IsHandleCreated || _text.Length == 0 || text.Length == 0)
            {
                _text = text;
                _next = text;
                _alpha = 255;
                _dir = 0;
                _fade.Stop();
                Invalidate();
                return;
            }
            _next = text;
            if (_dir == 0)
            {
                _dir = -1;
                _fade.Start();
            }
            else if (_dir > 0)
            {
                _text = text;   // it was fading in: just carry on with the new text
            }
        }

        void Step()
        {
            if (_dir < 0)
            {
                _alpha -= 64;
                if (_alpha <= 0)
                {
                    _alpha = 0;
                    _text = _next;
                    _dir = 1;
                }
            }
            else if (_dir > 0)
            {
                _alpha += 30;
                if (_alpha >= 255)
                {
                    _alpha = 255;
                    _dir = 0;
                    _fade.Stop();
                }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_text.Length == 0) return;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(_alpha, 0, 255), Theme.Muted));
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString(_text, Font, brush, ClientRectangle, format);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _fade.Dispose();
            base.Dispose(disposing);
        }
    }
}
