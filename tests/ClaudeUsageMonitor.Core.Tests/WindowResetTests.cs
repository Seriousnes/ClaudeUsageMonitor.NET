namespace ClaudeUsageMonitor.Core.Tests;

public class WindowResetTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Before_reset_leaves_window_unchanged()
    {
        var w = new UsageWindow(73, Now + TimeSpan.FromHours(1));
        Assert.Equal(w, WindowReset.Apply(w, Now));
    }

    [Fact]
    public void At_reset_zeroes_utilization()
    {
        var w = new UsageWindow(73, Now);
        Assert.Equal(0, WindowReset.Apply(w, Now).Utilization);
    }

    [Fact]
    public void After_reset_zeroes_utilization()
    {
        var w = new UsageWindow(73, Now - TimeSpan.FromMinutes(1));
        Assert.Equal(0, WindowReset.Apply(w, Now).Utilization);
    }

    [Fact]
    public void Reset_preserves_resets_at()
    {
        var resetsAt = Now - TimeSpan.FromMinutes(1);
        var w = new UsageWindow(73, resetsAt);
        Assert.Equal(resetsAt, WindowReset.Apply(w, Now).ResetsAt);
    }
}
