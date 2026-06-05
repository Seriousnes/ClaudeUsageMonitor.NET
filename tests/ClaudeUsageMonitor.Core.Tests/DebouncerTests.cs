using ClaudeUsageMonitor.Core;
using Microsoft.Extensions.Time.Testing;

namespace ClaudeUsageMonitor.Core.Tests;

public class DebouncerTests
{
    [Fact]
    public void Rapid_triggers_collapse_to_one_fire()
    {
        var time = new FakeTimeProvider();
        var fires = 0;
        var d = new Debouncer(time, TimeSpan.FromSeconds(2), () => fires++);

        d.Trigger();
        time.Advance(TimeSpan.FromSeconds(1));
        d.Trigger();                              // resets the window
        time.Advance(TimeSpan.FromSeconds(1));    // 1s since last trigger — not yet
        Assert.Equal(0, fires);
        time.Advance(TimeSpan.FromSeconds(1));    // 2s of quiet — fires once
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Fires_again_after_a_new_trigger()
    {
        var time = new FakeTimeProvider();
        var fires = 0;
        var d = new Debouncer(time, TimeSpan.FromSeconds(2), () => fires++);

        d.Trigger();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, fires);
        d.Trigger();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, fires);
    }
}
