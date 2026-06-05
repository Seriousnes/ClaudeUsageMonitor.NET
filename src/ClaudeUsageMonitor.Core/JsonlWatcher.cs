namespace ClaudeUsageMonitor.Core;

/// <summary>Watches ~/.claude/projects/**/*.jsonl for changes and fires a debounced activity callback.</summary>
public sealed class JsonlWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Debouncer _debouncer;

    public JsonlWatcher(string projectsRoot, TimeProvider time, TimeSpan debounce, Action onActivity)
    {
        _debouncer = new Debouncer(time, debounce, onActivity);
        _watcher = new FileSystemWatcher(projectsRoot, "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => _debouncer.Trigger();
        _watcher.Created += (_, _) => _debouncer.Trigger();
    }

    public void Dispose() { _watcher.Dispose(); _debouncer.Dispose(); }
}
