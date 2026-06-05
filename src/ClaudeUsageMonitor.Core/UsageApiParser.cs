using System.Text.Json;

namespace ClaudeUsageMonitor.Core;

public static class UsageApiParser
{
    private const string WeeklyModelPrefix = "seven_day_";

    public static UsageSnapshot Parse(string json, string planLabel, DateTimeOffset fetchedAt)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var fiveHour = ParseWindow(root, "five_hour")
            ?? throw new FormatException("usage response missing valid 'five_hour'");
        var sevenDay = ParseWindow(root, "seven_day")
            ?? throw new FormatException("usage response missing valid 'seven_day'");

        var weeklyByModel = new Dictionary<string, UsageWindow>();
        foreach (var prop in root.EnumerateObject())
        {
            // "seven_day_opus" matches; "seven_day" does not (no trailing underscore).
            if (!prop.Name.StartsWith(WeeklyModelPrefix, StringComparison.Ordinal))
                continue;
            var window = ParseWindowElement(prop.Value);
            if (window is not null)
                weeklyByModel[prop.Name[WeeklyModelPrefix.Length..]] = window;
        }

        return new UsageSnapshot(fiveHour, sevenDay, weeklyByModel, fetchedAt, planLabel);
    }

    private static UsageWindow? ParseWindow(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) ? ParseWindowElement(el) : null;

    private static UsageWindow? ParseWindowElement(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) return null;
        if (!el.TryGetProperty("resets_at", out var r) || r.ValueKind != JsonValueKind.String) return null;
        return new UsageWindow(u.GetDouble(), DateTimeOffset.Parse(r.GetString()!));
    }
}
