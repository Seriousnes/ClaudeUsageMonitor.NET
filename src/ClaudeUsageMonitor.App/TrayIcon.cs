using System.Windows.Forms;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _clickThroughItem;
    private System.Drawing.Icon? _currentIcon;

    public event Action? ToggleWidget;
    public event Action? RefreshNow;
    public event Action? ToggleStartup;
    public event Action? ToggleClickThrough;
    public event Action? OpenSettings;
    public event Action? Quit;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show/Hide widget", null, (_, _) => ToggleWidget?.Invoke());
        menu.Items.Add("Refresh now", null, (_, _) => RefreshNow?.Invoke());
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup?.Invoke());
        menu.Items.Add(_startupItem);
        _clickThroughItem = new ToolStripMenuItem("Click-through", null, (_, _) => ToggleClickThrough?.Invoke());
        menu.Items.Add(_clickThroughItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings?.Invoke());
        menu.Items.Add("Quit", null, (_, _) => Quit?.Invoke());

        _currentIcon = IconRenderer.Render(Status.Stale);
        _notify = new NotifyIcon
        {
            Visible = true,
            Text = "Claude Usage Monitor",
            ContextMenuStrip = menu,
            Icon = _currentIcon,
        };
        _notify.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleWidget?.Invoke(); };
    }

    public void Update(Status status, string tooltip)
    {
        var newIcon = IconRenderer.Render(status);
        _notify.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;
        _notify.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;   // NotifyIcon.Text caps at 63 chars
    }

    public void SetStartupChecked(bool on) => _startupItem.Checked = on;

    public void SetClickThroughChecked(bool on) => _clickThroughItem.Checked = on;

    public void ShowToast(string title, string text)
        => _notify.ShowBalloonTip(5000, title, text, ToolTipIcon.Warning);

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
        _currentIcon?.Dispose();
    }
}
