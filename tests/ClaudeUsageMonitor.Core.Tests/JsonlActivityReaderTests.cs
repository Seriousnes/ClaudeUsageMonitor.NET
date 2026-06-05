using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class JsonlActivityReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_usage_entries_and_skips_non_usage_lines()
    {
        var lines = new[]
        {
            """{"type":"user","timestamp":"2026-06-04T09:30:00+00:00","message":{"role":"user"}}""",
            """{"type":"assistant","timestamp":"2026-06-04T09:45:00+00:00","message":{"usage":{"input_tokens":100,"output_tokens":50,"cache_creation_input_tokens":10,"cache_read_input_tokens":40}}}""",
            "",
            "not json at all",
        };
        var entries = JsonlActivityReader.ParseLines(lines);
        Assert.Single(entries);
        Assert.Equal(150, entries[0].Tokens);    // 100+50 — cache creation/read deliberately excluded
    }

    [Fact]
    public void Tokens_exclude_cache_read_and_creation()
    {
        // A cache-heavy turn: tiny real I/O, enormous cache numbers. Summing the cache fields is what
        // inflated the burn rate to ~10m tok/hr mid-session; only prompt+completion should count.
        var line = """{"timestamp":"2026-06-04T09:45:00+00:00","message":{"usage":{"input_tokens":100,"output_tokens":50,"cache_creation_input_tokens":900000,"cache_read_input_tokens":10000000}}}""";
        var entries = JsonlActivityReader.ParseLines([line]);
        Assert.Equal(150, entries[0].Tokens);
    }

    [Fact]
    public void TokensPerHour_sums_only_within_trailing_window()
    {
        var entries = new List<TokenEntry>
        {
            new(Now.AddMinutes(-30), 600),   // inside 1h window
            new(Now.AddMinutes(-90), 9999),  // outside
        };
        var rate = JsonlActivityReader.TokensPerHour(entries, Now, TimeSpan.FromHours(1));
        Assert.Equal(600, rate);             // 600 tokens / 1 hour
    }

    [Fact]
    public void TokensPerHour_empty_is_zero()
        => Assert.Equal(0, JsonlActivityReader.TokensPerHour([], Now, TimeSpan.FromHours(1)));
}
