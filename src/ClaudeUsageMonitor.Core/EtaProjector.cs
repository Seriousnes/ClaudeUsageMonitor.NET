namespace ClaudeUsageMonitor.Core;

public class EtaProjector(TimeSpan? historyWindow = null)
{
    private readonly TimeSpan _historyWindow = historyWindow ?? TimeSpan.FromMinutes(30);
    private readonly Dictionary<string, List<Sample>> _history = [];

    private readonly record struct Sample(DateTimeOffset At, double Util, string Label);

    /// <summary>Record this poll's utilization for every tracked window, keyed on Key. Prunes windows no longer tracked and samples older than the history window.</summary>
    public void AddSnapshot(IReadOnlyList<TrackedWindow> tracked, DateTimeOffset now)
    {
        var keys = tracked.Select(t => t.Key).ToHashSet();
        foreach (var gone in _history.Keys.Where(k => !keys.Contains(k)).ToList())
            _history.Remove(gone);

        foreach (var t in tracked)
        {
            if (!_history.TryGetValue(t.Key, out var list))
                _history[t.Key] = list = [];
            list.Add(new Sample(now, t.Window.Utilization, t.Label));
            list.RemoveAll(s => now - s.At > _historyWindow);
        }
    }

    /// <summary>The soonest projected time-to-100% across tracked windows, or null if none is rising.</summary>
    public (TimeSpan Eta, string Label)? ProjectSoonest()
    {
        (TimeSpan Eta, string Label)? best = null;
        foreach (var samples in _history.Values)
        {
            if (samples.Count < 2) continue;
            var first = samples[0];
            var last = samples[^1];
            var hours = (last.At - first.At).TotalHours;
            if (hours <= 0) continue;
            var slope = (last.Util - first.Util) / hours;      // %/hr
            if (slope <= 0) continue;
            var remaining = 100.0 - last.Util;
            if (remaining <= 0) continue;
            var eta = TimeSpan.FromHours(remaining / slope);
            if (best is null || eta < best.Value.Eta)
                best = (eta, last.Label);
        }
        return best;
    }
}
