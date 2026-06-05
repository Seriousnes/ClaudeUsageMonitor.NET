using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public partial class WidgetWindow : Window
{
    public event Action? RefreshRequested;
    public event Action? CloseRequested;

    private bool _clickThrough;

    public WidgetWindow(double opacity, double? x, double? y)
    {
        InitializeComponent();
        Opacity = opacity;
        if (x is not null && y is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = x.Value;
            Top = y.Value;
        }
        HeaderBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        RefreshButton.Click += (_, _) => RefreshRequested?.Invoke();
        // App owns hiding so it can persist the (possibly dragged) position before the window hides.
        CloseButton.Click += (_, _) => CloseRequested?.Invoke();
        // The HWND is created on SourceInitialized; re-apply on each show in case it was toggled while hidden.
        SourceInitialized += (_, _) => ApplyClickThrough();
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) ApplyClickThrough(); };
    }

    /// <summary>Toggle whether mouse input passes through the widget to the window behind it.</summary>
    public void SetClickThrough(bool on)
    {
        _clickThrough = on;
        ApplyClickThrough();
    }

    private void ApplyClickThrough()
        => WindowInterop.SetClickThrough(new WindowInteropHelper(this).Handle, _clickThrough);

    public void Render(MonitorView view)
    {
        PlanText.Text = $"Claude  ·  {view.PlanLabel}";
        RowsPanel.Children.Clear();
        var grey = view.Freshness == Freshness.Stale;
        foreach (var row in view.Rows)
            RowsPanel.Children.Add(BuildRow(row, grey));

        if (view.Freshness != Freshness.Live && view.Rows.Count > 0)
        {
            // Recent and Stale both carry an age hint; Recent keeps real row colors, Stale greys them.
            // Keep the activity readout visible alongside it (spec §7).
            EtaText.Text = view.Burn is not null ? $"{view.AgeText}  ·  {FormatBurn(view.Burn)}" : view.AgeText;
            EtaText.Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
            EtaText.Visibility = Visibility.Visible;
        }
        else if (view.Freshness == Freshness.Live && view.Burn is not null)
        {
            EtaText.Text = FormatBurn(view.Burn);
            EtaText.Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E));
            EtaText.Visibility = Visibility.Visible;
        }
        else
        {
            EtaText.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatBurn(BurnEstimate burn)
    {
        if (burn.EtaSoonest is { } eta)
        {
            var human = eta < TimeSpan.FromHours(1)
                ? $"~{(int)eta.TotalMinutes} min"
                : $"~{(int)eta.TotalHours}h {eta.Minutes}m";
            return $"⚠ {human} to {burn.EtaWindowLabel} limit";
        }
        return $"≈ {HumanNumber.Format(burn.TokensPerHour)} tok/hr";   // active but no projection
    }

    private static StackPanel BuildRow(WindowRow row, bool stale)
    {
        var fg = new SolidColorBrush(stale ? Color.FromRgb(0x9E, 0x9E, 0x9E) : Color.FromRgb(0xEE, 0xEE, 0xEE));
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = row.Label, Foreground = fg };
        var pct = new TextBlock { Text = $"{row.Utilization:0}%", Foreground = fg, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(pct, 1);
        top.Children.Add(label);
        top.Children.Add(pct);

        var bar = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Value = row.Utilization,
            Height = 6, Margin = new Thickness(0, 3, 0, 1),
        };
        var reset = new TextBlock
        {
            Text = row.ResetText,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 11,
        };

        panel.Children.Add(top);
        panel.Children.Add(bar);
        panel.Children.Add(reset);
        return panel;
    }
}
