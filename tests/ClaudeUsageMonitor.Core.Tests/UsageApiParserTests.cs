using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class UsageApiParserTests
{
    private const string Sample = """
    {
      "five_hour": { "utilization": 13.0, "resets_at": "2026-06-04T20:30:00.737894+10:00" },
      "seven_day": { "utilization": 20.0, "resets_at": "2026-06-10T18:00:00.73791+10:00" },
      "seven_day_opus": null,
      "seven_day_sonnet": { "utilization": 2.0, "resets_at": "2026-06-10T18:00:00.73791+10:00" },
      "extra_usage": { "is_enabled": false, "monthly_limit": null, "used_credits": null,
                       "utilization": null, "currency": null, "disabled_reason": null }
    }
    """;

    private static readonly DateTimeOffset Now = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_aggregate_windows()
    {
        var s = UsageApiParser.Parse(Sample, "Max 5x", Now);
        Assert.Equal(13.0, s.FiveHour.Utilization);
        Assert.Equal(20.0, s.SevenDay.Utilization);
        Assert.Equal("Max 5x", s.PlanLabel);
        Assert.Equal(Now, s.FetchedAt);
    }

    [Fact]
    public void Parses_resets_at_with_offset()
    {
        var s = UsageApiParser.Parse(Sample, "Max 5x", Now);
        Assert.Equal(new DateTimeOffset(2026, 6, 4, 20, 30, 0, 737, TimeSpan.FromHours(10)).ToUnixTimeSeconds(),
                     s.FiveHour.ResetsAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void Includes_only_non_null_model_windows()
    {
        var s = UsageApiParser.Parse(Sample, "Max 5x", Now);
        Assert.True(s.WeeklyByModel.ContainsKey("sonnet"));
        Assert.False(s.WeeklyByModel.ContainsKey("opus"));   // was null in response
        Assert.Equal(2.0, s.WeeklyByModel["sonnet"].Utilization);
    }

    [Fact]
    public void Tolerates_unknown_keys()
    {
        const string withUnknown = """
        { "five_hour": { "utilization": 1, "resets_at": "2026-06-04T20:30:00+10:00" },
          "seven_day": { "utilization": 1, "resets_at": "2026-06-10T18:00:00+10:00" },
          "brand_new_key": { "whatever": true } }
        """;
        var s = UsageApiParser.Parse(withUnknown, "Pro", Now);
        Assert.Empty(s.WeeklyByModel);
        Assert.Equal(1.0, s.FiveHour.Utilization);
    }

    [Fact]
    public void Throws_when_required_window_missing()
        => Assert.Throws<FormatException>(() => UsageApiParser.Parse("""{ "seven_day": {} }""", "Pro", Now));
}
