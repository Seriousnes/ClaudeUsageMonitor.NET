namespace ClaudeUsageMonitor.Core;

/// <summary>Coalesces bursts of Trigger() calls into a single action fired after a quiet period.</summary>
public sealed class Debouncer : IDisposable
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _delay;
    private readonly Action _action;
    private readonly Lock _lock = new();
    private ITimer? _timer;

    public Debouncer(TimeProvider time, TimeSpan delay, Action action)
        => (_time, _delay, _action) = (time, delay, action);

    public void Trigger()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = _time.CreateTimer(_ => _action(), null, _delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose() { lock (_lock) _timer?.Dispose(); }
}
