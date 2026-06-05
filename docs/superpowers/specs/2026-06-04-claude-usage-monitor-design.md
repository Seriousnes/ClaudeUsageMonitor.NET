# Claude Usage Monitor — Design Spec

**Date:** 2026-06-04
**Status:** Approved (pending user spec review)
**Platform:** Windows 11, .NET 10, WPF

## 1. Goal

A lightweight Windows tray application that displays Claude subscription usage in
real time, so the user never has to manually run `/usage` again. Displayed numbers
match `/usage` exactly because they come from the same authoritative API. A
color-coded tray icon plus threshold notifications let the user ignore the tool
until it proactively signals that a limit is approaching.

**Primary success criterion:** the user can stop manually monitoring their session
limit. This requires (a) numbers that match `/usage` to the decimal so the tool is
trusted, and (b) proactive alerts so the user does not have to glance at it.

## 2. Data Sources (verified 2026-06-04)

### 2.1 Authoritative — Anthropic usage API

```
GET https://api.anthropic.com/api/oauth/usage
Headers:
  Authorization: Bearer <accessToken>
  anthropic-beta: oauth-2025-04-20
```

Verified response shape:

```jsonc
{
  "five_hour":  { "utilization": 13.0, "resets_at": "2026-06-04T20:30:00.737894+10:00" }, // session limit
  "seven_day":  { "utilization": 20.0, "resets_at": "2026-06-10T18:00:00.73791+10:00" },  // weekly
  "seven_day_opus":   null,                                  // weekly Opus cap (Max), null when inactive
  "seven_day_sonnet": { "utilization": 2.0, "resets_at": "..." },
  "extra_usage": { "is_enabled": false, "monthly_limit": null, "used_credits": null,
                   "utilization": null, "currency": null, "disabled_reason": null }
}
```

- `utilization` is a percentage (0–100). `resets_at` is an ISO-8601 timestamp with offset.
- **Per-model breakdown exists only on the weekly windows** (`seven_day_opus`, `seven_day_sonnet`,
  …). `five_hour` is aggregate-only — there is no per-model 5-hour window in the API.
- Any of the `seven_day_*` blocks may be `null` (observed: `seven_day_opus` was `null` while
  `seven_day_sonnet` was present, despite heavy Opus use). Selection logic (§5.1) must tolerate a
  selected model window being `null` and fall back to the aggregate `seven_day`.
- `/api/rate-limits` returns 404 and is not used.

### 2.2 Credentials — `~/.claude/.credentials.json`

```jsonc
{ "claudeAiOauth": {
    "accessToken": "...", "refreshToken": "...", "expiresAt": <epoch-ms>,
    "scopes": [...], "subscriptionType": "max", "rateLimitTier": "default_claude_max_5x" } }
```

- `rateLimitTier` → plan label shown in the widget (e.g. "Max 5x"). Auto-detected; no user input.
- Read **live on each poll** — Claude Code's daemon refreshes this file (observed ~8h cadence
  in `daemon.log`), so reading fresh picks up rotated tokens automatically.

### 2.3 Live activity trigger — JSONL transcripts

`~/.claude/projects/**/*.jsonl` — append-only transcripts. Each assistant entry carries
`message.usage` (`input_tokens`, `output_tokens`, `cache_creation_input_tokens`,
`cache_read_input_tokens`) and a `timestamp`. Used to:
1. Detect activity (file change) → trigger an immediate API poll so the % updates instantly.
2. Provide a secondary tokens/hour "activity" readout.

The JSONL is **not** used to compute the authoritative session/weekly % (the API is). It only
supplements responsiveness and burn-rate display.

## 3. Architecture

UI is fully separated from logic so the Core layer is unit-testable headless.

```
UI layer (WPF, references Core)
  • TrayIcon (NotifyIcon)  — color badge, tooltip, context menu
  • Widget window          — frameless, always-on-top, semi-transparent, draggable

Core layer (no WPF references)
  • AuthProvider     — reads .credentials.json live; in-memory OAuth refresh on expiry
  • UsagePoller      — GET /api/oauth/usage on interval + on-demand; emits UsageSnapshot
  • JsonlWatcher     — FileSystemWatcher over projects/**/*.jsonl; emits activity events
  • BurnCalculator   — ETA from recent utilization-% samples; tokens/hr from JSONL
  • AlertEngine      — threshold crossing detection with per-window arming
  • ConfigStore      — load/save config.json

External: Anthropic usage API · ~/.claude credentials + JSONL transcripts
```

### Core types (sketch)

```csharp
record UsageWindow(double Utilization, DateTimeOffset ResetsAt);
record UsageSnapshot(
    UsageWindow FiveHour,                                  // session limit (aggregate)
    UsageWindow SevenDay,                                  // weekly (aggregate)
    IReadOnlyDictionary<string, UsageWindow> WeeklyByModel,// "opus","sonnet",… (only non-null keys)
    DateTimeOffset FetchedAt, string PlanLabel);
record BurnEstimate(double TokensPerHour, TimeSpan? EtaSoonest, string EtaWindowLabel);
enum  Status { Green, Yellow, Orange, Red, Stale }
```

Per-model weekly windows are kept in a **dictionary keyed by model family** (not fixed fields), so
new/renamed `seven_day_*` keys are tolerated without code changes. Which keys are *displayed and
alerted on* is decided by config (§5.1), not hardcoded.

## 4. Data Flow

1. `UsagePoller` fires every 60s (configurable). `JsonlWatcher` firing also triggers a poll
   (debounced ~2s) so the % moves right after a message completes.
2. Each poll: `AuthProvider` supplies a valid token → GET usage → parse → `UsageSnapshot`.
3. `BurnCalculator` keeps a short rolling history of utilization samples per tracked window (§5.1)
   and projects each slope to 100%, reporting the **soonest** as `EtaSoonest`/`EtaWindowLabel`.
   Tokens/hr derived from JSONL entries in the trailing window.
4. `AlertEngine` compares new snapshot against armed thresholds, evaluated over the **tracked
   windows** (§5.1) → may raise a toast.
5. UI re-renders tray color/tooltip and the widget (if visible).

## 5. UI / UX

### 5.1 Tracked windows & the "Week" row (config-driven)

The widget shows two kinds of rows: **Session** (5h, aggregate) and **Week**. Session is always the
aggregate 5-hour window — the API has no per-model 5h breakdown. What the **Week** row *means* is
chosen by `config.weeklyModels`, which resolves to one (or more) weekly windows:

- `["highest"]` — **default.** The single highest-tier non-null per-model weekly window. Tier order
  `opus > sonnet > haiku`; currently resolves to Opus, and auto-follows future tiers since the API
  keys on model *family*, not version (Opus 4.8 → Opus 5 stays `opus`). If that model window is
  `null` (observed: `seven_day_opus` absent while Sonnet present), it **falls back to the aggregate
  `seven_day`** so "Week" is never blank.
- `[]` — the aggregate weekly (`seven_day`).
- `["opus"]`, `["opus","sonnet"]` — explicit family keys.
- `["*"]` — aggregate weekly plus every non-null per-model window.

With the default the user gets a clean **Session + Week** widget, where "Week" tracks their top
model (Opus, falling back to aggregate). When exactly one weekly window is tracked it is labelled
simply **"Week"**; when several are tracked, the aggregate is "Week" and the rest are labelled by
model name.

**Tracked windows** = `{ session } ∪ { resolved weekly window(s) }`. This single set drives the
widget rows, tray color, tooltip, alert arming, and ETA — so **what you see is exactly what you're
warned about.** For the user that is Session + the Opus-focused Week; Sonnet never participates.

### Tray icon (always present)
- Color = `Status`, driven by the **maximum utilization across the tracked windows** (§5.1):
  - Green `<50%` · Yellow `50–80%` · Orange `80–95%` · Red `≥95%` · greyed when `Stale`.
- Tooltip: `Session 72% · resets 1h48m | Week 41%` (tracked windows only).
- Left-click → toggle widget. Right-click → menu:
  `Show/Hide widget` · `Refresh now` · `Start with Windows ✓` · `Settings` (opens config.json) · `Quit`.

### Widget (frameless, always-on-top, draggable, position remembered)
```
┌────────────────────────────────┐
│ Claude  ·  Max 5x          ⟳ ✕ │
│ Session  72%  ▓▓▓▓▓▓▓░░░        │
│   resets in 1h 48m             │
│ Week     64%  ▓▓▓▓▓▓░░░░        │
│   resets Wed                   │
│ ⚠ ~38 min to session limit     │
└────────────────────────────────┘
```
- Rows = the tracked windows (§5.1): with the default config, **Session + Week** (Week = the
  Opus-focused weekly, or aggregate fallback). Extra weekly rows appear only if the user configures
  multiple `weeklyModels`.
- Burn/ETA line shows the **soonest-projected** tracked window (`EtaSoonest` / `EtaWindowLabel`);
  hidden when activity is idle or no projection is meaningful.
- `⟳` = refresh now, `✕` = hide to tray (does not quit).

## 6. Alerting

- Windows toast when **any tracked window** (§5.1) crosses **80%** and **95%** (defaults). With the
  default config that means the Session and the Week (Opus-focused) windows — never Sonnet.
- Each threshold fires once per window; re-arms when that window's `resets_at` passes (detected
  by a drop in utilization or a new reset timestamp). The toast names which window tripped
  (e.g. "Week at 80%").
- Tray color is the passive signal; toasts are the active one.

## 7. Auth & Offline Behavior

- Token read live each poll. If `expiresAt` is in the future → use `accessToken`.
- If expired: attempt an **in-memory** OAuth refresh via `refreshToken` against Claude Code's
  token endpoint. The refreshed token is held in memory only — **not** written back to
  `.credentials.json`, to avoid racing Claude Code's daemon.
- If refresh fails (offline/revoked): enter `Stale` state — show last-known values greyed with a
  `stale · Nm ago` badge, keep the JSONL activity readout. The widget therefore remains useful
  when Claude Code is closed (answering "do I have budget before I start?").

> Open implementation detail to confirm during planning: exact OAuth token endpoint + grant
> parameters (to be extracted from the Claude Code binary / observed refresh). Until then, the
> `Stale` fallback guarantees the app degrades gracefully even if refresh is deferred to a later iteration.

## 8. Configuration

`config.json` beside the executable:

```jsonc
{
  "pollIntervalSeconds": 60,
  "alertThresholds": [80, 95],
  "weeklyModels": ["highest"],   // ["highest"] | ["opus"] | ["opus","sonnet"] | ["*"] | []
  "widgetOpacity": 0.92,
  "widgetPosition": { "x": null, "y": null },
  "startWithWindows": true
}
```

`weeklyModels` selects what the "Week" row tracks (§5.1); default `["highest"]` follows the top
model tier (currently Opus). `pollIntervalSeconds` defaults to 60. `startWithWindows` defaults to
**true** (registers a per-user startup entry on first run; toggleable from the tray menu). Edited
via tray → Settings (opens the file). No settings GUI in v1.

## 9. Project Layout & Testing

```
ClaudeUsageMonitor.NET/
  ClaudeUsageMonitor.sln
  src/
    ClaudeUsageMonitor.Core/        # pollers, parsers, burn, alerts, config (no WPF)
    ClaudeUsageMonitor.App/         # WPF tray + widget
  tests/
    ClaudeUsageMonitor.Core.Tests/  # window math, burn slope, threshold arming, JSON parse
  docs/superpowers/specs/
```

- Built as a self-contained single-file `.exe` (no runtime install required).
- Core logic unit-tested. API client tested against a captured sample response (§2.1).
- Burn/threshold logic tested with synthetic utilization-sample sequences.

## 10. Scope

**v1 (in):** tray icon + widget (**Session + Week** by default); **config-driven "Week" row
(default `["highest"]` = Opus, aggregate fallback)**; exact API numbers; burn-rate/ETA; 80/95%
toasts; tray color status; start-with-Windows (default **on**); offline/stale degrade; config.json.

**Out (later):** historical graphs/trends; cost estimates; multi-account; full settings GUI;
per-model 5-hour breakdown (not exposed by the API).

## 11. Risks / Notes

- The usage endpoint is undocumented; response keys may change. Isolate parsing in one place so a
  shape change is a one-file fix. Unknown/null keys are tolerated by design.
- Poll cadence defaults to 60s (not seconds-level) to avoid hammering an account endpoint; the
  JSONL watch provides the "instant" feel without high-frequency polling.
- Token handling reads (never writes) `.credentials.json` to avoid corrupting Claude Code state.
