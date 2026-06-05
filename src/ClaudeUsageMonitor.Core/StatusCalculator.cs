namespace ClaudeUsageMonitor.Core;

public static class StatusCalculator
{
    public const double YellowAt = 50.0;
    public const double OrangeAt = 80.0;
    public const double RedAt = 95.0;

    /// <summary>Stale overrides everything; otherwise color reflects the max utilization across tracked windows.</summary>
    public static Status Compute(IReadOnlyList<TrackedWindow> tracked, bool isStale)
    {
        if (isStale) return Status.Stale;
        double max = 0;
        foreach (var t in tracked)
            if (t.Window.Utilization > max) max = t.Window.Utilization;
        return FromUtilization(max);
    }

    public static Status FromUtilization(double util) => util switch
    {
        >= RedAt => Status.Red,
        >= OrangeAt => Status.Orange,
        >= YellowAt => Status.Yellow,
        _ => Status.Green,
    };
}
