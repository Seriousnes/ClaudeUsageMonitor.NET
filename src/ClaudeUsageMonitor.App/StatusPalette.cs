using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

/// <summary>
/// Single source of truth for the green→yellow→orange→red status palette, shared by the tray icon
/// and the widget usage bars. Returns raw RGB so each consumer builds its own Color type.
/// </summary>
public static class StatusPalette
{
    public static (byte R, byte G, byte B) Rgb(Status status) => status switch
    {
        Status.Green => (0x3F, 0xB9, 0x50),
        Status.Yellow => (0xE6, 0xC2, 0x29),
        Status.Orange => (0xE6, 0x8A, 0x00),
        Status.Red => (0xD9, 0x3A, 0x2B),
        _ => (0x9E, 0x9E, 0x9E),   // Stale = grey
    };
}
