namespace ClaudeUsageMonitor.Core;

public class MonitorConfig
{
    public int PollIntervalSeconds { get; set; } = 60;
    public int[] AlertThresholds { get; set; } = [80, 95];
    public string[] WeeklyModels { get; set; } = [];
    public double WidgetOpacity { get; set; } = 0.92;
    public WidgetPosition WidgetPosition { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public bool ClickThrough { get; set; } = false;
}

public class WidgetPosition
{
    public double? X { get; set; }
    public double? Y { get; set; }
}
