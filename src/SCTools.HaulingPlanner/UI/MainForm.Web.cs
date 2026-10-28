namespace SCTools.UI;

public sealed partial class MainForm
{
    LiveWebServer? _web;
    readonly System.Windows.Forms.Timer _webTimer = new();
    readonly ToolStripMenuItem _webBtn = new();

    string WebButtonText() => _web != null ? "Live Web View: On (port " + _web.Port + ")" : "Live Web View: Off";

    void InitWebView()
    {
        _webTimer.Interval = 2000;
        _webTimer.Tick += (_, _) => PublishWeb();
        FormClosing += (_, _) => StopWeb();
        if (_data.WebView) StartWeb(false);
    }

    void ToggleWeb()
    {
        if (_web != null)
        {
            StopWeb();
            _data.WebView = false;
            _state.Save();
            SetStatus("The live web view is off.");
            return;
        }
        if (StartWeb(true))
        {
            _data.WebView = true;
            _state.Save();
        }
    }

    bool StartWeb(bool announce)
    {
        var port = _data.WebViewPort is >= 1024 and <= 65535 ? _data.WebViewPort : 8765;
        try
        {
            var web = new LiveWebServer(port);
            web.Start();
            _web = web;
        }
        catch (Exception ex)
        {
            _web = null;
            _webBtn.Text = WebButtonText();
            SetStatus("Could not start the live web view on port " + port + ": " + ex.Message);
            return false;
        }
        _webBtn.Text = WebButtonText();
        _webTimer.Start();
        PublishWeb();
        SetStatus("Live web view on http://localhost:" + port + " (read only).");
        return true;
    }

    void StopWeb()
    {
        _webTimer.Stop();
        _web?.Dispose();
        _web = null;
        _webBtn.Text = WebButtonText();
    }

    void PublishWeb()
    {
        if (_web == null || IsDisposed) return;
        try
        {
            _web.Json = WebSnapshot.Json(_tracker, new WebSnapshotOptions
            {
                Dark = Theme.Dark,
                MeasureBold = text => TextRenderer.MeasureText(text, PlanBoldFont).Width,   // the Deliver/Collect column of the Trip Planner as wide as in the program
            });
        }
        catch (Exception ex)
        {
            SetStatus("Live web view: " + ex.Message);
        }
    }
}
