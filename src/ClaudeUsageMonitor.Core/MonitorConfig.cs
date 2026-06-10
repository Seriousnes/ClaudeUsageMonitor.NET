using System.Text.Json.Serialization;

namespace ClaudeUsageMonitor.Core;

public class MonitorConfig
{
    public int PollIntervalSeconds { get; set; } = 60;            // cadence while a Claude session is active
    public int ActiveWindowSeconds { get; set; } = 300;          // JSONL quiet time before a session counts as idle
    public int IdlePollIntervalSeconds { get; set; } = 3600;      // slow heartbeat when no JSONL activity
    public int[] AlertThresholds { get; set; } = [80, 95];
    public string[] WeeklyModels { get; set; } = [];
    public double WidgetOpacity { get; set; } = 0.92;
    public WidgetPosition WidgetPosition { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public bool ClickThrough { get; set; } = false;
    public PaceSettings Pace { get; set; } = new();
    public UsageBandSettings UsageBand { get; set; } = new();

    /// <summary>Next time polling is allowed after a 429 back-off. Persisted so the back-off survives a
    /// restart; written only while rate-limited and removed once polling recovers.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RateLimitedUntil { get; set; }
}

public class WidgetPosition
{
    public double? X { get; set; }
    public double? Y { get; set; }
}

/// <summary>
/// Tunable thresholds for the pace-based icon colour and burn line. Holds the single source of
/// truth for every default; <see cref="SessionPace"/> and <see cref="StatusCalculator"/> read from
/// here and hardcode no threshold values. Projected = Session usage% / fraction-of-window-elapsed.
/// </summary>
public class PaceSettings
{
    public double YellowProjected { get; set; } = 95;
    public double OrangeProjected { get; set; } = 115;
    public double RedProjected { get; set; } = 140;
    public double EarlyFloorStartPercent { get; set; } = 20;
    public double EarlyFloorBasePercent { get; set; } = 5;
    public double EarlyGracePercent { get; set; } = 15;
    public double HighUsageOrange { get; set; } = 85;
    public double HighUsageRed { get; set; } = 95;
}

/// <summary>
/// Fill-% thresholds for the discrete four-band colour of the Session and Week usage bars. This is
/// each bar's own utilization band — distinct from the pace-based icon colour in <see cref="PaceSettings"/>.
/// </summary>
public class UsageBandSettings
{
    public double YellowPercent { get; set; } = 40;
    public double OrangePercent { get; set; } = 65;
    public double RedPercent { get; set; } = 85;
}
