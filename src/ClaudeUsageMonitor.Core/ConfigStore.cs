using System.Text.Json;

namespace ClaudeUsageMonitor.Core;

public class ConfigStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public MonitorConfig Load()
    {
        if (!File.Exists(path))
            return new MonitorConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MonitorConfig>(json, Options) ?? new MonitorConfig();
        }
        catch (JsonException)
        {
            return new MonitorConfig();
        }
    }

    public void Save(MonitorConfig config)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);   // no-op if it already exists
        File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
    }
}
