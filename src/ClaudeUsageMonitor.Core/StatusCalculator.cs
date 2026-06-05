namespace ClaudeUsageMonitor.Core;

public static class StatusCalculator
{
    /// <summary>
    /// Icon status: stale greys everything; otherwise the worst of the Session pace band and the
    /// high-usage safety net evaluated across all tracked windows.
    /// </summary>
    public static Status Compute(PaceResult? pace, IReadOnlyList<TrackedWindow> tracked, PaceSettings settings, bool isStale)
    {
        if (isStale) return Status.Stale;
        var status = pace?.Status ?? Status.Green;
        foreach (var t in tracked)
            status = Worse(status, HighUsage(t.Window.Utilization, settings));
        return status;
    }

    private static Status HighUsage(double util, PaceSettings s)
    {
        if (util >= s.HighUsageRed) return Status.Red;
        if (util >= s.HighUsageOrange) return Status.Orange;
        return Status.Green;
    }

    private static Status Worse(Status a, Status b) => (Status)Math.Max((int)a, (int)b);
}
