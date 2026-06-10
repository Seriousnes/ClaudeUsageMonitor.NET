using System.Diagnostics;

namespace ClaudeUsageMonitor.Core;

public interface IErrorLog
{
    /// <summary>Record a failed poll/API call. Consecutive identical failures collapse to a single line;
    /// a "still failing" heartbeat is written once the same failure outlives the heartbeat interval.</summary>
    void RecordFailure(string context, string message);

    /// <summary>Record a successful poll. Writes one "recovered" line if a failure streak was open, then resets.</summary>
    void RecordSuccess();
}

/// <summary>
/// Appends timestamped failure lines to a single log file. Stateful: it remembers the current failure so
/// repeats collapse instead of flooding, re-logs a heartbeat while a failure persists, and bookends an
/// outage with a recovery line. All file I/O is best-effort — a logging failure must never crash the app.
/// </summary>
public sealed class FileErrorLog : IErrorLog
{
    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly TimeSpan _heartbeat;
    private readonly Lock _gate = new();

    private string? _signature;          // context|message of the open failure streak, null when healthy
    private DateTimeOffset _failingSince;
    private DateTimeOffset _lastWrite;

    public FileErrorLog(string path, TimeProvider time, TimeSpan heartbeat)
        => (_path, _time, _heartbeat) = (path, time, heartbeat);

    public void RecordFailure(string context, string message)
    {
        var signature = $"{context}|{message}";
        var now = _time.GetLocalNow();
        lock (_gate)
        {
            if (signature != _signature)
            {
                _signature = signature;
                _failingSince = now;
                _lastWrite = now;
                Write($"[{context}] {message}", now);
            }
            else if (now - _lastWrite >= _heartbeat)
            {
                _lastWrite = now;
                Write($"[{context}] still failing ({Elapsed(now - _failingSince)}): {message}", now);
            }
        }
    }

    public void RecordSuccess()
    {
        var now = _time.GetLocalNow();
        lock (_gate)
        {
            if (_signature is null) return;
            Write($"[ok] recovered after {Elapsed(now - _failingSince)}", now);
            _signature = null;
        }
    }

    private void Write(string body, DateTimeOffset now)
    {
        try
        {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            File.AppendAllText(_path, $"{now:yyyy-MM-dd HH:mm:ss}  {body}{Environment.NewLine}");
        }
        catch (Exception ex) { Debug.WriteLine($"FileErrorLog write failed: {ex.Message}"); }
    }

    private static string Elapsed(TimeSpan span)
        => span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m" : $"{(int)span.TotalSeconds}s";
}
