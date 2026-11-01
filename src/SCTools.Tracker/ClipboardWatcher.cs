using System.Runtime.InteropServices;

namespace SCTools.Tracker;

/// <summary>
/// Tells when the clipboard changed (Windows' own notice, WM_CLIPBOARDUPDATE, to a window that is never shown), so the tracker can see the game's
/// /showlocation result when the game puts it there (Frank, 1 Nov 2026). It reads nothing itself: the tray app looks at the text and keeps only that one.
/// </summary>
sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    const int ClipboardUpdate = 0x031D;
    static readonly IntPtr MessageOnly = new(-3);   // HWND_MESSAGE: a window for messages alone

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    /// <summary>The clipboard changed (on the thread of the tray app).</summary>
    public event Action? Changed;

    public ClipboardWatcher()
    {
        CreateHandle(new CreateParams { Parent = MessageOnly });
        AddClipboardFormatListener(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == ClipboardUpdate) Changed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }
}
