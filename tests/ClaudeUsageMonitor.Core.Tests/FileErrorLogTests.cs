using ClaudeUsageMonitor.Core;
using Microsoft.Extensions.Time.Testing;

namespace ClaudeUsageMonitor.Core.Tests;

public class FileErrorLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 6, 11, 14, 0, 0, TimeSpan.Zero));

    private FileErrorLog NewLog(TimeSpan? heartbeat = null)
        => new(_path, _time, heartbeat ?? TimeSpan.FromMinutes(10));

    private string[] Lines() => File.Exists(_path) ? File.ReadAllLines(_path) : [];

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Failure_writes_timestamped_context_and_message()
    {
        NewLog().RecordFailure("api", "HttpRequestException: 401");

        var lines = Lines();
        Assert.Single(lines);
        Assert.StartsWith("2026-06-11 14:00:00", lines[0]);
        Assert.Contains("[api] HttpRequestException: 401", lines[0]);
    }

    [Fact]
    public void Identical_consecutive_failures_collapse_to_one_line()
    {
        var log = NewLog();
        log.RecordFailure("api", "401");
        _time.Advance(TimeSpan.FromMinutes(1));
        log.RecordFailure("api", "401");
        _time.Advance(TimeSpan.FromMinutes(1));
        log.RecordFailure("api", "401");

        Assert.Single(Lines());
    }

    [Fact]
    public void Persisting_failure_emits_heartbeat_after_interval()
    {
        var log = NewLog(TimeSpan.FromMinutes(10));
        log.RecordFailure("api", "401");
        _time.Advance(TimeSpan.FromMinutes(10));
        log.RecordFailure("api", "401");

        var lines = Lines();
        Assert.Equal(2, lines.Length);
        Assert.Contains("still failing (10m)", lines[1]);
    }

    [Fact]
    public void Different_failure_writes_a_new_line_immediately()
    {
        var log = NewLog();
        log.RecordFailure("api", "401");
        log.RecordFailure("auth", "credentials missing or token expired");

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void Success_after_failure_writes_recovered_line()
    {
        var log = NewLog();
        log.RecordFailure("api", "401");
        _time.Advance(TimeSpan.FromMinutes(3));
        log.RecordSuccess();

        var lines = Lines();
        Assert.Equal(2, lines.Length);
        Assert.Contains("[ok] recovered after 3m", lines[1]);
    }

    [Fact]
    public void Success_with_no_open_failure_writes_nothing()
    {
        NewLog().RecordSuccess();

        Assert.Empty(Lines());
    }

    [Fact]
    public void New_failure_after_recovery_logs_again()
    {
        var log = NewLog();
        log.RecordFailure("api", "401");
        log.RecordSuccess();
        log.RecordFailure("api", "401");   // same signature, but the streak was closed -> logs fresh

        Assert.Equal(3, Lines().Length);
    }
}
