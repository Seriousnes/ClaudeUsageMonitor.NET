using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class HumanNumberTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(465, "465")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0k")]
    [InlineData(3465, "3.5k")]
    [InlineData(999_999, "1.0m")]   // rounds up across the unit boundary, not "1000.0k"
    [InlineData(1_000_000, "1.0m")]
    [InlineData(1_324_123, "1.3m")]
    public void Format_matches_expected(double n, string expected)
        => Assert.Equal(expected, HumanNumber.Format(n));
}
