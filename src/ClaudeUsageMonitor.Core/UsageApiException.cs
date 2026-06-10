namespace ClaudeUsageMonitor.Core;

/// <summary>
/// A non-2xx response from the usage endpoint. <see cref="RetryAfter"/> is the Retry-After delta and is
/// populated only on HTTP 429 — other failures don't carry meaningful retry timing.
/// </summary>
public sealed class UsageApiException(int statusCode, string? reasonPhrase, TimeSpan? retryAfter)
    : Exception($"{statusCode} {reasonPhrase}".TrimEnd())
{
    public int StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
