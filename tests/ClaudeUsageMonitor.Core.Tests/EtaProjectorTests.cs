using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class EtaProjectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset = T0.AddHours(3);

    private static IReadOnlyList<TrackedWindow> Tracked(string label, string key, double util)
        => [new TrackedWindow(label, key, new UsageWindow(util, Reset))];

    [Fact]
    public void Single_sample_yields_no_eta()
    {
        var p = new EtaProjector();
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 50), T0);
        Assert.Null(p.ProjectSoonest());
    }

    [Fact]
    public void Rising_slope_projects_eta()
    {
        var p = new EtaProjector();
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 50), T0);
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 60), T0.AddMinutes(10)); // +10%/10min => +60%/hr
        var eta = p.ProjectSoonest();
        Assert.NotNull(eta);
        Assert.Equal("Week", eta!.Value.Label);
        // remaining 40% at 60%/hr => 40 min
        Assert.Equal(40, eta.Value.Eta.TotalMinutes, precision: 1);
    }

    [Fact]
    public void Flat_or_declining_slope_yields_no_eta()
    {
        var p = new EtaProjector();
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 60), T0);
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 60), T0.AddMinutes(10));
        Assert.Null(p.ProjectSoonest());
    }

    [Fact]
    public void Soonest_window_wins()
    {
        var p = new EtaProjector();
        IReadOnlyList<TrackedWindow> a = [
            new("Session", "five_hour", new UsageWindow(50, Reset)),
            new("Week", "seven_day_opus", new UsageWindow(50, Reset))];
        IReadOnlyList<TrackedWindow> b = [
            new("Session", "five_hour", new UsageWindow(70, Reset)),   // +20%/10min, steeper
            new("Week", "seven_day_opus", new UsageWindow(55, Reset))];
        p.AddSnapshot(a, T0);
        p.AddSnapshot(b, T0.AddMinutes(10));
        Assert.Equal("Session", p.ProjectSoonest()!.Value.Label);
    }

    [Fact]
    public void Key_change_does_not_splice_slopes()
    {
        // opus rising fast, then opus disappears and Week falls back to aggregate at a low, flat value.
        var p = new EtaProjector();
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 80), T0);
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 90), T0.AddMinutes(10));
        // fallback: same Label "Week", different Key — opus history is pruned, aggregate has one sample.
        p.AddSnapshot(Tracked("Week", "seven_day", 41), T0.AddMinutes(20));
        Assert.Null(p.ProjectSoonest());   // no garbage 90->41 slope
    }
}
