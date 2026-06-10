namespace ClaudeUsageMonitor.Core;

/// <summary>
/// Decides, on each timer tick, whether to actually hit the usage API. An active Claude session (a JSONL
/// change within <paramref name="activeWindow"/>) polls every tick; when idle, polling drops to a slow
/// heartbeat — only once <paramref name="idleInterval"/> has elapsed since the last poll.
/// </summary>
public static class PollSchedule
{
    public static bool ShouldPoll(DateTimeOffset now, DateTimeOffset lastActivity, DateTimeOffset lastPoll,
        TimeSpan activeWindow, TimeSpan idleInterval)
    {
        var active = now - lastActivity < activeWindow;
        var heartbeatDue = now - lastPoll >= idleInterval;
        return active || heartbeatDue;
    }
}
