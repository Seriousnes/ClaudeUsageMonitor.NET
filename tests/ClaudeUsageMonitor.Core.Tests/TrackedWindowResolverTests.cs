using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class TrackedWindowResolverTests
{
    private static readonly DateTimeOffset R = new(2026, 6, 10, 18, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snap(params (string key, double util)[] models)
    {
        var dict = models.ToDictionary(m => m.key, m => new UsageWindow(m.util, R));
        return new UsageSnapshot(new UsageWindow(13, R), new UsageWindow(20, R), dict,
            DateTimeOffset.UnixEpoch, "Max 5x");
    }

    [Fact]
    public void Session_is_always_first()
    {
        var t = TrackedWindowResolver.Resolve(Snap(), ["highest"]);
        Assert.Equal("Session", t[0].Label);
        Assert.Equal("five_hour", t[0].Key);
        Assert.Equal(13, t[0].Window.Utilization);
    }

    [Fact]
    public void Highest_picks_opus_when_present()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64), ("sonnet", 2)), ["highest"]);
        var week = t[1];
        Assert.Equal("Week", week.Label);
        Assert.Equal("seven_day_opus", week.Key);
        Assert.Equal(64, week.Window.Utilization);
    }

    [Fact]
    public void Highest_falls_back_to_sonnet_then_aggregate()
    {
        var sonnetOnly = TrackedWindowResolver.Resolve(Snap(("sonnet", 2)), ["highest"]);
        Assert.Equal("seven_day_sonnet", sonnetOnly[1].Key);

        var none = TrackedWindowResolver.Resolve(Snap(), ["highest"]);   // no per-model windows
        Assert.Equal("Week", none[1].Label);
        Assert.Equal("seven_day", none[1].Key);      // aggregate fallback — never blank
        Assert.Equal(20, none[1].Window.Utilization);
    }

    [Fact]
    public void Empty_list_uses_aggregate_weekly()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64)), []);
        Assert.Equal("Week", t[1].Label);
        Assert.Equal("seven_day", t[1].Key);
    }

    [Fact]
    public void Explicit_single_family_labelled_Week()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64), ("sonnet", 2)), ["opus"]);
        Assert.Equal(2, t.Count);
        Assert.Equal("Week", t[1].Label);
        Assert.Equal("seven_day_opus", t[1].Key);
    }

    [Fact]
    public void Explicit_missing_family_falls_back_to_aggregate()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("sonnet", 2)), ["opus"]);  // opus absent
        Assert.Equal("Week", t[1].Label);
        Assert.Equal("seven_day", t[1].Key);
    }

    [Fact]
    public void Explicit_multiple_families_use_model_names()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64), ("sonnet", 2)), ["opus", "sonnet"]);
        Assert.Equal(3, t.Count);
        Assert.Equal(["Opus", "Sonnet"], [.. t.Skip(1).Select(w => w.Label)]);
    }

    [Fact]
    public void Star_uses_aggregate_as_Week_plus_models_by_name()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64), ("sonnet", 2)), ["*"]);
        Assert.Equal("Week", t[1].Label);
        Assert.Equal("seven_day", t[1].Key);
        Assert.Contains(t, w => w.Label == "Opus");
        Assert.Contains(t, w => w.Label == "Sonnet");
    }
}
