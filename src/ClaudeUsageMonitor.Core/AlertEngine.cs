namespace ClaudeUsageMonitor.Core;

public record UsageAlert(string WindowLabel, string WindowKey, int Threshold);

public class AlertEngine
{
    private readonly int[] _thresholds;     // ascending
    private readonly double _lowest;
    private readonly Dictionary<string, WindowState> _state = [];

    private sealed class WindowState
    {
        public DateTimeOffset LastResetsAt;
        public readonly HashSet<int> Fired = [];
    }

    public AlertEngine(IEnumerable<int> thresholds)
    {
        _thresholds = [.. thresholds.OrderBy(t => t)];
        _lowest = _thresholds.Length > 0 ? _thresholds[0] : double.MaxValue;
    }

    public IReadOnlyList<UsageAlert> Evaluate(IReadOnlyList<TrackedWindow> tracked, DateTimeOffset now)
    {
        var alerts = new List<UsageAlert>();
        foreach (var t in tracked)
        {
            if (!_state.TryGetValue(t.Key, out var st))
            {
                _state[t.Key] = st = new WindowState { LastResetsAt = t.Window.ResetsAt };
            }
            // Re-arm only on a genuine window roll. The API stamps resets_at from a request-time
            // clock, so it drifts forward fractions of a second each poll; treating any advance as a
            // reset re-fires every poll. A real roll requires the stored boundary to have *passed*
            // (now) AND the API to report a later one. A utilization drop below the lowest threshold
            // is the independent signal that the window restarted.
            else if ((now >= st.LastResetsAt && t.Window.ResetsAt > st.LastResetsAt)
                     || t.Window.Utilization < _lowest)
            {
                st.Fired.Clear();
                st.LastResetsAt = t.Window.ResetsAt;
            }

            foreach (var threshold in _thresholds)
                if (t.Window.Utilization >= threshold && st.Fired.Add(threshold))
                    alerts.Add(new UsageAlert(t.Label, t.Key, threshold));
        }
        return alerts;
    }
}
