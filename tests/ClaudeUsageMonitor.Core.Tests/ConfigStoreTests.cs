using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class ConfigStoreTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var store = new ConfigStore(TempPath());
        var cfg = store.Load();
        Assert.Equal(60, cfg.PollIntervalSeconds);
        Assert.Equal([80, 95], cfg.AlertThresholds);
        Assert.Empty(cfg.WeeklyModels);                 // default matches Desktop's All Models aggregate
        Assert.True(cfg.StartWithWindows);
        Assert.False(cfg.ClickThrough);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var path = TempPath();
        var store = new ConfigStore(path);
        var cfg = new MonitorConfig { PollIntervalSeconds = 30, WeeklyModels = ["opus", "sonnet"], ClickThrough = true };
        cfg.WidgetPosition.X = 100; cfg.WidgetPosition.Y = 200;
        store.Save(cfg);

        var loaded = store.Load();
        Assert.Equal(30, loaded.PollIntervalSeconds);
        Assert.Equal(["opus", "sonnet"], loaded.WeeklyModels);
        Assert.Equal(100, loaded.WidgetPosition.X);
        Assert.True(loaded.ClickThrough);
        File.Delete(path);
    }

    [Fact]
    public void Load_partial_json_keeps_defaults_for_missing_fields()
    {
        var path = TempPath();
        File.WriteAllText(path, """{ "pollIntervalSeconds": 15 }""");
        var loaded = new ConfigStore(path).Load();
        Assert.Equal(15, loaded.PollIntervalSeconds);
        Assert.Equal([80, 95], loaded.AlertThresholds);   // default preserved
        Assert.True(loaded.StartWithWindows);                     // default preserved
        File.Delete(path);
    }

    [Fact]
    public void Save_creates_missing_parent_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(dir, "config.json");   // parent dir does not exist yet
        new ConfigStore(path).Save(new MonitorConfig());
        Assert.True(File.Exists(path));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Save_writes_camelCase_keys()
    {
        var path = TempPath();
        new ConfigStore(path).Save(new MonitorConfig());
        var json = File.ReadAllText(path);
        Assert.Contains("\"pollIntervalSeconds\"", json);
        Assert.DoesNotContain("\"PollIntervalSeconds\"", json);
        File.Delete(path);
    }

    [Fact]
    public void Load_missing_pace_uses_defaults()
    {
        var path = TempPath();
        File.WriteAllText(path, """{ "pollIntervalSeconds": 15 }""");
        var cfg = new ConfigStore(path).Load();
        Assert.Equal(95.0, cfg.Pace.YellowProjected);
        Assert.Equal(115.0, cfg.Pace.OrangeProjected);
        Assert.Equal(140.0, cfg.Pace.RedProjected);
        Assert.Equal(20.0, cfg.Pace.EarlyFloorStartPercent);
        Assert.Equal(5.0, cfg.Pace.EarlyFloorBasePercent);
        Assert.Equal(15.0, cfg.Pace.EarlyGracePercent);
        Assert.Equal(85.0, cfg.Pace.HighUsageOrange);
        Assert.Equal(95.0, cfg.Pace.HighUsageRed);
        File.Delete(path);
    }

    [Fact]
    public void Pace_round_trips()
    {
        var path = TempPath();
        var cfg = new MonitorConfig();
        cfg.Pace.RedProjected = 200;
        cfg.Pace.HighUsageOrange = 70;
        new ConfigStore(path).Save(cfg);

        var loaded = new ConfigStore(path).Load();
        Assert.Equal(200.0, loaded.Pace.RedProjected);
        Assert.Equal(70.0, loaded.Pace.HighUsageOrange);
        Assert.Equal(95.0, loaded.Pace.YellowProjected);   // untouched default preserved
        File.Delete(path);
    }
}
