namespace ClaudeUsageMonitor.Core;

public abstract record PollResult
{
    public sealed record Ok(UsageSnapshot Snapshot) : PollResult;
    public sealed record Stale(string Reason) : PollResult;
}

public class UsagePoller : IDisposable
{
    private readonly AuthProvider _auth;
    private readonly IUsageApiClient _client;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan> _interval;
    private ITimer? _timer;

    public event Action<PollResult>? Polled;

    public UsagePoller(AuthProvider auth, IUsageApiClient client, TimeProvider time, Func<TimeSpan> interval)
        => (_auth, _client, _time, _interval) = (auth, client, time, interval);

    public async Task<PollResult> PollOnceAsync(CancellationToken ct = default)
    {
        var auth = await _auth.GetTokenAsync(ct);
        if (!auth.IsValid) return new PollResult.Stale("auth");

        string json;
        try { json = await _client.GetUsageJsonAsync(auth.AccessToken!, ct); }
        catch (Exception ex) { return new PollResult.Stale(ex.GetType().Name); }

        try { return new PollResult.Ok(UsageApiParser.Parse(json, auth.PlanLabel, _time.GetUtcNow())); }
        catch (Exception ex) { return new PollResult.Stale(ex.GetType().Name); }
    }

    public void Start()
        => _timer = _time.CreateTimer(_ => _ = PollAndPublishAsync(), null, TimeSpan.Zero, _interval());

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
