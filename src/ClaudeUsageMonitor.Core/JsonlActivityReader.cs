using System.Text.Json;

namespace ClaudeUsageMonitor.Core;

/// <summary>One usage record: when it happened and its prompt+completion (input+output) token count.
/// Cache read/creation tokens are excluded so the derived burn rate reflects real consumption — not the
/// re-read context, which dwarfs everything during active use.</summary>
public record TokenEntry(DateTimeOffset Timestamp, long Tokens);

public static class JsonlActivityReader
{
    public static IReadOnlyList<TokenEntry> ParseLines(IEnumerable<string> lines)
    {
        var entries = new List<TokenEntry>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = TryParseLine(line);
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }

    private static TokenEntry? TryParseLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.String) return null;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return null;
            if (!msg.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;

            // Count prompt+completion only. cache_read re-counts the whole cached context on every turn
            // and cache_creation writes it once; summing either inflated the rate to ~10m tok/hr mid-session.
            long tokens = GetLong(usage, "input_tokens") + GetLong(usage, "output_tokens");
            return new TokenEntry(DateTimeOffset.Parse(ts.GetString()!), tokens);
        }
        catch (JsonException) { return null; }
    }

    private static long GetLong(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    public static double TokensPerHour(IReadOnlyList<TokenEntry> entries, DateTimeOffset now, TimeSpan window)
    {
        var cutoff = now - window;
        long sum = 0;
        foreach (var e in entries)
            if (e.Timestamp >= cutoff && e.Timestamp <= now) sum += e.Tokens;
        var hours = window.TotalHours;
        return hours <= 0 ? 0 : sum / hours;
    }
}

/// <summary>IO wrapper: scans recently-modified JSONL transcripts and computes a tokens/hour rate. Thin; verified via the running app.</summary>
public static class JsonlActivityScanner
{
    public static double TokensPerHour(string projectsRoot, DateTimeOffset now, TimeSpan window)
    {
        if (!Directory.Exists(projectsRoot)) return 0;
        var cutoff = now - window;
        var lines = new List<string>();
        foreach (var file in Directory.EnumerateFiles(projectsRoot, "*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                if (new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) < cutoff) continue;
                lines.AddRange(File.ReadLines(file));
            }
            catch (IOException) { /* file in use by Claude Code; skip this poll */ }
        }
        return JsonlActivityReader.TokensPerHour(JsonlActivityReader.ParseLines(lines), now, window);
    }
}
