using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class SessionPaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly PaceSettings S = new();

    // resets_at chosen so that `timeFrac` of the 5h window has elapsed at Now.
    private static UsageWindow Session(double util, double timeFrac)
        => new(util, Now + TimeSpan.FromHours(5 * (1 - timeFrac)));

    [Theory]
    [InlineData(15, 0.30, Status.Green)]    // projected 50  — user's green case
    [InlineData(21, 0.19, Status.Yellow)]   // projected ~110 — user's yellow case
    [InlineData(30, 0.20, Status.Red)]      // projected 150 — user's red case
    public void Reference_cases_map_to_expected_band(double util, double timeFrac, Status expected)
        => Assert.Equal(expected, SessionPace.Evaluate(Session(util, timeFrac), Now, S).Status);

    [Fact]
    public void Mild_early_usage_is_suppressed_to_green()
        => Assert.Equal(Status.Green, SessionPace.Evaluate(Session(8, 0.05), Now, S).Status);

    [Fact]
    public void Heavy_early_usage_still_trips_red()
        => Assert.Equal(Status.Red, SessionPace.Evaluate(Session(25, 0.05), Now, S).Status);

    [Fact]
    public void Floor_suppressed_green_has_no_time_to_limit()
    {
        var r = SessionPace.Evaluate(Session(8, 0.05), Now, S);   // projected 160 but floor-suppressed to Green
        Assert.Equal(Status.Green, r.Status);
        Assert.Null(r.TimeToLimit);
        Assert.Null(r.LimitAt);
    }

    [Theory]
    [InlineData(47.5, 0.50, Status.Yellow)]   // projected exactly 95
    [InlineData(47.0, 0.50, Status.Green)]    // projected 94
    [InlineData(57.5, 0.50, Status.Orange)]   // projected exactly 115
    [InlineData(70.0, 0.50, Status.Red)]      // projected exactly 140
    public void Band_boundaries(double util, double timeFrac, Status expected)
        => Assert.Equal(expected, SessionPace.Evaluate(Session(util, timeFrac), Now, S).Status);

    [Fact]
    public void On_track_returns_time_to_limit()
    {
        var r = SessionPace.Evaluate(Session(30, 0.20), Now, S);   // 1h elapsed, projected 150
        Assert.NotNull(r.TimeToLimit);
        // remaining 70% at 30%/hr (30% used in 1h) => 70/30 h = 2h20m
        Assert.Equal(140, r.TimeToLimit!.Value.TotalMinutes, precision: 0);
        Assert.Equal(Now + r.TimeToLimit.Value, r.LimitAt);
    }

    [Fact]
    public void Under_pace_has_no_time_to_limit()
        => Assert.Null(SessionPace.Evaluate(Session(15, 0.30), Now, S).TimeToLimit);

    [Fact]
    public void Zero_usage_is_green_without_dividing()
    {
        var r = SessionPace.Evaluate(Session(0, 0.50), Now, S);
        Assert.Equal(Status.Green, r.Status);
        Assert.Null(r.TimeToLimit);
    }

    [Fact]
    public void Zero_elapsed_is_green_without_dividing()
    {
        var r = SessionPace.Evaluate(Session(50, 0.0), Now, S);   // resets_at == Now + 5h
        Assert.Equal(Status.Green, r.Status);
        Assert.Null(r.TimeToLimit);
    }

    [Fact]
    public void Past_reset_clamps_time_fraction_to_one()
    {
        var r = SessionPace.Evaluate(new UsageWindow(80, Now - TimeSpan.FromHours(1)), Now, S);
        Assert.Equal(1.0, r.TimeFraction);
        Assert.Equal(80, r.Projected, precision: 0);
    }

    [Fact]
    public void Thresholds_are_read_from_settings()
    {
        var lower = new PaceSettings { OrangeProjected = 105 };   // projected ~110 now lands Orange
        Assert.Equal(Status.Orange, SessionPace.Evaluate(Session(21, 0.19), Now, lower).Status);
    }
}
