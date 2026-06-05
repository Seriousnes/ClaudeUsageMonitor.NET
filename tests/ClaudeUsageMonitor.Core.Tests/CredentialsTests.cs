using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class CredentialsTests
{
    [Fact]
    public void Reads_credentials_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, """
        { "claudeAiOauth": {
            "accessToken": "at-123", "refreshToken": "rt-456", "expiresAt": 1780000000000,
            "scopes": ["a"], "subscriptionType": "max", "rateLimitTier": "default_claude_max_5x" } }
        """);
        var c = CredentialsReader.Read(path);
        Assert.Equal("at-123", c.AccessToken);
        Assert.Equal("rt-456", c.RefreshToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1780000000000), c.ExpiresAt);
        Assert.Equal("max", c.SubscriptionType);
        Assert.Equal("default_claude_max_5x", c.RateLimitTier);
        File.Delete(path);
    }

    [Theory]
    [InlineData("default_claude_max_5x", "max", "Max 5x")]
    [InlineData("default_claude_max_20x", "max", "Max 20x")]
    [InlineData("default_claude_pro", "pro", "Pro")]
    [InlineData("some_future_tier", "max", "Max")]      // unknown tier -> capitalized subscriptionType
    [InlineData("", "", "Claude")]                      // nothing known -> generic
    public void Maps_plan_label(string tier, string sub, string expected)
        => Assert.Equal(expected, PlanLabelMapper.Map(tier, sub));
}
