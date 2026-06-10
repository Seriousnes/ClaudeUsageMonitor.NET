using System.Diagnostics;
using System.Windows;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public partial class App : Application
{
    private TrayIcon? _tray;
    private MonitorController? _controller;
    private WidgetWindow? _widget;
    private ConfigStore? _configStore;
    private MonitorConfig? _config;
    private IErrorLog? _errorLog;
    private string _configPath = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var claudeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var credentialsPath = Path.Combine(claudeDir, ".credentials.json");
        var projectsRoot = Path.Combine(claudeDir, "projects");
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageMonitor", "config.json");

        var firstRun = !File.Exists(_configPath);
        _configStore = new ConfigStore(_configPath);
        _config = _configStore.Load();
        if (firstRun)
        {
            // Runs before the dispatcher loop and before the tray exists, so OnDispatcherUnhandledException
            // can't catch it — guard here so a write/registry failure degrades to in-memory defaults.
            try
            {
                _configStore.Save(_config);                 // materialize defaults for the user to edit
                if (_config.StartWithWindows) StartupRegistry.Set(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"First-run setup failed: {ex.Message}");
            }
        }

        _widget = new WidgetWindow(_config.WidgetOpacity, _config.WidgetPosition.X, _config.WidgetPosition.Y);
        _widget.RefreshRequested += () => _controller?.RefreshNow();
        _widget.CloseRequested += HideWidget;
        _widget.SetClickThrough(_config.ClickThrough);   // applied for real once the HWND exists (on show)

        _tray = new TrayIcon();
        _tray.Quit += Shutdown;
        _tray.RefreshNow += () => _controller?.RefreshNow();
        _tray.ToggleWidget += ToggleWidget;
        _tray.ToggleStartup += ToggleStartup;
        _tray.ToggleClickThrough += ToggleClickThrough;
        _tray.OpenSettings += OpenSettings;
        _tray.SetStartupChecked(StartupRegistry.IsEnabled());
        _tray.SetClickThroughChecked(_config.ClickThrough);

        var logPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "log.txt");
        _errorLog = new FileErrorLog(logPath, TimeProvider.System, TimeSpan.FromMinutes(10));
        var rateLimitGate = new ConfigRateLimitGate(_config, _configStore);

        _controller = new MonitorController(credentialsPath, projectsRoot, _config, TimeProvider.System,
            _errorLog, rateLimitGate);
        _controller.Updated += OnViewUpdated;
        _controller.Alert += OnAlert;
        _controller.Start();
    }

    private void ToggleStartup()
    {
        if (_config is null || _configStore is null) return;
        var enabled = !StartupRegistry.IsEnabled();
        StartupRegistry.Set(enabled);
        _config.StartWithWindows = enabled;
        _configStore.Save(_config);
        _tray?.SetStartupChecked(enabled);
    }

    private void ToggleClickThrough()
    {
        if (_config is null || _configStore is null) return;
        _config.ClickThrough = !_config.ClickThrough;
        _configStore.Save(_config);
        _widget?.SetClickThrough(_config.ClickThrough);
        _tray?.SetClickThroughChecked(_config.ClickThrough);
    }

    private void OpenSettings()
    {
        if (_configStore is null || _config is null) return;
        _configStore.Save(_config);   // re-materialize so newly-added settings appear in the file before editing
        Process.Start(new ProcessStartInfo(_configPath) { UseShellExecute = true });
    }

    private void ToggleWidget()
    {
        if (_widget is null) return;
        if (_widget.IsVisible) HideWidget();
        else { _widget.Show(); _widget.Activate(); }
    }

    private void HideWidget()
    {
        if (_widget is null || !_widget.IsVisible) return;
        SaveWidgetPosition();   // persist position while still visible (the ✕ path also routes here)
        _widget.Hide();
    }

    private void SaveWidgetPosition()
    {
        if (_widget is null || _config is null || _configStore is null || !_widget.IsVisible) return;
        _config.WidgetPosition.X = _widget.Left;
        _config.WidgetPosition.Y = _widget.Top;
        _configStore.Save(_config);
    }

    private void OnViewUpdated(MonitorView view) => Dispatcher.Invoke(() =>
    {
        _tray?.Update(view.Status, BuildTooltip(view));
        _widget?.Render(view);
    });

    private void OnAlert(string title, string text) => Dispatcher.Invoke(() => _tray?.ShowToast(title, text));

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // A tray-only app must not die from a transient failure in a menu action or a background update
        // (e.g. Settings has no .json association, or an HKCU write is denied). Surface and keep running.
        _errorLog?.RecordFailure("ui", $"{e.Exception.GetType().Name}: {e.Exception.Message}");
        _tray?.ShowToast("Claude Usage Monitor", e.Exception.Message);
        e.Handled = true;
    }

    private static string BuildTooltip(MonitorView view)
    {
        if (view.Rows.Count == 0) return "Claude Usage — no data";
        var parts = view.Rows.Select(r => $"{r.Label} {r.Utilization:0}% · {r.ResetText}");
        var text = string.Join(" | ", parts);
        return view.Freshness != Freshness.Live ? $"{text}  ({view.AgeText})" : text;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveWidgetPosition();
        _controller?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
