using System.Globalization;

namespace ClaudeUsageMonitor.Core;

/// <summary>Renders a token count compactly: "465", "3.5k", "1.3m". Always one decimal at k/m scale.</summary>
public static class HumanNumber
{
    public static string Format(double n)
    {
        if (n < 1_000)
        {
            // Round once and reuse for both the promote decision and the display, so a value that
            // rounds up to a full unit (999_999 -> "1.0m", never "1000.0k") never disagrees with itself.
            var whole = Math.Round(n, MidpointRounding.AwayFromZero);
            if (whole < 1_000)
                return whole.ToString("0", CultureInfo.InvariantCulture);
            n = whole;
        }

        if (n < 1_000_000)
        {
            var k = Math.Round(n / 1_000.0, 1, MidpointRounding.AwayFromZero);
            if (k < 1_000)
                return k.ToString("0.0", CultureInfo.InvariantCulture) + "k";
        }

        var m = Math.Round(n / 1_000_000.0, 1, MidpointRounding.AwayFromZero);
        return m.ToString("0.0", CultureInfo.InvariantCulture) + "m";
    }
}
