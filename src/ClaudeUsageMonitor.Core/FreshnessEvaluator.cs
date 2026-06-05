namespace ClaudeUsageMonitor.Core;

/// <summary>
/// Maps the latest poll outcome and the age of the last good reading to a freshness state.
/// A single failed poll no longer greys the widget: the reading stays Live-colored while it is
/// still within the grace period (Recent), and only goes Stale once genuinely outdated.
/// </summary>
public static class FreshnessEvaluator
{
    /// <summary>How long a good reading survives failed polls before it is considered stale.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(5);

    public static Freshness Evaluate(bool latestPollSucceeded, DateTimeOffset? lastGood,
        DateTimeOffset now, TimeSpan threshold)
    {
        if (latestPollSucceeded) return Freshness.Live;
        if (lastGood is null) return Freshness.Stale;            // never polled successfully -> no data
        return now - lastGood.Value > threshold ? Freshness.Stale : Freshness.Recent;
    }
}
