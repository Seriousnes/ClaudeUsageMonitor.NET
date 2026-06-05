using System.Globalization;

namespace ClaudeUsageMonitor.Core;

public static class ResetFormatter
{
    public static string Format(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var span = resetsAt - now;
        if (span <= TimeSpan.Zero) return "resets now";
        if (span < TimeSpan.FromHours(1)) return $"resets in {(int)span.TotalMinutes}m";
        if (span < TimeSpan.FromHours(24)) return $"resets in {(int)span.TotalHours}h {span.Minutes}m";
        return $"resets {resetsAt.ToString("ddd", CultureInfo.InvariantCulture)}";
    }
}
