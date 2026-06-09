namespace ClaudeUsageMonitor.Core;

/// <summary>
/// Maps a usage bar's own fill % to a discrete four-band <see cref="Status"/> (Green/Yellow/Orange/Red).
/// Stateless and config-driven — the consumption story, distinct from the pace-based icon in <see cref="SessionPace"/>.
/// </summary>
public static class UsageBand
{
    public static Status Evaluate(double util, UsageBandSettings s)
        => util >= s.RedPercent ? Status.Red
         : util >= s.OrangePercent ? Status.Orange
         : util >= s.YellowPercent ? Status.Yellow
         : Status.Green;
}
