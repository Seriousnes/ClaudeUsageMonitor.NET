using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class StatusCalculatorTests
{
    private static readonly PaceSettings S = new();
    private static TrackedWindow W(double util) => new("X", "x", new UsageWindow(util, DateTimeOffset.UnixEpoch));
    private static PaceResult Pace(Status status) => new(0.5, 0, 0, status, null, null);

    [Fact]
    public void Stale_overrides_everything()
        => Assert.Equal(Status.Stale, StatusCalculator.Compute(Pace(Status.Red), [W(5)], S, isStale: true));

    [Fact]
    public void Pace_band_drives_color_when_usage_low()
        => Assert.Equal(Status.Yellow, StatusCalculator.Compute(Pace(Status.Yellow), [W(10)], S, isStale: false));

    [Fact]
    public void High_usage_net_forces_orange_from_any_window()
        => Assert.Equal(Status.Orange, StatusCalculator.Compute(Pace(Status.Green), [W(10), W(88)], S, isStale: false));

    [Fact]
    public void High_usage_net_forces_red_from_any_window()
        => Assert.Equal(Status.Red, StatusCalculator.Compute(Pace(Status.Green), [W(96)], S, isStale: false));

    [Fact]
    public void Worst_of_pace_and_net_wins()
        => Assert.Equal(Status.Red, StatusCalculator.Compute(Pace(Status.Orange), [W(96)], S, isStale: false));

    [Fact]
    public void Null_pace_defaults_to_green_band()
        => Assert.Equal(Status.Green, StatusCalculator.Compute(null, [W(10)], S, isStale: false));

    [Fact]
    public void Late_session_high_usage_goes_red_via_net()
    {
        // The user's cited scenario: 96% used at 99% elapsed. Pace alone reads Yellow
        // (projected ~97); the high-usage net must override it to Red. Drives the real
        // SessionPace -> StatusCalculator path, not a synthetic band.
        var now = new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
        var win = new UsageWindow(96, now + TimeSpan.FromHours(5 * (1 - 0.99)));   // 99% elapsed
        var pace = SessionPace.Evaluate(win, now, S);
        Assert.Equal(Status.Yellow, pace.Status);
        Assert.Equal(Status.Red, StatusCalculator.Compute(pace, [new TrackedWindow("X", "x", win)], S, isStale: false));
    }
}
