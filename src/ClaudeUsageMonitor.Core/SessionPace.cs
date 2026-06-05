namespace ClaudeUsageMonitor.Core;

/// <summary>
/// Result of evaluating Session pace. <see cref="Projected"/> is end-of-session usage% if the
/// current average pace holds; <see cref="TimeToLimit"/>/<see cref="LimitAt"/> are populated only
/// when the user is on track to exceed 100% before the window resets.
/// </summary>
public record PaceResult(
    double TimeFraction,
    double UsagePercent,
    double Projected,
    Status Status,
    TimeSpan? TimeToLimit,
    DateTimeOffset? LimitAt);

/// <summary>
/// Maps the Session window to a pace-based <see cref="Status"/>. Stateless: derived entirely from
/// utilization, resets_at, now, and the fixed 5h window length. The API carries no window-start
/// timestamp, so the start is inferred as resets_at − SessionLength.
/// </summary>
public static class SessionPace
{
    public static readonly TimeSpan SessionLength = TimeSpan.FromHours(5);

    public static PaceResult Evaluate(UsageWindow session, DateTimeOffset now, PaceSettings s)
    {
        var start = session.ResetsAt - SessionLength;
        var elapsed = now - start;
        // timeFrac is clamped; elapsed stays raw. Past reset, clamping forces projected <= usage <= 100,
        // so the projected > 100 time-to-limit branch never fires off an out-of-range elapsed.
        var timeFrac = Math.Clamp(elapsed / SessionLength, 0.0, 1.0);
        var usage = session.Utilization;

        // Early-session tolerance: a usage floor that decays from EarlyFloorStartPercent at t=0 to
        // EarlyFloorBasePercent by EarlyGracePercent of the window. Below it, always green.
        var graceFrac = s.EarlyGracePercent / 100.0;
        var floor = timeFrac >= graceFrac
            ? s.EarlyFloorBasePercent
            : s.EarlyFloorBasePercent
                + (s.EarlyFloorStartPercent - s.EarlyFloorBasePercent) * (1 - timeFrac / graceFrac);

        var projected = timeFrac > 0 ? usage / timeFrac : 0;   // guard: t≈0 -> 0, suppressed by floor anyway

        Status status;
        if (usage < floor || projected < s.YellowProjected) status = Status.Green;
        else if (projected >= s.RedProjected) status = Status.Red;
        else if (projected >= s.OrangeProjected) status = Status.Orange;
        else status = Status.Yellow;

        TimeSpan? timeToLimit = null;
        DateTimeOffset? limitAt = null;
        if (projected > 100 && usage > 0)
        {
            var ttl = TimeSpan.FromHours((100 - usage) * elapsed.TotalHours / usage);
            timeToLimit = ttl;
            limitAt = now + ttl;
        }

        return new PaceResult(timeFrac, usage, projected, status, timeToLimit, limitAt);
    }
}
