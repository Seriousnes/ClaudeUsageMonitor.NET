using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class AlertEngineTests
{
    private static readonly DateTimeOffset R1 = new(2026, 6, 10, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset R2 = R1.AddDays(7);
    private static readonly DateTimeOffset Before = R1.AddHours(-1);   // a poll while the window is still open

    private static IReadOnlyList<TrackedWindow> One(string key, double util, DateTimeOffset reset)
        => [new TrackedWindow("Week", key, new UsageWindow(util, reset))];

    [Fact]
    public void Fires_each_threshold_once_as_it_is_crossed()
    {
        var e = new AlertEngine([80, 95]);
        Assert.Empty(e.Evaluate(One("seven_day", 70, R1), Before));

        var at80 = e.Evaluate(One("seven_day", 82, R1), Before);
        Assert.Single(at80);
        Assert.Equal(80, at80[0].Threshold);
        Assert.Equal("Week", at80[0].WindowLabel);

        Assert.Empty(e.Evaluate(One("seven_day", 90, R1), Before));   // 80 already fired, 95 not reached

        var at95 = e.Evaluate(One("seven_day", 96, R1), Before);
        Assert.Single(at95);
        Assert.Equal(95, at95[0].Threshold);

        Assert.Empty(e.Evaluate(One("seven_day", 99, R1), Before));   // both fired
    }

    [Fact]
    public void Re_arms_when_resets_at_advances()
    {
        var e = new AlertEngine([80, 95]);
        e.Evaluate(One("seven_day", 82, R1), Before);                 // fires 80
        // A week later: the old boundary (R1) has passed and the API reports the next reset (R2).
        var afterReset = e.Evaluate(One("seven_day", 85, R2), R1.AddDays(1));
        Assert.Single(afterReset);
        Assert.Equal(80, afterReset[0].Threshold);
    }

    [Fact]
    public void Re_arms_when_utilization_drops_below_lowest_threshold()
    {
        var e = new AlertEngine([80, 95]);
        e.Evaluate(One("seven_day", 82, R1), Before);                 // fires 80
        Assert.Empty(e.Evaluate(One("seven_day", 10, R1), Before));   // dropped below 80 -> re-armed, but 10<80 so no fire
        Assert.Single(e.Evaluate(One("seven_day", 82, R1), Before));  // crosses again -> fires
    }

    [Fact]
    public void Does_not_refire_when_resets_at_drifts_forward_each_poll()
    {
        // The API stamps resets_at from a request-time clock, so it creeps forward fractions of a
        // second each poll while the real window is unchanged. A bare "resets_at advanced" re-arm
        // mistakes that drift for a new window and re-fires every poll.
        var e = new AlertEngine([80, 95]);
        Assert.Single(e.Evaluate(One("seven_day", 90, R1), Before));     // fires 80 once

        for (var i = 1; i <= 5; i++)
        {
            var drifted = R1.AddTicks(i);                                // sub-millisecond forward drift
            Assert.Empty(e.Evaluate(One("seven_day", 90, drifted), Before));   // must NOT re-fire
        }
    }

    [Fact]
    public void Windows_are_independent()
    {
        var e = new AlertEngine([80, 95]);
        IReadOnlyList<TrackedWindow> two = [
            new("Session", "five_hour", new UsageWindow(82, R1)),
            new("Week", "seven_day", new UsageWindow(50, R1))];
        var alerts = e.Evaluate(two, Before);
        Assert.Single(alerts);
        Assert.Equal("Session", alerts[0].WindowLabel);
    }

    [Fact]
    public void Key_change_does_not_spuriously_refire()
    {
        // opus fires 80; then Week falls back to aggregate (different Key) at a low value.
        var e = new AlertEngine([80, 95]);
        Assert.Single(e.Evaluate(One("seven_day_opus", 82, R1), Before));    // opus fires 80
        Assert.Empty(e.Evaluate(One("seven_day", 41, R1), Before));          // aggregate is a fresh key at 41 — no fire, no refire
    }
}
