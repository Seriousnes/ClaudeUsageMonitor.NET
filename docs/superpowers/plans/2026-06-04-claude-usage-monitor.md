# Claude Usage Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a lightweight Windows tray app (.NET 10 / WPF) that displays Claude subscription usage in real time, matching `/usage` exactly, with a color-coded tray icon, a draggable widget, and 80/95% threshold toasts.

**Architecture:** A WPF UI layer (tray `NotifyIcon` + frameless widget) over a fully headless, unit-tested `Core` library (auth, polling, parsing, window selection, burn/ETA, alerting, config). The `Core` layer holds all logic and references no WPF/WinForms; the App layer is a thin composition root that wires Core components to UI and is verified manually. A single `TrackedWindowResolver` produces the one set of "tracked windows" that the tray color, tooltip, widget rows, ETA, and alerts all consume — so what you see is exactly what you're warned about.

**Tech Stack:** .NET 10, WPF (`net10.0-windows`), WinForms `NotifyIcon` (via `UseWindowsForms`), `System.Text.Json`, `HttpClient`, `TimeProvider`/`ITimer` for all timing, xUnit + `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`).

---

## Key design decisions (read before starting)

These resolve ambiguities in the spec and are load-bearing across tasks. Do not deviate without updating the plan.

1. **`TrackedWindow(string Label, string Key, UsageWindow Window)` is the linchpin type.** `TrackedWindowResolver` is its *sole* producer. Tray color, tooltip, widget rows, ETA, and alerts all consume this one list — none of them re-resolve windows independently. This is what makes spec §5.1's "what you see is exactly what you're warned about" guarantee hold.
2. **`Key` (stable API field name, e.g. `seven_day_opus`) is separate from `Label` (display text, e.g. "Week").** ETA history accumulation and alert arming **key on `Key`, never `Label`.** Reason: under `["highest"]`, the "Week" label can point at `seven_day_opus` one poll and fall back to `seven_day` the next — same label, different window. Keying on `Key` prevents splicing two windows into one ETA slope and prevents spurious alert re-fires.
3. **All time comes from `TimeProvider`.** Tests inject `FakeTimeProvider`; production uses `TimeProvider.System` and `TimeProvider.CreateTimer` for cadence. No `DateTimeOffset.Now`/`Now` in Core logic.
4. **OAuth refresh is deferred (spec §7 open detail).** `ITokenRefresher` is injectable; the v1 `StubTokenRefresher` always returns `null`, so expired tokens degrade to `Stale`. Wiring a real refresher later is a one-class change.
5. **Threshold/color boundaries are pinned once** in `StatusCalculator` constants and tested at exact values: Green `<50`, Yellow `[50,80)`, Orange `[80,95)`, Red `>=95`. `Stale` overrides color regardless of utilization.
6. **Alerts re-arm** when a window's `resets_at` advances **or** its utilization drops below the lowest configured threshold. Each threshold fires once per arm.
7. **Toasts use `NotifyIcon.ShowBalloonTip`** in v1 (zero extra deps; Win11 routes balloons to the Action Center, and they carry title+text so "Week at 80%" works). Upgrade path (real toast via an AppUserModelID/shortcut + CommunityToolkit) is explicitly out of v1 scope.
8. **Out of scope for v1 (state explicitly, don't silently drop):** single-instance enforcement (relaunch → two tray icons + double alerts is the known failure mode, accepted for v1); real OAuth refresh; historical graphs; cost estimates; settings GUI; per-model 5h breakdown (not in API).

---

## File Structure

**`src/ClaudeUsageMonitor.Core/`** (`net10.0`, no WPF/WinForms — fully testable):
- `Models.cs` — `UsageWindow`, `UsageSnapshot`, `TrackedWindow`, `BurnEstimate`, `Status` enum
- `MonitorConfig.cs` — config POCO + `WidgetPosition`
- `ConfigStore.cs` — load/save `config.json` with defaults
- `UsageApiParser.cs` — raw usage JSON → `UsageSnapshot`
- `TrackedWindowResolver.cs` — `config.weeklyModels` + snapshot → `IReadOnlyList<TrackedWindow>`
- `StatusCalculator.cs` — tracked windows (+ stale flag) → `Status`
- `ResetFormatter.cs` — `resets_at` + now → human string
- `EtaProjector.cs` — rolling utilization samples → soonest ETA
- `JsonlActivityReader.cs` — JSONL lines → token entries + tokens/hr; `JsonlActivityScanner` (IO)
- `AlertEngine.cs` — per-window threshold arming → alerts
- `Credentials.cs` — `Credentials` record, `CredentialsReader`, `PlanLabelMapper`
- `AuthProvider.cs` — `AuthResult`, `ITokenRefresher`, `StubTokenRefresher`, `AuthProvider`
- `UsageApiClient.cs` — `IUsageApiClient`, `HttpUsageApiClient`
- `UsagePoller.cs` — `PollResult`, `UsagePoller`
- `Debouncer.cs` — coalesce rapid triggers
- `JsonlWatcher.cs` — `FileSystemWatcher` + debounce (thin)

**`src/ClaudeUsageMonitor.App/`** (`net10.0-windows`, WPF + WinForms — composition root + UI):
- `App.xaml` / `App.xaml.cs` — composition root, lifecycle
- `TrayIcon.cs` — `NotifyIcon` wrapper, context menu, balloon toasts
- `IconRenderer.cs` — generate colored status icon
- `WidgetWindow.xaml` / `WidgetWindow.xaml.cs` — frameless always-on-top widget
- `StartupRegistry.cs` — HKCU `...\Run` start-with-Windows toggle

**`tests/ClaudeUsageMonitor.Core.Tests/`** (`net10.0`, xUnit): one test file per Core component.

---

## Task 1: Solution & project scaffolding

**Files:**
- Create: `ClaudeUsageMonitor.sln`, `src/ClaudeUsageMonitor.Core/ClaudeUsageMonitor.Core.csproj`, `src/ClaudeUsageMonitor.App/ClaudeUsageMonitor.App.csproj`, `tests/ClaudeUsageMonitor.Core.Tests/ClaudeUsageMonitor.Core.Tests.csproj`

- [ ] **Step 1: Create solution and projects**

```powershell
dotnet new sln -n ClaudeUsageMonitor
dotnet new classlib -n ClaudeUsageMonitor.Core -o src/ClaudeUsageMonitor.Core -f net10.0
dotnet new wpf -n ClaudeUsageMonitor.App -o src/ClaudeUsageMonitor.App -f net10.0   # template produces a net10.0-windows TFM
dotnet new xunit -n ClaudeUsageMonitor.Core.Tests -o tests/ClaudeUsageMonitor.Core.Tests -f net10.0
Remove-Item src/ClaudeUsageMonitor.Core/Class1.cs
```

- [ ] **Step 2: Add projects to solution and wire references**

```powershell
dotnet sln add src/ClaudeUsageMonitor.Core/ClaudeUsageMonitor.Core.csproj
dotnet sln add src/ClaudeUsageMonitor.App/ClaudeUsageMonitor.App.csproj
dotnet sln add tests/ClaudeUsageMonitor.Core.Tests/ClaudeUsageMonitor.Core.Tests.csproj
dotnet add src/ClaudeUsageMonitor.App reference src/ClaudeUsageMonitor.Core
dotnet add tests/ClaudeUsageMonitor.Core.Tests reference src/ClaudeUsageMonitor.Core
dotnet add tests/ClaudeUsageMonitor.Core.Tests package Microsoft.Extensions.TimeProvider.Testing
```

- [ ] **Step 3: Enable WinForms in the App project (for `NotifyIcon`)**

Edit `src/ClaudeUsageMonitor.App/ClaudeUsageMonitor.App.csproj` — add `<UseWindowsForms>true</UseWindowsForms>` inside the existing `<PropertyGroup>` that already contains `<UseWPF>true</UseWPF>`:

```xml
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
```

- [ ] **Step 4: Build to verify the skeleton compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (The default WPF `MainWindow` still exists; it is removed in Task 16.)

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "chore: scaffold solution (Core, App, Core.Tests)"
```

---

## Task 2: Core domain models

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/Models.cs`

These are pure data carriers with no behavior, so there is no failing test — correctness is enforced by the components that consume them in later tasks. Define them once, exactly, so every later task references the same shapes.

- [ ] **Step 1: Write the models**

```csharp
namespace ClaudeUsageMonitor.Core;

/// <summary>One usage window from the API: a 0–100 utilization % and when it resets.</summary>
public record UsageWindow(double Utilization, DateTimeOffset ResetsAt);

/// <summary>A full parsed usage response. WeeklyByModel is keyed by model family ("opus","sonnet",…), non-null windows only.</summary>
public record UsageSnapshot(
    UsageWindow FiveHour,
    UsageWindow SevenDay,
    IReadOnlyDictionary<string, UsageWindow> WeeklyByModel,
    DateTimeOffset FetchedAt,
    string PlanLabel);

/// <summary>
/// A window the user is actively tracking. Label is display text ("Session","Week","Opus");
/// Key is the stable API field name ("five_hour","seven_day","seven_day_opus"). ETA history and
/// alert arming MUST key on Key, never Label.
/// </summary>
public record TrackedWindow(string Label, string Key, UsageWindow Window);

/// <summary>Burn-rate readout: tokens/hour and the soonest-projected window's ETA + label.</summary>
public record BurnEstimate(double TokensPerHour, TimeSpan? EtaSoonest, string? EtaWindowLabel);

public enum Status { Green, Yellow, Orange, Red, Stale }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ClaudeUsageMonitor.Core`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/Models.cs
git commit -m "feat(core): add domain models"
```

---

## Task 3: ConfigStore

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/MonitorConfig.cs`, `src/ClaudeUsageMonitor.Core/ConfigStore.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/ConfigStoreTests.cs`

- [ ] **Step 1: Write the config model**

```csharp
namespace ClaudeUsageMonitor.Core;

public class MonitorConfig
{
    public int PollIntervalSeconds { get; set; } = 60;
    public int[] AlertThresholds { get; set; } = [80, 95];
    public string[] WeeklyModels { get; set; } = ["highest"];
    public double WidgetOpacity { get; set; } = 0.92;
    public WidgetPosition WidgetPosition { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
}

public class WidgetPosition
{
    public double? X { get; set; }
    public double? Y { get; set; }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
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
        Assert.Equal(new[] { 80, 95 }, cfg.AlertThresholds);
        Assert.Equal(new[] { "highest" }, cfg.WeeklyModels);
        Assert.True(cfg.StartWithWindows);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var path = TempPath();
        var store = new ConfigStore(path);
        var cfg = new MonitorConfig { PollIntervalSeconds = 30, WeeklyModels = ["opus", "sonnet"] };
        cfg.WidgetPosition.X = 100; cfg.WidgetPosition.Y = 200;
        store.Save(cfg);

        var loaded = store.Load();
        Assert.Equal(30, loaded.PollIntervalSeconds);
        Assert.Equal(new[] { "opus", "sonnet" }, loaded.WeeklyModels);
        Assert.Equal(100, loaded.WidgetPosition.X);
        File.Delete(path);
    }

    [Fact]
    public void Load_partial_json_keeps_defaults_for_missing_fields()
    {
        var path = TempPath();
        File.WriteAllText(path, """{ "pollIntervalSeconds": 15 }""");
        var loaded = new ConfigStore(path).Load();
        Assert.Equal(15, loaded.PollIntervalSeconds);
        Assert.Equal(new[] { 80, 95 }, loaded.AlertThresholds);   // default preserved
        Assert.True(loaded.StartWithWindows);                     // default preserved
        File.Delete(path);
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
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter ConfigStoreTests`
Expected: FAIL — `ConfigStore` does not exist.

- [ ] **Step 4: Write the implementation**

```csharp
using System.Text.Json;

namespace ClaudeUsageMonitor.Core;

public class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    public ConfigStore(string path) => _path = path;

    public MonitorConfig Load()
    {
        if (!File.Exists(_path))
            return new MonitorConfig();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<MonitorConfig>(json, Options) ?? new MonitorConfig();
        }
        catch (JsonException)
        {
            return new MonitorConfig();
        }
    }

    public void Save(MonitorConfig config)
        => File.WriteAllText(_path, JsonSerializer.Serialize(config, Options));
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter ConfigStoreTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/MonitorConfig.cs src/ClaudeUsageMonitor.Core/ConfigStore.cs tests/ClaudeUsageMonitor.Core.Tests/ConfigStoreTests.cs
git commit -m "feat(core): add MonitorConfig and ConfigStore"
```

---

## Task 4: UsageApiParser

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/UsageApiParser.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/UsageApiParserTests.cs`

The fixture below is the documented §2.1 shape with no secrets — this satisfies "tested against a captured sample response" without copying real tokens.

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class UsageApiParserTests
{
    private const string Sample = """
    {
      "five_hour": { "utilization": 13.0, "resets_at": "2026-06-04T20:30:00.737894+10:00" },
      "seven_day": { "utilization": 20.0, "resets_at": "2026-06-10T18:00:00.73791+10:00" },
      "seven_day_opus": null,
      "seven_day_sonnet": { "utilization": 2.0, "resets_at": "2026-06-10T18:00:00.73791+10:00" },
      "extra_usage": { "is_enabled": false, "monthly_limit": null, "used_credits": null,
                       "utilization": null, "currency": null, "disabled_reason": null }
    }
    """;

    private static readonly DateTimeOffset Now = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_aggregate_windows()
    {
        var s = UsageApiParser.Parse(Sample, "Max 5x", Now);
        Assert.Equal(13.0, s.FiveHour.Utilization);
        Assert.Equal(20.0, s.SevenDay.Utilization);
        Assert.Equal("Max 5x", s.PlanLabel);
        Assert.Equal(Now, s.FetchedAt);
    }

    [Fact]
    public void Parses_resets_at_with_offset()
    {
        var s = UsageApiParser.Parse(Sample, "Max 5x", Now);
        Assert.Equal(new DateTimeOffset(2026, 6, 4, 20, 30, 0, 737, TimeSpan.FromHours(10)).ToUnixTimeSeconds(),
                     s.FiveHour.ResetsAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void Includes_only_non_null_model_windows()
    {
        var s = UsageApiParser.Parse(Sample, "Max 5x", Now);
        Assert.True(s.WeeklyByModel.ContainsKey("sonnet"));
        Assert.False(s.WeeklyByModel.ContainsKey("opus"));   // was null in response
        Assert.Equal(2.0, s.WeeklyByModel["sonnet"].Utilization);
    }

    [Fact]
    public void Tolerates_unknown_keys()
    {
        const string withUnknown = """
        { "five_hour": { "utilization": 1, "resets_at": "2026-06-04T20:30:00+10:00" },
          "seven_day": { "utilization": 1, "resets_at": "2026-06-10T18:00:00+10:00" },
          "brand_new_key": { "whatever": true } }
        """;
        var s = UsageApiParser.Parse(withUnknown, "Pro", Now);
        Assert.Empty(s.WeeklyByModel);
        Assert.Equal(1.0, s.FiveHour.Utilization);
    }

    [Fact]
    public void Throws_when_required_window_missing()
        => Assert.Throws<FormatException>(() => UsageApiParser.Parse("""{ "seven_day": {} }""", "Pro", Now));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter UsageApiParserTests`
Expected: FAIL — `UsageApiParser` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.Json;

namespace ClaudeUsageMonitor.Core;

public static class UsageApiParser
{
    private const string WeeklyModelPrefix = "seven_day_";

    public static UsageSnapshot Parse(string json, string planLabel, DateTimeOffset fetchedAt)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var fiveHour = ParseWindow(root, "five_hour")
            ?? throw new FormatException("usage response missing valid 'five_hour'");
        var sevenDay = ParseWindow(root, "seven_day")
            ?? throw new FormatException("usage response missing valid 'seven_day'");

        var weeklyByModel = new Dictionary<string, UsageWindow>();
        foreach (var prop in root.EnumerateObject())
        {
            // "seven_day_opus" matches; "seven_day" does not (no trailing underscore).
            if (!prop.Name.StartsWith(WeeklyModelPrefix, StringComparison.Ordinal))
                continue;
            var window = ParseWindowElement(prop.Value);
            if (window is not null)
                weeklyByModel[prop.Name[WeeklyModelPrefix.Length..]] = window;
        }

        return new UsageSnapshot(fiveHour, sevenDay, weeklyByModel, fetchedAt, planLabel);
    }

    private static UsageWindow? ParseWindow(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) ? ParseWindowElement(el) : null;

    private static UsageWindow? ParseWindowElement(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) return null;
        if (!el.TryGetProperty("resets_at", out var r) || r.ValueKind != JsonValueKind.String) return null;
        return new UsageWindow(u.GetDouble(), DateTimeOffset.Parse(r.GetString()!));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter UsageApiParserTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/UsageApiParser.cs tests/ClaudeUsageMonitor.Core.Tests/UsageApiParserTests.cs
git commit -m "feat(core): parse usage API response, tolerate null/unknown keys"
```

---

## Task 5: TrackedWindowResolver

This is the linchpin (decision #1/#2). It turns a snapshot + `config.weeklyModels` into the one tracked-window list everything else consumes.

**Resolution rules (from spec §5.1):**
- Session is always `TrackedWindow("Session", "five_hour", FiveHour)`, first in the list.
- `[]` → aggregate weekly only: `("Week", "seven_day", SevenDay)`.
- `["highest"]` → first non-null of tier order `opus > sonnet > haiku`, as `("Week", "seven_day_<tier>", …)`; if none present, fall back to aggregate `("Week", "seven_day", …)`.
- `["*"]` → aggregate + every non-null per-model window.
- explicit families (e.g. `["opus","sonnet"]`) → those present, non-null.
- **Labeling:** exactly one weekly window → label "Week". Multiple → the aggregate (`seven_day`) is "Week" and the rest are title-cased model names ("Opus","Sonnet"). If multiple and none is the aggregate, all keep model-name labels.
- **Never blank:** any resolution yielding zero weekly windows falls back to aggregate `("Week", "seven_day", …)`.

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/TrackedWindowResolver.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/TrackedWindowResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class TrackedWindowResolverTests
{
    private static readonly DateTimeOffset R = new(2026, 6, 10, 18, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snap(params (string key, double util)[] models)
    {
        var dict = models.ToDictionary(m => m.key, m => new UsageWindow(m.util, R));
        return new UsageSnapshot(new UsageWindow(13, R), new UsageWindow(20, R), dict,
            DateTimeOffset.UnixEpoch, "Max 5x");
    }

    [Fact]
    public void Session_is_always_first()
    {
        var t = TrackedWindowResolver.Resolve(Snap(), ["highest"]);
        Assert.Equal("Session", t[0].Label);
        Assert.Equal("five_hour", t[0].Key);
        Assert.Equal(13, t[0].Window.Utilization);
    }

    [Fact]
    public void Highest_picks_opus_when_present()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64), ("sonnet", 2)), ["highest"]);
        var week = t[1];
        Assert.Equal("Week", week.Label);
        Assert.Equal("seven_day_opus", week.Key);
        Assert.Equal(64, week.Window.Utilization);
    }

    [Fact]
    public void Highest_falls_back_to_sonnet_then_aggregate()
    {
        var sonnetOnly = TrackedWindowResolver.Resolve(Snap(("sonnet", 2)), ["highest"]);
        Assert.Equal("seven_day_sonnet", sonnetOnly[1].Key);

        var none = TrackedWindowResolver.Resolve(Snap(), ["highest"]);   // no per-model windows
        Assert.Equal("Week", none[1].Label);
        Assert.Equal("seven_day", none[1].Key);      // aggregate fallback — never blank
        Assert.Equal(20, none[1].Window.Utilization);
    }

    [Fact]
    public void Empty_list_uses_aggregate_weekly()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64)), []);
        Assert.Equal("Week", t[1].Label);
        Assert.Equal("seven_day", t[1].Key);
    }

    [Fact]
    public void Explicit_single_family_labelled_Week()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64), ("sonnet", 2)), ["opus"]);
        Assert.Equal(2, t.Count);
        Assert.Equal("Week", t[1].Label);
        Assert.Equal("seven_day_opus", t[1].Key);
    }

    [Fact]
    public void Explicit_missing_family_falls_back_to_aggregate()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("sonnet", 2)), ["opus"]);  // opus absent
        Assert.Equal("Week", t[1].Label);
        Assert.Equal("seven_day", t[1].Key);
    }

    [Fact]
    public void Explicit_multiple_families_use_model_names()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64), ("sonnet", 2)), ["opus", "sonnet"]);
        Assert.Equal(3, t.Count);
        Assert.Equal(new[] { "Opus", "Sonnet" }, t.Skip(1).Select(w => w.Label).ToArray());
    }

    [Fact]
    public void Star_uses_aggregate_as_Week_plus_models_by_name()
    {
        var t = TrackedWindowResolver.Resolve(Snap(("opus", 64), ("sonnet", 2)), ["*"]);
        Assert.Equal("Week", t[1].Label);
        Assert.Equal("seven_day", t[1].Key);
        Assert.Contains(t, w => w.Label == "Opus");
        Assert.Contains(t, w => w.Label == "Sonnet");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter TrackedWindowResolverTests`
Expected: FAIL — `TrackedWindowResolver` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace ClaudeUsageMonitor.Core;

public static class TrackedWindowResolver
{
    private static readonly string[] TierOrder = ["opus", "sonnet", "haiku"];

    public static IReadOnlyList<TrackedWindow> Resolve(UsageSnapshot snapshot, IReadOnlyList<string> weeklyModels)
    {
        var result = new List<TrackedWindow> { new("Session", "five_hour", snapshot.FiveHour) };
        result.AddRange(ResolveWeekly(snapshot, weeklyModels));
        return result;
    }

    private static List<TrackedWindow> ResolveWeekly(UsageSnapshot s, IReadOnlyList<string> weeklyModels)
    {
        var aggregate = new TrackedWindow("Week", "seven_day", s.SevenDay);

        if (weeklyModels.Count == 0)
            return [aggregate];

        if (weeklyModels.Count == 1 && weeklyModels[0] == "highest")
        {
            foreach (var tier in TierOrder)
                if (s.WeeklyByModel.TryGetValue(tier, out var w))
                    return [new TrackedWindow("Week", "seven_day_" + tier, w)];
            return [aggregate];
        }

        var collected = new List<TrackedWindow>();
        if (weeklyModels.Count == 1 && weeklyModels[0] == "*")
        {
            collected.Add(aggregate);
            foreach (var kv in s.WeeklyByModel)
                collected.Add(new TrackedWindow(Title(kv.Key), "seven_day_" + kv.Key, kv.Value));
        }
        else
        {
            foreach (var key in weeklyModels)
            {
                if (key is "highest" or "*") continue;
                if (s.WeeklyByModel.TryGetValue(key, out var w))
                    collected.Add(new TrackedWindow(Title(key), "seven_day_" + key, w));
            }
        }

        if (collected.Count == 0)
            return [aggregate];                          // never blank
        if (collected.Count == 1)
            return [collected[0] with { Label = "Week" }];
        return collected;
    }

    private static string Title(string family)
        => family.Length == 0 ? family : char.ToUpperInvariant(family[0]) + family[1..];
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter TrackedWindowResolverTests`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/TrackedWindowResolver.cs tests/ClaudeUsageMonitor.Core.Tests/TrackedWindowResolverTests.cs
git commit -m "feat(core): resolve config.weeklyModels into tracked windows"
```

---

## Task 6: StatusCalculator

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/StatusCalculator.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/StatusCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class StatusCalculatorTests
{
    private static readonly DateTimeOffset R = DateTimeOffset.UnixEpoch;
    private static TrackedWindow W(double util) => new("X", "x", new UsageWindow(util, R));

    [Theory]
    [InlineData(0.0, Status.Green)]
    [InlineData(49.9, Status.Green)]
    [InlineData(50.0, Status.Yellow)]
    [InlineData(79.9, Status.Yellow)]
    [InlineData(80.0, Status.Orange)]
    [InlineData(94.9, Status.Orange)]
    [InlineData(95.0, Status.Red)]
    [InlineData(100.0, Status.Red)]
    public void FromUtilization_maps_boundaries(double util, Status expected)
        => Assert.Equal(expected, StatusCalculator.FromUtilization(util));

    [Fact]
    public void Compute_uses_max_across_tracked_windows()
        => Assert.Equal(Status.Orange, StatusCalculator.Compute([W(12), W(81), W(40)], isStale: false));

    [Fact]
    public void Stale_overrides_color_even_when_green()
        => Assert.Equal(Status.Stale, StatusCalculator.Compute([W(5)], isStale: true));

    [Fact]
    public void Compute_empty_is_green()
        => Assert.Equal(Status.Green, StatusCalculator.Compute([], isStale: false));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter StatusCalculatorTests`
Expected: FAIL — `StatusCalculator` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace ClaudeUsageMonitor.Core;

public static class StatusCalculator
{
    public const double YellowAt = 50.0;
    public const double OrangeAt = 80.0;
    public const double RedAt = 95.0;

    /// <summary>Stale overrides everything; otherwise color reflects the max utilization across tracked windows.</summary>
    public static Status Compute(IReadOnlyList<TrackedWindow> tracked, bool isStale)
    {
        if (isStale) return Status.Stale;
        double max = 0;
        foreach (var t in tracked)
            if (t.Window.Utilization > max) max = t.Window.Utilization;
        return FromUtilization(max);
    }

    public static Status FromUtilization(double util) => util switch
    {
        >= RedAt => Status.Red,
        >= OrangeAt => Status.Orange,
        >= YellowAt => Status.Yellow,
        _ => Status.Green,
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter StatusCalculatorTests`
Expected: PASS (11 cases).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/StatusCalculator.cs tests/ClaudeUsageMonitor.Core.Tests/StatusCalculatorTests.cs
git commit -m "feat(core): compute tray status from tracked windows"
```

---

## Task 7: ResetFormatter

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/ResetFormatter.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/ResetFormatterTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class ResetFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Under_an_hour_shows_minutes()
        => Assert.Equal("resets in 38m", ResetFormatter.Format(Now.AddMinutes(38), Now));

    [Fact]
    public void Within_a_day_shows_hours_and_minutes()
        => Assert.Equal("resets in 1h 48m", ResetFormatter.Format(Now.AddMinutes(108), Now));

    [Fact]
    public void Beyond_a_day_shows_weekday()
    {
        // 2026-06-10 is a Wednesday
        var resets = new DateTimeOffset(2026, 6, 10, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal("resets Wed", ResetFormatter.Format(resets, Now));
    }

    [Fact]
    public void Past_reset_shows_now()
        => Assert.Equal("resets now", ResetFormatter.Format(Now.AddMinutes(-5), Now));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ResetFormatterTests`
Expected: FAIL — `ResetFormatter` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Globalization;

namespace ClaudeUsageMonitor.Core;

public static class ResetFormatter
{
    public static string Format(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var span = resetsAt - now;
        if (span <= TimeSpan.Zero) return "resets now";
        if (span < TimeSpan.FromHours(1)) return $"resets in {(int)span.TotalMinutes}m";
        if (span < TimeSpan.FromHours(24)) return $"resets in {(int)span.TotalHours}h {span.Minutes}m";
        return $"resets {resetsAt.ToString("ddd", CultureInfo.InvariantCulture)}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter ResetFormatterTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/ResetFormatter.cs tests/ClaudeUsageMonitor.Core.Tests/ResetFormatterTests.cs
git commit -m "feat(core): format reset countdown text"
```

---

## Task 8: EtaProjector

Keeps a rolling history of `(timestamp, utilization)` **keyed on `TrackedWindow.Key`** and projects each window's slope to 100%, returning the soonest. Keying on `Key` (not `Label`) means the `["highest"]` opus→aggregate fallback does not splice two windows into one slope.

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/EtaProjector.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/EtaProjectorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class EtaProjectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset = T0.AddHours(3);

    private static IReadOnlyList<TrackedWindow> Tracked(string label, string key, double util)
        => [new TrackedWindow(label, key, new UsageWindow(util, Reset))];

    [Fact]
    public void Single_sample_yields_no_eta()
    {
        var p = new EtaProjector();
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 50), T0);
        Assert.Null(p.ProjectSoonest());
    }

    [Fact]
    public void Rising_slope_projects_eta()
    {
        var p = new EtaProjector();
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 50), T0);
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 60), T0.AddMinutes(10)); // +10%/10min => +60%/hr
        var eta = p.ProjectSoonest();
        Assert.NotNull(eta);
        Assert.Equal("Week", eta!.Value.Label);
        // remaining 40% at 60%/hr => 40 min
        Assert.Equal(40, eta.Value.Eta.TotalMinutes, precision: 1);
    }

    [Fact]
    public void Flat_or_declining_slope_yields_no_eta()
    {
        var p = new EtaProjector();
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 60), T0);
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 60), T0.AddMinutes(10));
        Assert.Null(p.ProjectSoonest());
    }

    [Fact]
    public void Soonest_window_wins()
    {
        var p = new EtaProjector();
        IReadOnlyList<TrackedWindow> a = [
            new("Session", "five_hour", new UsageWindow(50, Reset)),
            new("Week", "seven_day_opus", new UsageWindow(50, Reset))];
        IReadOnlyList<TrackedWindow> b = [
            new("Session", "five_hour", new UsageWindow(70, Reset)),   // +20%/10min, steeper
            new("Week", "seven_day_opus", new UsageWindow(55, Reset))];
        p.AddSnapshot(a, T0);
        p.AddSnapshot(b, T0.AddMinutes(10));
        Assert.Equal("Session", p.ProjectSoonest()!.Value.Label);
    }

    [Fact]
    public void Key_change_does_not_splice_slopes()
    {
        // opus rising fast, then opus disappears and Week falls back to aggregate at a low, flat value.
        var p = new EtaProjector();
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 80), T0);
        p.AddSnapshot(Tracked("Week", "seven_day_opus", 90), T0.AddMinutes(10));
        // fallback: same Label "Week", different Key — opus history is pruned, aggregate has one sample.
        p.AddSnapshot(Tracked("Week", "seven_day", 41), T0.AddMinutes(20));
        Assert.Null(p.ProjectSoonest());   // no garbage 90->41 slope
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter EtaProjectorTests`
Expected: FAIL — `EtaProjector` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace ClaudeUsageMonitor.Core;

public class EtaProjector
{
    private readonly TimeSpan _historyWindow;
    private readonly Dictionary<string, List<Sample>> _history = new();

    private readonly record struct Sample(DateTimeOffset At, double Util, string Label);

    public EtaProjector(TimeSpan? historyWindow = null)
        => _historyWindow = historyWindow ?? TimeSpan.FromMinutes(30);

    /// <summary>Record this poll's utilization for every tracked window, keyed on Key. Prunes windows no longer tracked and samples older than the history window.</summary>
    public void AddSnapshot(IReadOnlyList<TrackedWindow> tracked, DateTimeOffset now)
    {
        var keys = tracked.Select(t => t.Key).ToHashSet();
        foreach (var gone in _history.Keys.Where(k => !keys.Contains(k)).ToList())
            _history.Remove(gone);

        foreach (var t in tracked)
        {
            if (!_history.TryGetValue(t.Key, out var list))
                _history[t.Key] = list = new List<Sample>();
            list.Add(new Sample(now, t.Window.Utilization, t.Label));
            list.RemoveAll(s => now - s.At > _historyWindow);
        }
    }

    /// <summary>The soonest projected time-to-100% across tracked windows, or null if none is rising.</summary>
    public (TimeSpan Eta, string Label)? ProjectSoonest()
    {
        (TimeSpan Eta, string Label)? best = null;
        foreach (var samples in _history.Values)
        {
            if (samples.Count < 2) continue;
            var first = samples[0];
            var last = samples[^1];
            var hours = (last.At - first.At).TotalHours;
            if (hours <= 0) continue;
            var slope = (last.Util - first.Util) / hours;      // %/hr
            if (slope <= 0) continue;
            var remaining = 100.0 - last.Util;
            if (remaining <= 0) continue;
            var eta = TimeSpan.FromHours(remaining / slope);
            if (best is null || eta < best.Value.Eta)
                best = (eta, last.Label);
        }
        return best;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter EtaProjectorTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/EtaProjector.cs tests/ClaudeUsageMonitor.Core.Tests/EtaProjectorTests.cs
git commit -m "feat(core): project soonest ETA from utilization slope, keyed on window key"
```

---

## Task 9: JsonlActivityReader + scanner

Pure parsing/aggregation is TDD'd; the IO `JsonlActivityScanner` is a thin wrapper verified later via the running app.

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/JsonlActivityReader.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/JsonlActivityReaderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class JsonlActivityReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_usage_entries_and_skips_non_usage_lines()
    {
        var lines = new[]
        {
            """{"type":"user","timestamp":"2026-06-04T09:30:00+00:00","message":{"role":"user"}}""",
            """{"type":"assistant","timestamp":"2026-06-04T09:45:00+00:00","message":{"usage":{"input_tokens":100,"output_tokens":50,"cache_creation_input_tokens":10,"cache_read_input_tokens":40}}}""",
            "",
            "not json at all",
        };
        var entries = JsonlActivityReader.ParseLines(lines);
        Assert.Single(entries);
        Assert.Equal(200, entries[0].TotalTokens);    // 100+50+10+40
    }

    [Fact]
    public void TokensPerHour_sums_only_within_trailing_window()
    {
        var entries = new List<TokenEntry>
        {
            new(Now.AddMinutes(-30), 600),   // inside 1h window
            new(Now.AddMinutes(-90), 9999),  // outside
        };
        var rate = JsonlActivityReader.TokensPerHour(entries, Now, TimeSpan.FromHours(1));
        Assert.Equal(600, rate);             // 600 tokens / 1 hour
    }

    [Fact]
    public void TokensPerHour_empty_is_zero()
        => Assert.Equal(0, JsonlActivityReader.TokensPerHour([], Now, TimeSpan.FromHours(1)));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter JsonlActivityReaderTests`
Expected: FAIL — `JsonlActivityReader` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.Json;

namespace ClaudeUsageMonitor.Core;

public record TokenEntry(DateTimeOffset Timestamp, long TotalTokens);

public static class JsonlActivityReader
{
    public static IReadOnlyList<TokenEntry> ParseLines(IEnumerable<string> lines)
    {
        var entries = new List<TokenEntry>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = TryParseLine(line);
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }

    private static TokenEntry? TryParseLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.String) return null;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return null;
            if (!msg.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;

            long total = GetLong(usage, "input_tokens") + GetLong(usage, "output_tokens")
                       + GetLong(usage, "cache_creation_input_tokens") + GetLong(usage, "cache_read_input_tokens");
            return new TokenEntry(DateTimeOffset.Parse(ts.GetString()!), total);
        }
        catch (JsonException) { return null; }
    }

    private static long GetLong(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    public static double TokensPerHour(IReadOnlyList<TokenEntry> entries, DateTimeOffset now, TimeSpan window)
    {
        var cutoff = now - window;
        long sum = 0;
        foreach (var e in entries)
            if (e.Timestamp >= cutoff && e.Timestamp <= now) sum += e.TotalTokens;
        var hours = window.TotalHours;
        return hours <= 0 ? 0 : sum / hours;
    }
}

/// <summary>IO wrapper: scans recently-modified JSONL transcripts and computes a tokens/hour rate. Thin; verified via the running app.</summary>
public static class JsonlActivityScanner
{
    public static double TokensPerHour(string projectsRoot, DateTimeOffset now, TimeSpan window)
    {
        if (!Directory.Exists(projectsRoot)) return 0;
        var cutoff = now - window;
        var lines = new List<string>();
        foreach (var file in Directory.EnumerateFiles(projectsRoot, "*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                if (new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) < cutoff) continue;
                lines.AddRange(File.ReadLines(file));
            }
            catch (IOException) { /* file in use by Claude Code; skip this poll */ }
        }
        return JsonlActivityReader.TokensPerHour(JsonlActivityReader.ParseLines(lines), now, window);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter JsonlActivityReaderTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/JsonlActivityReader.cs tests/ClaudeUsageMonitor.Core.Tests/JsonlActivityReaderTests.cs
git commit -m "feat(core): read JSONL token usage and compute tokens/hour"
```

---

## Task 10: AlertEngine

Per-window threshold arming, **keyed on `TrackedWindow.Key`**. Each threshold fires once; re-arms when `resets_at` advances or utilization drops below the lowest threshold.

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/AlertEngine.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/AlertEngineTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class AlertEngineTests
{
    private static readonly DateTimeOffset R1 = new(2026, 6, 10, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset R2 = R1.AddDays(7);

    private static IReadOnlyList<TrackedWindow> One(string key, double util, DateTimeOffset reset)
        => [new TrackedWindow("Week", key, new UsageWindow(util, reset))];

    [Fact]
    public void Fires_each_threshold_once_as_it_is_crossed()
    {
        var e = new AlertEngine([80, 95]);
        Assert.Empty(e.Evaluate(One("seven_day", 70, R1)));

        var at80 = e.Evaluate(One("seven_day", 82, R1));
        Assert.Single(at80);
        Assert.Equal(80, at80[0].Threshold);
        Assert.Equal("Week", at80[0].WindowLabel);

        Assert.Empty(e.Evaluate(One("seven_day", 90, R1)));   // 80 already fired, 95 not reached

        var at95 = e.Evaluate(One("seven_day", 96, R1));
        Assert.Single(at95);
        Assert.Equal(95, at95[0].Threshold);

        Assert.Empty(e.Evaluate(One("seven_day", 99, R1)));   // both fired
    }

    [Fact]
    public void Re_arms_when_resets_at_advances()
    {
        var e = new AlertEngine([80, 95]);
        e.Evaluate(One("seven_day", 82, R1));                 // fires 80
        var afterReset = e.Evaluate(One("seven_day", 85, R2)); // new reset timestamp
        Assert.Single(afterReset);
        Assert.Equal(80, afterReset[0].Threshold);
    }

    [Fact]
    public void Re_arms_when_utilization_drops_below_lowest_threshold()
    {
        var e = new AlertEngine([80, 95]);
        e.Evaluate(One("seven_day", 82, R1));                 // fires 80
        Assert.Empty(e.Evaluate(One("seven_day", 10, R1)));   // dropped below 80 -> re-armed, but 10<80 so no fire
        Assert.Single(e.Evaluate(One("seven_day", 82, R1)));  // crosses again -> fires
    }

    [Fact]
    public void Windows_are_independent()
    {
        var e = new AlertEngine([80, 95]);
        IReadOnlyList<TrackedWindow> two = [
            new("Session", "five_hour", new UsageWindow(82, R1)),
            new("Week", "seven_day", new UsageWindow(50, R1))];
        var alerts = e.Evaluate(two);
        Assert.Single(alerts);
        Assert.Equal("Session", alerts[0].WindowLabel);
    }

    [Fact]
    public void Key_change_does_not_spuriously_refire()
    {
        // opus fires 80; then Week falls back to aggregate (different Key) at a low value.
        var e = new AlertEngine([80, 95]);
        Assert.Single(e.Evaluate(One("seven_day_opus", 82, R1)));    // opus fires 80
        Assert.Empty(e.Evaluate(One("seven_day", 41, R1)));          // aggregate is a fresh key at 41 — no fire, no refire
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AlertEngineTests`
Expected: FAIL — `AlertEngine` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace ClaudeUsageMonitor.Core;

public record UsageAlert(string WindowLabel, string WindowKey, int Threshold);

public class AlertEngine
{
    private readonly int[] _thresholds;     // ascending
    private readonly double _lowest;
    private readonly Dictionary<string, WindowState> _state = new();

    private sealed class WindowState
    {
        public DateTimeOffset LastResetsAt;
        public readonly HashSet<int> Fired = new();
    }

    public AlertEngine(IEnumerable<int> thresholds)
    {
        _thresholds = thresholds.OrderBy(t => t).ToArray();
        _lowest = _thresholds.Length > 0 ? _thresholds[0] : double.MaxValue;
    }

    public IReadOnlyList<UsageAlert> Evaluate(IReadOnlyList<TrackedWindow> tracked)
    {
        var alerts = new List<UsageAlert>();
        foreach (var t in tracked)
        {
            if (!_state.TryGetValue(t.Key, out var st))
            {
                _state[t.Key] = st = new WindowState { LastResetsAt = t.Window.ResetsAt };
            }
            else if (t.Window.ResetsAt > st.LastResetsAt || t.Window.Utilization < _lowest)
            {
                st.Fired.Clear();
                st.LastResetsAt = t.Window.ResetsAt;
            }

            foreach (var threshold in _thresholds)
                if (t.Window.Utilization >= threshold && st.Fired.Add(threshold))
                    alerts.Add(new UsageAlert(t.Label, t.Key, threshold));
        }
        return alerts;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AlertEngineTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/AlertEngine.cs tests/ClaudeUsageMonitor.Core.Tests/AlertEngineTests.cs
git commit -m "feat(core): per-window threshold alerting with re-arm on reset"
```

---

## Task 11: CredentialsReader & PlanLabelMapper

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/Credentials.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/CredentialsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class CredentialsTests
{
    [Fact]
    public void Reads_credentials_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, """
        { "claudeAiOauth": {
            "accessToken": "at-123", "refreshToken": "rt-456", "expiresAt": 1780000000000,
            "scopes": ["a"], "subscriptionType": "max", "rateLimitTier": "default_claude_max_5x" } }
        """);
        var c = CredentialsReader.Read(path);
        Assert.Equal("at-123", c.AccessToken);
        Assert.Equal("rt-456", c.RefreshToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1780000000000), c.ExpiresAt);
        Assert.Equal("max", c.SubscriptionType);
        Assert.Equal("default_claude_max_5x", c.RateLimitTier);
        File.Delete(path);
    }

    [Theory]
    [InlineData("default_claude_max_5x", "max", "Max 5x")]
    [InlineData("default_claude_max_20x", "max", "Max 20x")]
    [InlineData("default_claude_pro", "pro", "Pro")]
    [InlineData("some_future_tier", "max", "Max")]      // unknown tier -> capitalized subscriptionType
    [InlineData("", "", "Claude")]                      // nothing known -> generic
    public void Maps_plan_label(string tier, string sub, string expected)
        => Assert.Equal(expected, PlanLabelMapper.Map(tier, sub));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter CredentialsTests`
Expected: FAIL — `CredentialsReader` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.Json;

namespace ClaudeUsageMonitor.Core;

public record Credentials(
    string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt,
    string SubscriptionType, string RateLimitTier);

public static class CredentialsReader
{
    /// <summary>Reads ~/.claude/.credentials.json. Read-only — never writes, to avoid racing Claude Code's daemon.</summary>
    public static Credentials Read(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var o = doc.RootElement.GetProperty("claudeAiOauth");
        return new Credentials(
            Str(o, "accessToken"),
            Str(o, "refreshToken"),
            DateTimeOffset.FromUnixTimeMilliseconds(o.GetProperty("expiresAt").GetInt64()),
            Str(o, "subscriptionType"),
            Str(o, "rateLimitTier"));
    }

    private static string Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
}

public static class PlanLabelMapper
{
    private static readonly Dictionary<string, string> Known = new()
    {
        ["default_claude_max_5x"] = "Max 5x",
        ["default_claude_max_20x"] = "Max 20x",
        ["default_claude_pro"] = "Pro",
    };

    public static string Map(string rateLimitTier, string subscriptionType)
    {
        if (Known.TryGetValue(rateLimitTier, out var label)) return label;
        if (string.IsNullOrEmpty(subscriptionType)) return "Claude";
        return char.ToUpperInvariant(subscriptionType[0]) + subscriptionType[1..];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter CredentialsTests`
Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/Credentials.cs tests/ClaudeUsageMonitor.Core.Tests/CredentialsTests.cs
git commit -m "feat(core): read credentials and map plan label"
```

---

## Task 12: AuthProvider

Reads credentials live each call. Valid future token → use it. Expired → try `ITokenRefresher` (v1 stub always fails) → `Stale` (null token). Time via `TimeProvider`.

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/AuthProvider.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/AuthProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;
using Microsoft.Extensions.Time.Testing;

namespace ClaudeUsageMonitor.Core.Tests;

public class AuthProviderTests
{
    private sealed class FakeRefresher(string? token) : ITokenRefresher
    {
        public Task<string?> RefreshAsync(string refreshToken, CancellationToken ct) => Task.FromResult(token);
    }

    private static string WriteCreds(long expiresAtMs)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, $$"""
        { "claudeAiOauth": { "accessToken": "live-token", "refreshToken": "rt",
            "expiresAt": {{expiresAtMs}}, "subscriptionType": "max", "rateLimitTier": "default_claude_max_5x" } }
        """);
        return path;
    }

    [Fact]
    public async Task Valid_token_is_returned()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var future = time.GetUtcNow().AddHours(2).ToUnixTimeMilliseconds();
        var auth = new AuthProvider(WriteCreds(future), new StubTokenRefresher(), time);
        var result = await auth.GetTokenAsync();
        Assert.True(result.IsValid);
        Assert.Equal("live-token", result.AccessToken);
        Assert.Equal("Max 5x", result.PlanLabel);
    }

    [Fact]
    public async Task Expired_token_with_successful_refresh_returns_new_token()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var past = time.GetUtcNow().AddHours(-1).ToUnixTimeMilliseconds();
        var auth = new AuthProvider(WriteCreds(past), new FakeRefresher("refreshed-token"), time);
        var result = await auth.GetTokenAsync();
        Assert.True(result.IsValid);
        Assert.Equal("refreshed-token", result.AccessToken);
    }

    [Fact]
    public async Task Expired_token_with_failed_refresh_is_stale()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var past = time.GetUtcNow().AddHours(-1).ToUnixTimeMilliseconds();
        var auth = new AuthProvider(WriteCreds(past), new StubTokenRefresher(), time);
        var result = await auth.GetTokenAsync();
        Assert.False(result.IsValid);
        Assert.Null(result.AccessToken);
        Assert.Equal("Max 5x", result.PlanLabel);   // label still known from credentials
    }

    [Fact]
    public async Task Missing_credentials_file_is_stale()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var auth = new AuthProvider(Path.Combine(Path.GetTempPath(), "does-not-exist.json"),
            new StubTokenRefresher(), time);
        var result = await auth.GetTokenAsync();
        Assert.False(result.IsValid);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AuthProviderTests`
Expected: FAIL — `AuthProvider` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace ClaudeUsageMonitor.Core;

public record AuthResult(string? AccessToken, string PlanLabel)
{
    public bool IsValid => AccessToken is not null;
}

public interface ITokenRefresher
{
    Task<string?> RefreshAsync(string refreshToken, CancellationToken ct);
}

/// <summary>v1: the OAuth refresh endpoint is not yet known (spec §7 open detail). Always fails → Stale fallback.</summary>
public sealed class StubTokenRefresher : ITokenRefresher
{
    public Task<string?> RefreshAsync(string refreshToken, CancellationToken ct) => Task.FromResult<string?>(null);
}

public class AuthProvider
{
    private readonly string _credentialsPath;
    private readonly ITokenRefresher _refresher;
    private readonly TimeProvider _time;

    public AuthProvider(string credentialsPath, ITokenRefresher refresher, TimeProvider time)
        => (_credentialsPath, _refresher, _time) = (credentialsPath, refresher, time);

    public async Task<AuthResult> GetTokenAsync(CancellationToken ct = default)
    {
        Credentials creds;
        try { creds = CredentialsReader.Read(_credentialsPath); }
        catch { return new AuthResult(null, "Claude"); }

        var label = PlanLabelMapper.Map(creds.RateLimitTier, creds.SubscriptionType);
        if (creds.ExpiresAt > _time.GetUtcNow())
            return new AuthResult(creds.AccessToken, label);

        // Refreshed token is held in memory only — never written back to .credentials.json.
        var refreshed = await _refresher.RefreshAsync(creds.RefreshToken, ct);
        return new AuthResult(refreshed, label);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AuthProviderTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/AuthProvider.cs tests/ClaudeUsageMonitor.Core.Tests/AuthProviderTests.cs
git commit -m "feat(core): auth provider with in-memory refresh stub and stale fallback"
```

---

## Task 13: HttpUsageApiClient

The test asserts the **outbound request headers** (`Authorization: Bearer`, `anthropic-beta`), not just response parsing — a wrong header is a silent 401 → permanent Stale in production.

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/UsageApiClient.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/HttpUsageApiClientTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class HttpUsageApiClientTests
{
    private sealed class CapturingHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Captured = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task Sends_required_headers_and_returns_body()
    {
        var handler = new CapturingHandler("""{"five_hour":{}}""", HttpStatusCode.OK);
        var client = new HttpUsageApiClient(new HttpClient(handler));

        var body = await client.GetUsageJsonAsync("tok-789");

        Assert.Equal("""{"five_hour":{}}""", body);
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", handler.Captured!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Captured.Headers.Authorization!.Scheme);
        Assert.Equal("tok-789", handler.Captured.Headers.Authorization.Parameter);
        Assert.True(handler.Captured.Headers.TryGetValues("anthropic-beta", out var beta));
        Assert.Equal("oauth-2025-04-20", beta!.Single());
    }

    [Fact]
    public async Task Non_success_status_throws()
    {
        var handler = new CapturingHandler("nope", HttpStatusCode.Unauthorized);
        var client = new HttpUsageApiClient(new HttpClient(handler));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetUsageJsonAsync("tok"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter HttpUsageApiClientTests`
Expected: FAIL — `HttpUsageApiClient` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Net.Http.Headers;

namespace ClaudeUsageMonitor.Core;

public interface IUsageApiClient
{
    Task<string> GetUsageJsonAsync(string accessToken, CancellationToken ct = default);
}

public class HttpUsageApiClient : IUsageApiClient
{
    public const string Url = "https://api.anthropic.com/api/oauth/usage";
    private readonly HttpClient _http;

    public HttpUsageApiClient(HttpClient http) => _http = http;

    public async Task<string> GetUsageJsonAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter HttpUsageApiClientTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/UsageApiClient.cs tests/ClaudeUsageMonitor.Core.Tests/HttpUsageApiClientTests.cs
git commit -m "feat(core): HTTP usage API client with auth headers"
```

---

## Task 14: UsagePoller

Orchestrates auth → fetch → parse into a `PollResult` (Ok or Stale). `PollOnceAsync` holds all logic and is fully tested. The cadence timer uses `TimeProvider.CreateTimer`, so the timing is testable too.

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/UsagePoller.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/UsagePollerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;
using Microsoft.Extensions.Time.Testing;

namespace ClaudeUsageMonitor.Core.Tests;

public class UsagePollerTests
{
    private const string Body = """
    { "five_hour": { "utilization": 13.0, "resets_at": "2026-06-04T20:30:00+10:00" },
      "seven_day": { "utilization": 20.0, "resets_at": "2026-06-10T18:00:00+10:00" } }
    """;

    private sealed class FakeClient(string? body, Exception? throwOnCall = null) : IUsageApiClient
    {
        public Task<string> GetUsageJsonAsync(string accessToken, CancellationToken ct = default)
            => throwOnCall is not null ? Task.FromException<string>(throwOnCall) : Task.FromResult(body!);
    }

    private static AuthProvider ValidAuth(FakeTimeProvider time)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var future = time.GetUtcNow().AddHours(2).ToUnixTimeMilliseconds();
        File.WriteAllText(path, $$"""
        { "claudeAiOauth": { "accessToken": "t", "refreshToken": "r", "expiresAt": {{future}},
            "subscriptionType": "max", "rateLimitTier": "default_claude_max_5x" } }
        """);
        return new AuthProvider(path, new StubTokenRefresher(), time);
    }

    [Fact]
    public async Task PollOnce_returns_Ok_snapshot()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var poller = new UsagePoller(ValidAuth(time), new FakeClient(Body), time, () => TimeSpan.FromSeconds(60));
        var result = await poller.PollOnceAsync();
        var ok = Assert.IsType<PollResult.Ok>(result);
        Assert.Equal(13.0, ok.Snapshot.FiveHour.Utilization);
        Assert.Equal("Max 5x", ok.Snapshot.PlanLabel);
    }

    [Fact]
    public async Task PollOnce_is_Stale_when_auth_invalid()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var badAuth = new AuthProvider("missing.json", new StubTokenRefresher(), time);
        var poller = new UsagePoller(badAuth, new FakeClient(Body), time, () => TimeSpan.FromSeconds(60));
        Assert.IsType<PollResult.Stale>(await poller.PollOnceAsync());
    }

    [Fact]
    public async Task PollOnce_is_Stale_when_client_throws()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var poller = new UsagePoller(ValidAuth(time), new FakeClient(null, new HttpRequestException("offline")),
            time, () => TimeSpan.FromSeconds(60));
        Assert.IsType<PollResult.Stale>(await poller.PollOnceAsync());
    }

    [Fact]
    public async Task Timer_fires_polls_on_interval()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero));
        var poller = new UsagePoller(ValidAuth(time), new FakeClient(Body), time, () => TimeSpan.FromSeconds(60));
        var count = 0;
        var gate = new TaskCompletionSource();
        poller.Polled += _ => { if (Interlocked.Increment(ref count) >= 2) gate.TrySetResult(); };

        poller.Start();                          // dueTime 0, period 60s
        time.Advance(TimeSpan.FromSeconds(60));  // at least one periodic fire
        time.Advance(TimeSpan.FromSeconds(60));  // another — count reaches >= 2 regardless of zero-due timing
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(count >= 2);
        poller.Dispose();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter UsagePollerTests`
Expected: FAIL — `UsagePoller` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace ClaudeUsageMonitor.Core;

public abstract record PollResult
{
    public sealed record Ok(UsageSnapshot Snapshot) : PollResult;
    public sealed record Stale(string Reason) : PollResult;
}

public class UsagePoller : IDisposable
{
    private readonly AuthProvider _auth;
    private readonly IUsageApiClient _client;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan> _interval;
    private ITimer? _timer;

    public event Action<PollResult>? Polled;

    public UsagePoller(AuthProvider auth, IUsageApiClient client, TimeProvider time, Func<TimeSpan> interval)
        => (_auth, _client, _time, _interval) = (auth, client, time, interval);

    public async Task<PollResult> PollOnceAsync(CancellationToken ct = default)
    {
        var auth = await _auth.GetTokenAsync(ct);
        if (!auth.IsValid) return new PollResult.Stale("auth");

        string json;
        try { json = await _client.GetUsageJsonAsync(auth.AccessToken!, ct); }
        catch (Exception ex) { return new PollResult.Stale(ex.GetType().Name); }

        try { return new PollResult.Ok(UsageApiParser.Parse(json, auth.PlanLabel, _time.GetUtcNow())); }
        catch (Exception ex) { return new PollResult.Stale(ex.GetType().Name); }
    }

    public void Start()
        => _timer = _time.CreateTimer(_ => _ = PollAndPublishAsync(), null, TimeSpan.Zero, _interval());

    public Task TriggerAsync() => PollAndPublishAsync();

    private async Task PollAndPublishAsync()
    {
        var result = await PollOnceAsync();   // never throws; returns Stale on failure
        Polled?.Invoke(result);
    }

    public void Dispose() => _timer?.Dispose();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter UsagePollerTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/UsagePoller.cs tests/ClaudeUsageMonitor.Core.Tests/UsagePollerTests.cs
git commit -m "feat(core): usage poller with on-interval and on-demand polling"
```

---

## Task 15: Debouncer & JsonlWatcher

`Debouncer` (coalesces rapid triggers, time via `TimeProvider`) is TDD'd. `JsonlWatcher` is a thin `FileSystemWatcher` wrapper, verified later via the running app.

**Files:**
- Create: `src/ClaudeUsageMonitor.Core/Debouncer.cs`, `src/ClaudeUsageMonitor.Core/JsonlWatcher.cs`
- Test: `tests/ClaudeUsageMonitor.Core.Tests/DebouncerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClaudeUsageMonitor.Core;
using Microsoft.Extensions.Time.Testing;

namespace ClaudeUsageMonitor.Core.Tests;

public class DebouncerTests
{
    [Fact]
    public void Rapid_triggers_collapse_to_one_fire()
    {
        var time = new FakeTimeProvider();
        var fires = 0;
        var d = new Debouncer(time, TimeSpan.FromSeconds(2), () => fires++);

        d.Trigger();
        time.Advance(TimeSpan.FromSeconds(1));
        d.Trigger();                              // resets the window
        time.Advance(TimeSpan.FromSeconds(1));    // 1s since last trigger — not yet
        Assert.Equal(0, fires);
        time.Advance(TimeSpan.FromSeconds(1));    // 2s of quiet — fires once
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Fires_again_after_a_new_trigger()
    {
        var time = new FakeTimeProvider();
        var fires = 0;
        var d = new Debouncer(time, TimeSpan.FromSeconds(2), () => fires++);

        d.Trigger();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, fires);
        d.Trigger();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, fires);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter DebouncerTests`
Expected: FAIL — `Debouncer` does not exist.

- [ ] **Step 3: Write the Debouncer implementation**

```csharp
namespace ClaudeUsageMonitor.Core;

/// <summary>Coalesces bursts of Trigger() calls into a single action fired after a quiet period.</summary>
public sealed class Debouncer : IDisposable
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _delay;
    private readonly Action _action;
    private readonly object _lock = new();
    private ITimer? _timer;

    public Debouncer(TimeProvider time, TimeSpan delay, Action action)
        => (_time, _delay, _action) = (time, delay, action);

    public void Trigger()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = _time.CreateTimer(_ => _action(), null, _delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose() { lock (_lock) _timer?.Dispose(); }
}
```

- [ ] **Step 4: Write the JsonlWatcher (no test — thin IO wrapper)**

```csharp
namespace ClaudeUsageMonitor.Core;

/// <summary>Watches ~/.claude/projects/**/*.jsonl for changes and fires a debounced activity callback.</summary>
public sealed class JsonlWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Debouncer _debouncer;

    public JsonlWatcher(string projectsRoot, TimeProvider time, TimeSpan debounce, Action onActivity)
    {
        _debouncer = new Debouncer(time, debounce, onActivity);
        _watcher = new FileSystemWatcher(projectsRoot, "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => _debouncer.Trigger();
        _watcher.Created += (_, _) => _debouncer.Trigger();
    }

    public void Dispose() { _watcher.Dispose(); _debouncer.Dispose(); }
}
```

- [ ] **Step 5: Run tests to verify they pass and the project builds**

Run: `dotnet test --filter DebouncerTests`
Expected: PASS (2 tests).
Run: `dotnet build src/ClaudeUsageMonitor.Core`
Expected: Build succeeded (JsonlWatcher compiles).

- [ ] **Step 6: Commit**

```powershell
git add src/ClaudeUsageMonitor.Core/Debouncer.cs src/ClaudeUsageMonitor.Core/JsonlWatcher.cs tests/ClaudeUsageMonitor.Core.Tests/DebouncerTests.cs
git commit -m "feat(core): debouncer and JSONL activity watcher"
```

- [ ] **Step 7: Full Core test sweep**

Run: `dotnet test`
Expected: PASS — all Core tests green (this is the last Core task; everything downstream is WPF/manual).

---

> **App layer (Tasks 16–21) is verified manually**, per spec §9 ("Core logic unit-tested"). Each task lists explicit run-and-observe steps instead of automated assertions.

## Task 16: Tray shell, icon renderer, startup registry

Produces a runnable app: a grey tray icon with a working right-click menu (only **Quit** is wired; other items are wired in later tasks). `TrayIcon`, `IconRenderer`, and `StartupRegistry` are written in full here; later tasks only attach event handlers.

**Files:**
- Create: `src/ClaudeUsageMonitor.App/IconRenderer.cs`, `src/ClaudeUsageMonitor.App/TrayIcon.cs`, `src/ClaudeUsageMonitor.App/StartupRegistry.cs`
- Modify: `src/ClaudeUsageMonitor.App/App.xaml`, `src/ClaudeUsageMonitor.App/App.xaml.cs`
- Delete: `src/ClaudeUsageMonitor.App/MainWindow.xaml`, `src/ClaudeUsageMonitor.App/MainWindow.xaml.cs`

- [ ] **Step 1: Remove the default main window**

```powershell
Remove-Item src/ClaudeUsageMonitor.App/MainWindow.xaml, src/ClaudeUsageMonitor.App/MainWindow.xaml.cs
```

- [ ] **Step 2: Rewrite `App.xaml` (no startup window; explicit shutdown)**

```xml
<Application x:Class="ClaudeUsageMonitor.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources />
</Application>
```

- [ ] **Step 3: Write `IconRenderer.cs`**

```csharp
using System.Drawing;
using System.Runtime.InteropServices;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

/// <summary>Renders a flat colored-circle tray icon for a Status. Caller owns the returned Icon and must dispose the previous one.</summary>
public static partial class IconRenderer
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);

    public static Icon Render(Status status)
    {
        var color = status switch
        {
            Status.Green => Color.FromArgb(0x3F, 0xB9, 0x50),
            Status.Yellow => Color.FromArgb(0xE6, 0xC2, 0x29),
            Status.Orange => Color.FromArgb(0xE6, 0x8A, 0x00),
            Status.Red => Color.FromArgb(0xD9, 0x3A, 0x2B),
            _ => Color.FromArgb(0x9E, 0x9E, 0x9E),    // Stale = grey
        };

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 4, 4, 24, 24);
        }

        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }   // Clone owns its own handle...
        finally { DestroyIcon(handle); }                        // ...so the GDI handle can be freed.
    }
}
```

- [ ] **Step 4: Write `StartupRegistry.cs`**

```csharp
using Microsoft.Win32;

namespace ClaudeUsageMonitor.App;

/// <summary>Per-user "start with Windows" via HKCU\...\Run.</summary>
public static class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeUsageMonitor";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 5: Write `TrayIcon.cs`**

```csharp
using System.Windows.Forms;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _startupItem;
    private System.Drawing.Icon? _currentIcon;

    public event Action? ToggleWidget;
    public event Action? RefreshNow;
    public event Action? ToggleStartup;
    public event Action? OpenSettings;
    public event Action? Quit;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show/Hide widget", null, (_, _) => ToggleWidget?.Invoke());
        menu.Items.Add("Refresh now", null, (_, _) => RefreshNow?.Invoke());
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup?.Invoke());
        menu.Items.Add(_startupItem);
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

    public void ShowToast(string title, string text)
        => _notify.ShowBalloonTip(5000, title, text, ToolTipIcon.Warning);

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
        _currentIcon?.Dispose();
    }
}
```

- [ ] **Step 6: Rewrite `App.xaml.cs` (minimal lifecycle; full wiring added in Task 17)**

```csharp
using System.Windows;

namespace ClaudeUsageMonitor.App;

public partial class App : Application
{
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _tray = new TrayIcon();
        _tray.Quit += Shutdown;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 7: Build, run, and verify the tray icon**

Run: `dotnet build src/ClaudeUsageMonitor.App`
Expected: Build succeeded.
Run: `dotnet run --project src/ClaudeUsageMonitor.App`
Manually verify:
- A grey circular icon appears in the system tray (notification area).
- Right-click shows the menu: Show/Hide widget, Refresh now, Start with Windows, ─, Settings, Quit.
- Clicking **Quit** exits the process (the `dotnet run` command returns).

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "feat(app): tray icon shell, status icon renderer, startup registry"
```

---

## Task 17: Composition root — wire Core to the tray

Introduces `MonitorController`, which owns every Core component, polls, and raises a `MonitorView` (status + rows + ETA) plus `Alert` events. `App.xaml.cs` builds it, marshals its events to the WPF dispatcher, and updates the tray color + tooltip.

**Files:**
- Create: `src/ClaudeUsageMonitor.App/MonitorView.cs`, `src/ClaudeUsageMonitor.App/MonitorController.cs`
- Modify: `src/ClaudeUsageMonitor.App/App.xaml.cs`

- [ ] **Step 1: Write `MonitorView.cs` (the UI-facing snapshot)**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public record WindowRow(string Label, double Utilization, string ResetText);

public record MonitorView(
    Status Status,
    string PlanLabel,
    IReadOnlyList<WindowRow> Rows,
    BurnEstimate? Burn,
    bool IsStale,
    string StaleText);
```

- [ ] **Step 2: Write `MonitorController.cs`**

```csharp
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public sealed class MonitorController : IDisposable
{
    private readonly MonitorConfig _config;
    private readonly TimeProvider _time;
    private readonly string _projectsRoot;

    private readonly UsagePoller _poller;
    private readonly EtaProjector _eta = new();
    private readonly AlertEngine _alerts;
    private readonly JsonlWatcher? _watcher;
    private readonly HttpClient _http = new();

    private UsageSnapshot? _lastSnapshot;
    private DateTimeOffset _lastGood;

    public event Action<MonitorView>? Updated;
    public event Action<string, string>? Alert;

    public MonitorController(string credentialsPath, string projectsRoot, MonitorConfig config, TimeProvider time)
    {
        _config = config;
        _time = time;
        _projectsRoot = projectsRoot;

        var auth = new AuthProvider(credentialsPath, new StubTokenRefresher(), time);
        var client = new HttpUsageApiClient(_http);
        _poller = new UsagePoller(auth, client, time, () => TimeSpan.FromSeconds(_config.PollIntervalSeconds));
        _alerts = new AlertEngine(_config.AlertThresholds);
        _poller.Polled += OnPolled;

        if (Directory.Exists(projectsRoot))
            _watcher = new JsonlWatcher(projectsRoot, time, TimeSpan.FromSeconds(2), () => _ = _poller.TriggerAsync());
    }

    public void Start() => _poller.Start();

    public void RefreshNow() => _ = _poller.TriggerAsync();

    private void OnPolled(PollResult result)
    {
        var now = _time.GetUtcNow();
        switch (result)
        {
            case PollResult.Ok ok:
                _lastSnapshot = ok.Snapshot;
                _lastGood = now;
                var tracked = TrackedWindowResolver.Resolve(ok.Snapshot, _config.WeeklyModels);
                _eta.AddSnapshot(tracked, now);
                foreach (var alert in _alerts.Evaluate(tracked))
                    Alert?.Invoke($"Claude Usage — {alert.WindowLabel} at {alert.Threshold}%",
                                  $"{alert.WindowLabel} usage has reached {alert.Threshold}%.");
                Updated?.Invoke(BuildView(tracked, ok.Snapshot.PlanLabel, now, BuildBurn(now), isStale: false));
                break;

            case PollResult.Stale:
                Updated?.Invoke(BuildStaleView(now));
                break;
        }
    }

    private BurnEstimate? BuildBurn(DateTimeOffset now)
    {
        var tokensPerHour = JsonlActivityScanner.TokensPerHour(_projectsRoot, now, TimeSpan.FromHours(1));
        var eta = _eta.ProjectSoonest();
        if (eta is null && tokensPerHour <= 0) return null;     // idle and no projection -> hide burn line
        return new BurnEstimate(tokensPerHour, eta?.Eta, eta?.Label);
    }

    private MonitorView BuildView(IReadOnlyList<TrackedWindow> tracked, string plan, DateTimeOffset now,
        BurnEstimate? burn, bool isStale)
    {
        var rows = tracked
            .Select(t => new WindowRow(t.Label, t.Window.Utilization, ResetFormatter.Format(t.Window.ResetsAt, now)))
            .ToList();
        var status = StatusCalculator.Compute(tracked, isStale);
        return new MonitorView(status, plan, rows, burn, isStale, isStale ? StaleText(now) : "");
    }

    private MonitorView BuildStaleView(DateTimeOffset now)
    {
        if (_lastSnapshot is null)
            return new MonitorView(Status.Stale, "Claude", [], null, true, "no data");
        var tracked = TrackedWindowResolver.Resolve(_lastSnapshot, _config.WeeklyModels);
        return BuildView(tracked, _lastSnapshot.PlanLabel, now, burn: null, isStale: true);
    }

    private string StaleText(DateTimeOffset now)
    {
        var mins = (int)(now - _lastGood).TotalMinutes;
        return $"stale · {mins}m ago";
    }

    public void Dispose()
    {
        _poller.Dispose();
        _watcher?.Dispose();
        _http.Dispose();
    }
}
```

- [ ] **Step 3: Rewrite `App.xaml.cs` to build and wire the controller**

```csharp
using System.Windows;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public partial class App : Application
{
    private TrayIcon? _tray;
    private MonitorController? _controller;
    private ConfigStore? _configStore;
    private MonitorConfig? _config;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var claudeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var credentialsPath = Path.Combine(claudeDir, ".credentials.json");
        var projectsRoot = Path.Combine(claudeDir, "projects");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        _configStore = new ConfigStore(configPath);
        _config = _configStore.Load();

        _tray = new TrayIcon();
        _tray.Quit += Shutdown;
        _tray.RefreshNow += () => _controller?.RefreshNow();

        _controller = new MonitorController(credentialsPath, projectsRoot, _config, TimeProvider.System);
        _controller.Updated += OnViewUpdated;
        _controller.Start();
    }

    private void OnViewUpdated(MonitorView view)
        => Dispatcher.Invoke(() => _tray?.Update(view.Status, BuildTooltip(view)));

    private static string BuildTooltip(MonitorView view)
    {
        if (view.Rows.Count == 0) return "Claude Usage — no data";
        var parts = view.Rows.Select(r => $"{r.Label} {r.Utilization:0}% · {r.ResetText}");
        var text = string.Join(" | ", parts);
        return view.IsStale ? $"{text}  ({view.StaleText})" : text;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: Build, run, and verify live data**

Run: `dotnet build src/ClaudeUsageMonitor.App`
Expected: Build succeeded.
Run: `dotnet run --project src/ClaudeUsageMonitor.App`
Manually verify (requires a logged-in Claude Code on this machine):
- Within ~2s the tray icon changes from grey to a color (green/yellow/orange/red) reflecting current usage.
- Hovering the icon shows a tooltip like `Session 13% · resets in 6h 30m | Week 20% · resets Wed`.
- The percentages match `/usage` output in Claude Code.
- Right-click → **Refresh now** re-polls (tooltip timestamp/values refresh).
- If you rename `~/.claude/.credentials.json` temporarily, within a minute the icon greys out and the tooltip shows `(stale · Nm ago)` while keeping last-known values. Restore the file afterward.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(app): composition root polling Core into tray color and tooltip"
```

---

## Task 18: Widget window

A frameless, always-on-top, semi-transparent, draggable widget. Rows are built from `MonitorView.Rows` (so it shows exactly the tracked windows), with a progress bar + reset text each, and the ETA/stale line at the bottom. Position and opacity come from config; position is saved when the widget is hidden or the app exits.

**Files:**
- Create: `src/ClaudeUsageMonitor.App/WidgetWindow.xaml`, `src/ClaudeUsageMonitor.App/WidgetWindow.xaml.cs`
- Modify: `src/ClaudeUsageMonitor.App/App.xaml.cs`

- [ ] **Step 1: Write `WidgetWindow.xaml`**

```xml
<Window x:Class="ClaudeUsageMonitor.App.WidgetWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        SizeToContent="WidthAndHeight" Width="280">
    <Border CornerRadius="10" Background="#E61E1E1E" Padding="14">
        <StackPanel>
            <Grid x:Name="HeaderBar" Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock x:Name="PlanText" Grid.Column="0" Foreground="#EEEEEE"
                           FontWeight="SemiBold" Text="Claude" VerticalAlignment="Center"/>
                <Button x:Name="RefreshButton" Grid.Column="1" Content="⟳" Width="22" Height="22"
                        Background="Transparent" Foreground="#CCCCCC" BorderThickness="0" Cursor="Hand"/>
                <Button x:Name="CloseButton" Grid.Column="2" Content="✕" Width="22" Height="22"
                        Background="Transparent" Foreground="#CCCCCC" BorderThickness="0" Cursor="Hand"/>
            </Grid>
            <StackPanel x:Name="RowsPanel"/>
            <TextBlock x:Name="EtaText" Margin="0,8,0,0" Visibility="Collapsed" TextWrapping="Wrap"/>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 2: Write `WidgetWindow.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

public partial class WidgetWindow : Window
{
    public event Action? RefreshRequested;

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
        CloseButton.Click += (_, _) => Hide();
    }

    public void Render(MonitorView view)
    {
        PlanText.Text = $"Claude  ·  {view.PlanLabel}";
        RowsPanel.Children.Clear();
        foreach (var row in view.Rows)
            RowsPanel.Children.Add(BuildRow(row, view.IsStale));

        if (view.IsStale && view.Rows.Count > 0)
        {
            EtaText.Text = view.StaleText;
            EtaText.Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
            EtaText.Visibility = Visibility.Visible;
        }
        else if (!view.IsStale && view.Burn is not null)
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
        return $"≈ {burn.TokensPerHour / 1000.0:0.0}k tok/hr";   // active but no projection
    }

    private static UIElement BuildRow(WindowRow row, bool stale)
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
```

- [ ] **Step 3: Rewrite `App.xaml.cs` to create, toggle, and render the widget**

```csharp
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var claudeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var credentialsPath = Path.Combine(claudeDir, ".credentials.json");
        var projectsRoot = Path.Combine(claudeDir, "projects");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        _configStore = new ConfigStore(configPath);
        _config = _configStore.Load();

        _widget = new WidgetWindow(_config.WidgetOpacity, _config.WidgetPosition.X, _config.WidgetPosition.Y);
        _widget.RefreshRequested += () => _controller?.RefreshNow();

        _tray = new TrayIcon();
        _tray.Quit += Shutdown;
        _tray.RefreshNow += () => _controller?.RefreshNow();
        _tray.ToggleWidget += ToggleWidget;

        _controller = new MonitorController(credentialsPath, projectsRoot, _config, TimeProvider.System);
        _controller.Updated += OnViewUpdated;
        _controller.Start();
    }

    private void ToggleWidget()
    {
        if (_widget is null) return;
        if (_widget.IsVisible)
        {
            SaveWidgetPosition();
            _widget.Hide();
        }
        else
        {
            _widget.Show();
            _widget.Activate();
        }
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

    private static string BuildTooltip(MonitorView view)
    {
        if (view.Rows.Count == 0) return "Claude Usage — no data";
        var parts = view.Rows.Select(r => $"{r.Label} {r.Utilization:0}% · {r.ResetText}");
        var text = string.Join(" | ", parts);
        return view.IsStale ? $"{text}  ({view.StaleText})" : text;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveWidgetPosition();
        _controller?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: Build, run, and verify the widget**

Run: `dotnet build src/ClaudeUsageMonitor.App`
Expected: Build succeeded.
Run: `dotnet run --project src/ClaudeUsageMonitor.App`
Manually verify:
- App starts with no visible window (tray only).
- Left-click the tray icon (or right-click → Show/Hide widget) → a dark, semi-transparent rounded widget appears showing `Claude · Max 5x` (or your plan), a **Session** row and a **Week** row, each with a percentage, progress bar, and "resets …" text.
- Dragging the header moves the widget; the `✕` button hides it; `⟳` refreshes.
- Toggle hidden, then quit and relaunch → the widget reopens at the same position.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(app): draggable always-on-top usage widget"
```

---

## Task 19: Start-with-Windows toggle, Settings, first-run config

Wires the remaining tray menu items. On first run (no `config.json`), the file is written and — since `startWithWindows` defaults true — a startup entry is registered. The menu checkmark reflects the actual registry state; toggling flips both the registry and config.

**Files:**
- Modify: `src/ClaudeUsageMonitor.App/App.xaml.cs`

- [ ] **Step 1: Rewrite `App.xaml.cs` with startup + settings wiring**

```csharp
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
    private string _configPath = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var claudeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var credentialsPath = Path.Combine(claudeDir, ".credentials.json");
        var projectsRoot = Path.Combine(claudeDir, "projects");
        _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        var firstRun = !File.Exists(_configPath);
        _configStore = new ConfigStore(_configPath);
        _config = _configStore.Load();
        if (firstRun)
        {
            _configStore.Save(_config);                 // materialize defaults for the user to edit
            if (_config.StartWithWindows) StartupRegistry.Set(true);
        }

        _widget = new WidgetWindow(_config.WidgetOpacity, _config.WidgetPosition.X, _config.WidgetPosition.Y);
        _widget.RefreshRequested += () => _controller?.RefreshNow();

        _tray = new TrayIcon();
        _tray.Quit += Shutdown;
        _tray.RefreshNow += () => _controller?.RefreshNow();
        _tray.ToggleWidget += ToggleWidget;
        _tray.ToggleStartup += ToggleStartup;
        _tray.OpenSettings += OpenSettings;
        _tray.SetStartupChecked(StartupRegistry.IsEnabled());

        _controller = new MonitorController(credentialsPath, projectsRoot, _config, TimeProvider.System);
        _controller.Updated += OnViewUpdated;
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

    private void OpenSettings()
    {
        if (_configStore is null || _config is null) return;
        if (!File.Exists(_configPath)) _configStore.Save(_config);
        Process.Start(new ProcessStartInfo(_configPath) { UseShellExecute = true });
    }

    private void ToggleWidget()
    {
        if (_widget is null) return;
        if (_widget.IsVisible) { SaveWidgetPosition(); _widget.Hide(); }
        else { _widget.Show(); _widget.Activate(); }
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

    private static string BuildTooltip(MonitorView view)
    {
        if (view.Rows.Count == 0) return "Claude Usage — no data";
        var parts = view.Rows.Select(r => $"{r.Label} {r.Utilization:0}% · {r.ResetText}");
        var text = string.Join(" | ", parts);
        return view.IsStale ? $"{text}  ({view.StaleText})" : text;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveWidgetPosition();
        _controller?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 2: Build, run, and verify**

Run: `dotnet build src/ClaudeUsageMonitor.App`
Expected: Build succeeded.
Run: `dotnet run --project src/ClaudeUsageMonitor.App`
Manually verify:
- A `config.json` now exists next to the built executable (under `src/ClaudeUsageMonitor.App/bin/Debug/net10.0-windows/`) with the documented defaults.
- Right-click → **Start with Windows** shows a checkmark (first run registered it). Verify a `ClaudeUsageMonitor` value exists under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (run `Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ClaudeUsageMonitor`).
- Clicking **Start with Windows** again removes the checkmark and the registry value; clicking once more re-adds it.
- Right-click → **Settings** opens `config.json` in the default editor.

- [ ] **Step 3: Commit**

```powershell
git add -A
git commit -m "feat(app): start-with-Windows toggle, settings, first-run config"
```

---

## Task 20: Threshold toast notifications

Subscribe the App to `MonitorController.Alert` and surface each crossing as a balloon toast (`NotifyIcon.ShowBalloonTip`). The controller already raises `Alert` from `AlertEngine` (Task 17); this task only routes it to the tray.

**Files:**
- Modify: `src/ClaudeUsageMonitor.App/App.xaml.cs`

- [ ] **Step 1: Subscribe to alerts in `OnStartup`**

In `App.xaml.cs`, inside `OnStartup`, immediately after the line `_controller.Updated += OnViewUpdated;`, add:

```csharp
        _controller.Alert += OnAlert;
```

- [ ] **Step 2: Add the alert handler method**

Add this method to the `App` class (next to `OnViewUpdated`):

```csharp
    private void OnAlert(string title, string text) => Dispatcher.Invoke(() => _tray?.ShowToast(title, text));
```

- [ ] **Step 3: Build, run, and verify a toast fires**

Run: `dotnet build src/ClaudeUsageMonitor.App`
Expected: Build succeeded.

To force an alert without waiting for real usage to climb, temporarily edit `config.json` (next to the exe) so a threshold sits just below your current usage — e.g. if Session is at 13%, set `"alertThresholds": [10, 95]`. Then:
Run: `dotnet run --project src/ClaudeUsageMonitor.App`
Manually verify:
- Within ~2s a Windows toast/balloon appears titled `Claude Usage — Session at 10%` with body text naming the window.
- The toast fires once, not repeatedly, on subsequent polls.
- Restore `"alertThresholds": [80, 95]` afterward.

- [ ] **Step 4: Commit**

```powershell
git add -A
git commit -m "feat(app): threshold crossing toast notifications"
```

---

## Task 21: Single-file self-contained publish

Produce a self-contained single-file `.exe` requiring no .NET install. Verification is **empirical** — run the produced exe and confirm the tray icon and live data appear. Expect a large (~150 MB) exe; `IncludeNativeLibrariesForSelfExtract` is needed because WPF/WinForms ship native libraries.

**Files:**
- Modify: `src/ClaudeUsageMonitor.App/ClaudeUsageMonitor.App.csproj`

- [ ] **Step 1: Add publish properties to the App csproj**

Add a second `<PropertyGroup>` to `src/ClaudeUsageMonitor.App/ClaudeUsageMonitor.App.csproj` (below the existing one):

```xml
  <PropertyGroup>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <AssemblyName>ClaudeUsageMonitor</AssemblyName>
  </PropertyGroup>
```

- [ ] **Step 2: Publish**

Run: `dotnet publish src/ClaudeUsageMonitor.App -c Release`
Expected: Build succeeded; output at `src/ClaudeUsageMonitor.App/bin/Release/net10.0-windows/win-x64/publish/ClaudeUsageMonitor.exe`.

- [ ] **Step 3: Run the produced exe and verify empirically**

Run: `& "src/ClaudeUsageMonitor.App/bin/Release/net10.0-windows/win-x64/publish/ClaudeUsageMonitor.exe"`
Manually verify:
- A single `.exe` exists and launches with no .NET runtime installed dependency.
- A colored tray icon appears within ~2s and shows live usage matching `/usage`.
- Left-click toggles the widget; the widget shows Session + Week with correct percentages.
- A `config.json` is created next to the `.exe` on first run.
- Quit from the tray menu cleanly exits.

- [ ] **Step 4: Commit**

```powershell
git add src/ClaudeUsageMonitor.App/ClaudeUsageMonitor.App.csproj
git commit -m "build: self-contained single-file publish configuration"
```

---

## Spec coverage map

Verification that every spec section maps to a task:

| Spec section | Task(s) |
|---|---|
| §2.1 Usage API (GET, headers, response shape, null/unknown tolerance) | 4 (parse), 13 (client + headers) |
| §2.2 Credentials (live read, `rateLimitTier` → plan label, never write) | 11, 12 |
| §2.3 JSONL activity (trigger poll + tokens/hr) | 9 (reader/scanner), 15 (watcher), 17 (wiring) |
| §3 Architecture (UI/Core split, dictionary-keyed weekly windows) | 1 (split), 2, 4, 5 |
| §3 Core types sketch (`UsageWindow`/`UsageSnapshot`/`BurnEstimate`/`Status`) | 2 |
| §4 Data flow (60s poll + debounced JSONL trigger, burn, alert, render) | 14, 15, 8, 10, 17 |
| §5.1 Tracked windows & config-driven Week row (highest/aggregate/explicit/`*`, never blank, labeling) | 5 |
| §5 Tray icon (color by max util, tooltip, left/right click, menu) | 16, 17, 19 |
| §5 Widget (frameless, always-on-top, draggable, opacity, position, rows, bars, reset, burn line) | 18 |
| §6 Alerting (80/95 per tracked window, fire once, re-arm on reset, names window) | 10, 17, 20 |
| §7 Auth & offline (live token, in-memory refresh stub, Stale degrade with last-known + badge) | 11, 12, 17 |
| §8 Configuration (`config.json` fields, defaults, Settings opens file, start-with-Windows default on) | 3, 19 |
| §9 Project layout & testing (Core/App/Tests, single-file exe, unit tests) | 1, all Core test tasks, 21 |
| §10 Scope v1 (Session + Week default, exact numbers, burn/ETA, toasts, color, startup, stale, config) | 5, 17, 18, 19, 20 |
| §11 Risks (parsing isolated in one place, 60s cadence, read-only credentials) | 4, 14, 11 |

**Explicitly out of v1 scope** (per Key design decision #8): single-instance enforcement, real OAuth refresh endpoint, historical graphs, cost estimates, settings GUI, per-model 5-hour breakdown, real (non-balloon) toast via AppUserModelID.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-06-04-claude-usage-monitor.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
