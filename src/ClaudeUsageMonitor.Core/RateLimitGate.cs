namespace ClaudeUsageMonitor.Core;

/// <summary>
/// Holds the next-allowed poll time during a rate-limit back-off. The poller reads it to skip polling
/// while a 429's Retry-After window is open, and writes it when one arrives. Implementations may persist
/// the value (e.g. to config.json) so the back-off survives a restart.
/// </summary>
public interface IRateLimitGate
{
    DateTimeOffset? RetryAt { get; set; }
}

/// <summary>Non-persistent back-off store; honors the deadline within the current process only.</summary>
public sealed class InMemoryRateLimitGate : IRateLimitGate
{
    public DateTimeOffset? RetryAt { get; set; }
}
