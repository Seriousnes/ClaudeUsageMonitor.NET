using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class ResetFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Under_an_hour_shows_minutes()
        => Assert.Equal("resets in 38m", ResetFormatter.Format(Now.AddMinutes(38), Now));

    [Fact]
    public void Within_a_day_shows_hours_and_minutes()
        => Assert.Equal("resets in 1h 48m", ResetFormatter.Format(Now.AddMinutes(108), Now));

    [Fact]
    public void Beyond_a_day_shows_weekday()
    {
        // 2026-06-10 is a Wednesday
        var resets = new DateTimeOffset(2026, 6, 10, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal("resets Wed", ResetFormatter.Format(resets, Now));
    }

    [Fact]
    public void Past_reset_shows_now()
        => Assert.Equal("resets now", ResetFormatter.Format(Now.AddMinutes(-5), Now));
}
