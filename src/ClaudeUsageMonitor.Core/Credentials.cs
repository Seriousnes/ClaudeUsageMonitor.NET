using System.Text.Json;

namespace ClaudeUsageMonitor.Core;

public record Credentials(
    string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt,
    string SubscriptionType, string RateLimitTier);

public static class CredentialsReader
{
    /// <summary>Reads ~/.claude/.credentials.json. Read-only — never writes, to avoid racing Claude Code's daemon.</summary>
    public static Credentials Read(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var o = doc.RootElement.GetProperty("claudeAiOauth");
        return new Credentials(
            Str(o, "accessToken"),
            Str(o, "refreshToken"),
            DateTimeOffset.FromUnixTimeMilliseconds(o.GetProperty("expiresAt").GetInt64()),
            Str(o, "subscriptionType"),
            Str(o, "rateLimitTier"));
    }

    private static string Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
}

public static class PlanLabelMapper
{
    private static readonly Dictionary<string, string> Known = new()
    {
        ["default_claude_max_5x"] = "Max 5x",
        ["default_claude_max_20x"] = "Max 20x",
        ["default_claude_pro"] = "Pro",
    };

    public static string Map(string rateLimitTier, string subscriptionType)
    {
        if (Known.TryGetValue(rateLimitTier, out var label)) return label;
        if (string.IsNullOrEmpty(subscriptionType)) return "Claude";
        return char.ToUpperInvariant(subscriptionType[0]) + subscriptionType[1..];
    }
}
