using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

/// <summary>Persists the rate-limit back-off deadline to config.json so it survives a restart.</summary>
internal sealed class ConfigRateLimitGate(MonitorConfig config, ConfigStore store) : IRateLimitGate
{
    public DateTimeOffset? RetryAt
    {
        get => config.RateLimitedUntil;
        set { config.RateLimitedUntil = value; store.Save(config); }
    }
}
