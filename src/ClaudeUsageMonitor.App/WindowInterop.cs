using System.Runtime.InteropServices;

namespace ClaudeUsageMonitor.App;

/// <summary>
/// The single contained P/Invoke in the app (managed code is preferred everywhere else): toggles the
/// WS_EX_TRANSPARENT extended style so mouse input falls through the always-on-top widget to the window
/// behind it. The widget is already WS_EX_LAYERED via AllowsTransparency, so this is the only flag to flip.
/// </summary>
internal static class WindowInterop
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void SetClickThrough(IntPtr hwnd, bool on)
    {
        if (hwnd == IntPtr.Zero) return;                 // handle not created yet; the caller re-applies later
        var current = GetWindowLong(hwnd, GWL_EXSTYLE);
        var updated = on ? current | WS_EX_TRANSPARENT : current & ~WS_EX_TRANSPARENT;
        if (updated != current)
            SetWindowLong(hwnd, GWL_EXSTYLE, updated);
    }
}
