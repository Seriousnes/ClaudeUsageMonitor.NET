using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class PollScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Idle = TimeSpan.FromHours(1);

    private static bool Should(DateTimeOffset activity, DateTimeOffset lastPoll)
        => PollSchedule.ShouldPoll(Now, activity, lastPoll, ActiveWindow, Idle);

    [Fact]
    public void Active_session_polls_every_tick_even_just_after_a_poll()
        => Assert.True(Should(activity: Now.AddSeconds(-30), lastPoll: Now.AddSeconds(-5)));

    [Fact]
    public void Idle_within_heartbeat_window_does_not_poll()
        => Assert.False(Should(activity: Now.AddMinutes(-10), lastPoll: Now.AddMinutes(-10)));

    [Fact]
    public void Idle_past_heartbeat_polls()
        => Assert.True(Should(activity: Now.AddHours(-3), lastPoll: Now.AddHours(-2)));

    [Fact]
    public void Cold_start_polls_via_due_heartbeat()
        => Assert.True(Should(activity: DateTimeOffset.MinValue, lastPoll: DateTimeOffset.MinValue));

    [Fact]
    public void Activity_exactly_at_window_edge_is_not_active()
        // boundary: now - lastActivity == activeWindow is NOT active (strict <); and the heartbeat isn't due.
        => Assert.False(Should(activity: Now - ActiveWindow, lastPoll: Now.AddSeconds(-1)));

    [Fact]
    public void Heartbeat_exactly_at_interval_is_due()
        => Assert.True(Should(activity: Now.AddHours(-3), lastPoll: Now - Idle));
}
