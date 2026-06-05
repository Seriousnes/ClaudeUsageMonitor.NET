using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class FreshnessEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    [Fact]
    public void Latest_poll_ok_is_live_at_any_age()
        => Assert.Equal(Freshness.Live,
            FreshnessEvaluator.Evaluate(true, Now - TimeSpan.FromHours(3), Now, Threshold));

    [Fact]
    public void Failed_poll_within_grace_is_recent()
        => Assert.Equal(Freshness.Recent,
            FreshnessEvaluator.Evaluate(false, Now - TimeSpan.FromMinutes(4), Now, Threshold));

    [Fact]
    public void Failed_poll_at_exactly_threshold_is_recent()
        => Assert.Equal(Freshness.Recent,
            FreshnessEvaluator.Evaluate(false, Now - TimeSpan.FromMinutes(5), Now, Threshold));

    [Fact]
    public void Failed_poll_past_threshold_is_stale()
        => Assert.Equal(Freshness.Stale,
            FreshnessEvaluator.Evaluate(false, Now - TimeSpan.FromMinutes(6), Now, Threshold));

    [Fact]
    public void No_successful_poll_yet_is_stale()
        => Assert.Equal(Freshness.Stale,
            FreshnessEvaluator.Evaluate(false, null, Now, Threshold));
}
