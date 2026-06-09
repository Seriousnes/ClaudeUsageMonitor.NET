namespace ClaudeUsageMonitor.Core.Tests;

public class UsageBandTests
{
    private static readonly UsageBandSettings S = new();

    [Theory]
    [InlineData(39, Status.Green)]
    [InlineData(40, Status.Yellow)]
    [InlineData(64, Status.Yellow)]
    [InlineData(65, Status.Orange)]
    [InlineData(84, Status.Orange)]
    [InlineData(85, Status.Red)]
    [InlineData(100, Status.Red)]
    public void Maps_fill_percent_to_band(double util, Status expected)
        => Assert.Equal(expected, UsageBand.Evaluate(util, S));

    [Fact]
    public void Honours_custom_thresholds()
    {
        var custom = new UsageBandSettings { YellowPercent = 50, OrangePercent = 70, RedPercent = 90 };
        Assert.Equal(Status.Green, UsageBand.Evaluate(49, custom));
        Assert.Equal(Status.Yellow, UsageBand.Evaluate(50, custom));
        Assert.Equal(Status.Orange, UsageBand.Evaluate(70, custom));
        Assert.Equal(Status.Red, UsageBand.Evaluate(90, custom));
    }
}
