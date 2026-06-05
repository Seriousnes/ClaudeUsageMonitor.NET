using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public sealed class MonitorController : IDisposable
{
    private readonly MonitorConfig _config;
    private readonly TimeProvider _time;
    private readonly string _projectsRoot;

    private readonly UsagePoller _poller;
    private readonly EtaProjector _eta = new();
    private readonly AlertEngine _alerts;
    private readonly JsonlWatcher? _watcher;
    private readonly HttpClient _http = new();

    private readonly Lock _gate = new();   // serializes OnPolled state across overlapping poll triggers
    private UsageSnapshot? _lastSnapshot;
    private DateTimeOffset _lastGood;

    public event Action<MonitorView>? Updated;
    public event Action<string, string>? Alert;

    public MonitorController(string credentialsPath, string projectsRoot, MonitorConfig config, TimeProvider time)
    {
        _config = config;
        _time = time;
        _projectsRoot = projectsRoot;

        var auth = new AuthProvider(credentialsPath, new StubTokenRefresher(), time);
        var client = new HttpUsageApiClient(_http);
        _poller = new UsagePoller(auth, client, time, () => TimeSpan.FromSeconds(_config.PollIntervalSeconds));
        _alerts = new AlertEngine(_config.AlertThresholds);
        _poller.Polled += OnPolled;

        if (Directory.Exists(projectsRoot))
            _watcher = new JsonlWatcher(projectsRoot, time, TimeSpan.FromSeconds(2), () => _ = _poller.TriggerAsync());
    }

    public void Start() => _poller.Start();

    public void RefreshNow() => _ = _poller.TriggerAsync();

    private void OnPolled(PollResult result)
    {
        var now = _time.GetUtcNow();
        MonitorView view;
        var alerts = new List<(string Title, string Text)>();

        // Periodic timer, file-watcher debounce, and manual refresh can all reach here on different
        // threads. Mutate/read shared state (snapshot, ETA history, alert arming) under the gate, but
        // raise events OUTSIDE it — Updated/Alert do a blocking Dispatcher.Invoke and a UI-thread
        // RefreshNow holding nothing must never deadlock against this lock.
        lock (_gate)
        {
            switch (result)
            {
                case PollResult.Ok ok:
                    _lastSnapshot = ok.Snapshot;
                    _lastGood = now;
                    var tracked = TrackedWindowResolver.Resolve(ok.Snapshot, _config.WeeklyModels);
                    _eta.AddSnapshot(tracked, now);
                    foreach (var alert in _alerts.Evaluate(tracked, now))
                        alerts.Add(($"Claude Usage — {alert.WindowLabel} at {alert.Threshold}%",
                                    $"{alert.WindowLabel} usage has reached {alert.Threshold}%."));
                    view = BuildView(tracked, ok.Snapshot.PlanLabel, now, BuildBurn(now), Freshness.Live);
                    break;

                default:   // PollResult.Stale — a failed poll no longer greys immediately (spec §5)
                    view = BuildDegradedView(now);
                    break;
            }
        }

        foreach (var (title, text) in alerts)
            Alert?.Invoke(title, text);
        Updated?.Invoke(view);
    }

    private BurnEstimate? BuildBurn(DateTimeOffset now)
    {
        var tokensPerHour = JsonlActivityScanner.TokensPerHour(_projectsRoot, now, TimeSpan.FromHours(1));
        var eta = _eta.ProjectSoonest();
        if (eta is null && tokensPerHour <= 0) return null;     // idle and no projection -> hide burn line
        return new BurnEstimate(tokensPerHour, eta?.Eta, eta?.Label);
    }

    private MonitorView BuildView(IReadOnlyList<TrackedWindow> tracked, string plan, DateTimeOffset now,
        BurnEstimate? burn, Freshness freshness)
    {
        var grey = freshness == Freshness.Stale;
        var rows = tracked
            .Select(t => new WindowRow(t.Label, t.Window.Utilization, ResetFormatter.Format(t.Window.ResetsAt, now)))
            .ToList();
        var session = tracked.FirstOrDefault(t => t.Key == "five_hour");
        var pace = session is null ? null : SessionPace.Evaluate(session.Window, now, _config.Pace);
        var status = StatusCalculator.Compute(pace, tracked, _config.Pace, grey);
        return new MonitorView(status, plan, rows, burn, freshness, AgeText(freshness, now), pace);
    }

    private MonitorView BuildDegradedView(DateTimeOffset now)
    {
        if (_lastSnapshot is null)
            return new MonitorView(Status.Stale, "Claude", [], null, Freshness.Stale, "no data", null);

        // _lastGood is set together with _lastSnapshot, so it is valid here.
        var freshness = FreshnessEvaluator.Evaluate(false, _lastGood, now, FreshnessEvaluator.GracePeriod);
        var tracked = TrackedWindowResolver.Resolve(_lastSnapshot, _config.WeeklyModels);
        // Spec §7: keep the JSONL activity readout when degraded (it reads disk, works offline). Emit the
        // tokens/hr rate only — not a projected ETA, which would be a misleading countdown off old samples.
        var tokensPerHour = JsonlActivityScanner.TokensPerHour(_projectsRoot, now, TimeSpan.FromHours(1));
        var burn = tokensPerHour > 0 ? new BurnEstimate(tokensPerHour, null, null) : null;
        return BuildView(tracked, _lastSnapshot.PlanLabel, now, burn, freshness);
    }

    private string AgeText(Freshness freshness, DateTimeOffset now)
    {
        if (freshness == Freshness.Live) return "";
        var mins = (int)(now - _lastGood).TotalMinutes;
        return freshness == Freshness.Recent ? $"updated {mins}m ago" : $"stale · {mins}m ago";
    }

    public void Dispose()
    {
        _poller.Dispose();
        _watcher?.Dispose();
        _http.Dispose();
    }
}
