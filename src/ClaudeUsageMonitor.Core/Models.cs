namespace ClaudeUsageMonitor.Core;

/// <summary>One usage window from the API: a 0–100 utilization % and when it resets.</summary>
public record UsageWindow(double Utilization, DateTimeOffset ResetsAt);

/// <summary>A full parsed usage response. WeeklyByModel is keyed by model family ("opus","sonnet",…), non-null windows only.</summary>
public record UsageSnapshot(
    UsageWindow FiveHour,
    UsageWindow SevenDay,
    IReadOnlyDictionary<string, UsageWindow> WeeklyByModel,
    DateTimeOffset FetchedAt,
    string PlanLabel);

/// <summary>
/// A window the user is actively tracking. Label is display text ("Session","Week","Opus");
/// Key is the stable API field name ("five_hour","seven_day","seven_day_opus"). Alert arming
/// MUST key on Key, never Label.
/// </summary>
public record TrackedWindow(string Label, string Key, UsageWindow Window);

/// <summary>Local JSONL activity readout: tokens consumed per hour over the last hour.</summary>
public record BurnEstimate(double TokensPerHour);

public enum Status { Green, Yellow, Orange, Red, Stale }

/// <summary>Freshness of the displayed reading, keyed to the age of the last successful poll.</summary>
public enum Freshness { Live, Recent, Stale }
