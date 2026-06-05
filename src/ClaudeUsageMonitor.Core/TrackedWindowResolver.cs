namespace ClaudeUsageMonitor.Core;

public static class TrackedWindowResolver
{
    private static readonly string[] TierOrder = ["opus", "sonnet", "haiku"];

    public static IReadOnlyList<TrackedWindow> Resolve(UsageSnapshot snapshot, IReadOnlyList<string> weeklyModels)
    {
        var result = new List<TrackedWindow> { new("Session", "five_hour", snapshot.FiveHour) };
        result.AddRange(ResolveWeekly(snapshot, weeklyModels));
        return result;
    }

    private static List<TrackedWindow> ResolveWeekly(UsageSnapshot s, IReadOnlyList<string> weeklyModels)
    {
        var aggregate = new TrackedWindow("Week", "seven_day", s.SevenDay);

        if (weeklyModels.Count == 0)
            return [aggregate];

        if (weeklyModels.Count == 1 && weeklyModels[0] == "highest")
        {
            foreach (var tier in TierOrder)
                if (s.WeeklyByModel.TryGetValue(tier, out var w))
                    return [new TrackedWindow("Week", "seven_day_" + tier, w)];
            return [aggregate];
        }

        var collected = new List<TrackedWindow>();
        if (weeklyModels.Count == 1 && weeklyModels[0] == "*")
        {
            collected.Add(aggregate);
            foreach (var kv in s.WeeklyByModel)
                collected.Add(new TrackedWindow(Title(kv.Key), "seven_day_" + kv.Key, kv.Value));
        }
        else
        {
            foreach (var key in weeklyModels)
            {
                if (key is "highest" or "*") continue;
                if (s.WeeklyByModel.TryGetValue(key, out var w))
                    collected.Add(new TrackedWindow(Title(key), "seven_day_" + key, w));
            }
        }

        if (collected.Count == 0)
            return [aggregate];                          // never blank
        if (collected.Count == 1)
            return [collected[0] with { Label = "Week" }];
        return collected;
    }

    private static string Title(string family)
        => family.Length == 0 ? family : char.ToUpperInvariant(family[0]) + family[1..];
}
