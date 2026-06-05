using ClaudeUsageMonitor.Core;
using Microsoft.Extensions.Time.Testing;

namespace ClaudeUsageMonitor.Core.Tests;

public class UsagePollerTests
{
    private const string Body = """
    { "five_hour": { "utilization": 13.0, "resets_at": "2026-06-04T20:30:00+10:00" },
      "seven_day": { "utilization": 20.0, "resets_at": "2026-06-10T18:00:00+10:00" } }
    """;

    private sealed class FakeClient(string? body, Exception? throwOnCall = null) : IUsageApiClient
    {
        public Task<string> GetUsageJsonAsync(string accessToken, CancellationToken ct = default)
            => throwOnCall is not null ? Task.FromException<string>(throwOnCall) : Task.FromResult(body!);
    }

    private static AuthProvider ValidAuth(FakeTimeProvider time)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var future = time.GetUtcNow().AddHours(2).ToUnixTimeMilliseconds();
        File.WriteAllText(path, $$"""
        { "claudeAiOauth": { "accessToken": "t", "refreshToken": "r", "expiresAt": {{future}},
            "subscriptionType": "max", "rateLimitTier": "default_claude_max_5x" } }
        """);
        return new AuthProvider(path, new StubTokenRefresher(), time);
    }

    [Fact]
    public async Task PollOnce_returns_Ok_snapshot()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var poller = new UsagePoller(ValidAuth(time), new FakeClient(Body), time, () => TimeSpan.FromSeconds(60));
        var result = await poller.PollOnceAsync();
        var ok = Assert.IsType<PollResult.Ok>(result);
        Assert.Equal(13.0, ok.Snapshot.FiveHour.Utilization);
        Assert.Equal("Max 5x", ok.Snapshot.PlanLabel);
    }

    [Fact]
    public async Task PollOnce_is_Stale_when_auth_invalid()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var badAuth = new AuthProvider("missing.json", new StubTokenRefresher(), time);
        var poller = new UsagePoller(badAuth, new FakeClient(Body), time, () => TimeSpan.FromSeconds(60));
        Assert.IsType<PollResult.Stale>(await poller.PollOnceAsync());
    }

    [Fact]
    public async Task PollOnce_is_Stale_when_client_throws()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var poller = new UsagePoller(ValidAuth(time), new FakeClient(null, new HttpRequestException("offline")),
            time, () => TimeSpan.FromSeconds(60));
        Assert.IsType<PollResult.Stale>(await poller.PollOnceAsync());
    }

    [Fact]
    public async Task Timer_fires_polls_on_interval()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var poller = new UsagePoller(ValidAuth(time), new FakeClient(Body), time, () => TimeSpan.FromSeconds(60));
        var count = 0;
        var gate = new TaskCompletionSource();
        poller.Polled += _ => { if (Interlocked.Increment(ref count) >= 2) gate.TrySetResult(); };

        poller.Start();                          // dueTime 0, period 60s
        time.Advance(TimeSpan.FromSeconds(60));  // at least one periodic fire
        time.Advance(TimeSpan.FromSeconds(60));  // another — count reaches >= 2 regardless of zero-due timing
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(count >= 2);
        poller.Dispose();
    }
}
