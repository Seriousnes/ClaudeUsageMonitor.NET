using ClaudeUsageMonitor.Core;
using Microsoft.Extensions.Time.Testing;

namespace ClaudeUsageMonitor.Core.Tests;

public class AuthProviderTests
{
    private sealed class FakeRefresher(string? token) : ITokenRefresher
    {
        public Task<string?> RefreshAsync(string refreshToken, CancellationToken ct) => Task.FromResult(token);
    }

    private static string WriteCreds(long expiresAtMs)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, $$"""
        { "claudeAiOauth": { "accessToken": "live-token", "refreshToken": "rt",
            "expiresAt": {{expiresAtMs}}, "subscriptionType": "max", "rateLimitTier": "default_claude_max_5x" } }
        """);
        return path;
    }

    [Fact]
    public async Task Valid_token_is_returned()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var future = time.GetUtcNow().AddHours(2).ToUnixTimeMilliseconds();
        var auth = new AuthProvider(WriteCreds(future), new StubTokenRefresher(), time);
        var result = await auth.GetTokenAsync();
        Assert.True(result.IsValid);
        Assert.Equal("live-token", result.AccessToken);
        Assert.Equal("Max 5x", result.PlanLabel);
    }

    [Fact]
    public async Task Expired_token_with_successful_refresh_returns_new_token()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var past = time.GetUtcNow().AddHours(-1).ToUnixTimeMilliseconds();
        var auth = new AuthProvider(WriteCreds(past), new FakeRefresher("refreshed-token"), time);
        var result = await auth.GetTokenAsync();
        Assert.True(result.IsValid);
        Assert.Equal("refreshed-token", result.AccessToken);
    }

    [Fact]
    public async Task Expired_token_with_failed_refresh_is_stale()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var past = time.GetUtcNow().AddHours(-1).ToUnixTimeMilliseconds();
        var auth = new AuthProvider(WriteCreds(past), new StubTokenRefresher(), time);
        var result = await auth.GetTokenAsync();
        Assert.False(result.IsValid);
        Assert.Null(result.AccessToken);
        Assert.Equal("Max 5x", result.PlanLabel);   // label still known from credentials
    }

    [Fact]
    public async Task Missing_credentials_file_is_stale()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var auth = new AuthProvider(Path.Combine(Path.GetTempPath(), "does-not-exist.json"),
            new StubTokenRefresher(), time);
        var result = await auth.GetTokenAsync();
        Assert.False(result.IsValid);
    }
}
