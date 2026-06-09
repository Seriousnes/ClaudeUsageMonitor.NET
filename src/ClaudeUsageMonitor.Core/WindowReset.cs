namespace ClaudeUsageMonitor.Core;

/// <summary>
/// Zeroes a window's utilization the moment its reset time passes, leaving <see cref="UsageWindow.ResetsAt"/>
/// unchanged so the row reads 0% with "resets now" until the next successful poll. A no-op on fresh data.
/// </summary>
public static class WindowReset
{
    public static UsageWindow Apply(UsageWindow w, DateTimeOffset now)
        => now >= w.ResetsAt ? w with { Utilization = 0 } : w;
}
