namespace ClaudeUsageMonitor.Core;

public abstract record PollResult
{
    public sealed record Ok(UsageSnapshot Snapshot) : PollResult;

    /// <summary>A poll that produced no fresh data. Context is the failing stage ("auth"/"api"/"parse");
    /// Message is a human-readable reason suitable for the log and the widget's error line.</summary>
    public sealed record Stale(string Context, string Message) : PollResult;
}

public class UsagePoller : IDisposable
{
    private readonly AuthProvider _auth;
    private readonly IUsageApiClient _client;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan> _interval;
    private readonly IRateLimitGate _rateLimit;
    private readonly Func<bool> _shouldPoll;   // gates timer-driven polls; manual TriggerAsync bypasses it
    private ITimer? _timer;

    public event Action<PollResult>? Polled;

    public UsagePoller(AuthProvider auth, IUsageApiClient client, TimeProvider time, Func<TimeSpan> interval,
        IRateLimitGate rateLimit, Func<bool> shouldPoll)
        => (_auth, _client, _time, _interval, _rateLimit, _shouldPoll)
            = (auth, client, time, interval, rateLimit, shouldPoll);

    public async Task<PollResult> PollOnceAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        if (_rateLimit.RetryAt is { } until && now < until)   // honor an open 429 back-off; don't hit the API
            return new PollResult.Stale("api", RateLimitMessage(until));

        var auth = await _auth.GetTokenAsync(ct);
        if (!auth.IsValid) return new PollResult.Stale("auth", "credentials missing or token expired");

        string json;
        try { json = await _client.GetUsageJsonAsync(auth.AccessToken!, ct); }
        catch (UsageApiException ex) when (ex.RetryAfter is { } delta)
        {
            var retryAt = now + delta;
            _rateLimit.RetryAt = retryAt;   // persisted; honored on later polls and across restarts
            return new PollResult.Stale("api", RateLimitMessage(retryAt));
        }
        catch (UsageApiException ex) { return new PollResult.Stale("api", ex.Message); }
        catch (Exception ex) { return new PollResult.Stale("api", $"{ex.GetType().Name}: {ex.Message}"); }

        try
        {
            var ok = new PollResult.Ok(UsageApiParser.Parse(json, auth.PlanLabel, _time.GetUtcNow()));
            if (_rateLimit.RetryAt is not null) _rateLimit.RetryAt = null;   // recovered — lift the back-off
            return ok;
        }
        catch (Exception ex) { return new PollResult.Stale("parse", $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static string RateLimitMessage(DateTimeOffset until)
        => $"next refresh @ {until.ToLocalTime():HH:mm:ss}";

    public void Start()
        => _timer = _time.CreateTimer(_ => _ = TickAsync(), null, TimeSpan.Zero, _interval());

    // The timer ticks at the active interval; skip the API call when the schedule says the session is idle
    // and the heartbeat isn't due yet. A manual TriggerAsync bypasses this — a user refresh always polls.
    private async Task TickAsync()
    {
        if (_shouldPoll()) await PollAndPublishAsync();
    }

    public Task TriggerAsync() => PollAndPublishAsync();

    private async Task PollAndPublishAsync()
    {
        var result = await PollOnceAsync();   // never throws; returns Stale on failure
        Polled?.Invoke(result);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _timer?.Dispose();
    }
}
