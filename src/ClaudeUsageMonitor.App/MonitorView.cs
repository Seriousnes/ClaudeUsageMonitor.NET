using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public record WindowRow(string Label, double Utilization, string ResetText);

public record MonitorView(
    Status Status,
    string PlanLabel,
    IReadOnlyList<WindowRow> Rows,
    BurnEstimate? Burn,
    Freshness Freshness,
    string AgeText,
    PaceResult? Pace);
