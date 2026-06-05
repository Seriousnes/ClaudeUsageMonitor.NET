using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class StatusCalculatorTests
{
    private static readonly DateTimeOffset R = DateTimeOffset.UnixEpoch;
    private static TrackedWindow W(double util) => new("X", "x", new UsageWindow(util, R));

    [Theory]
    [InlineData(0.0, Status.Green)]
    [InlineData(49.9, Status.Green)]
    [InlineData(50.0, Status.Yellow)]
    [InlineData(79.9, Status.Yellow)]
    [InlineData(80.0, Status.Orange)]
    [InlineData(94.9, Status.Orange)]
    [InlineData(95.0, Status.Red)]
    [InlineData(100.0, Status.Red)]
    public void FromUtilization_maps_boundaries(double util, Status expected)
        => Assert.Equal(expected, StatusCalculator.FromUtilization(util));

    [Fact]
    public void Compute_uses_max_across_tracked_windows()
        => Assert.Equal(Status.Orange, StatusCalculator.Compute([W(12), W(81), W(40)], isStale: false));

    [Fact]
    public void Stale_overrides_color_even_when_green()
        => Assert.Equal(Status.Stale, StatusCalculator.Compute([W(5)], isStale: true));

    [Fact]
    public void Compute_empty_is_green()
        => Assert.Equal(Status.Green, StatusCalculator.Compute([], isStale: false));
}
