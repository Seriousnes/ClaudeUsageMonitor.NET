#Requires -Version 7
<#
.SYNOPSIS
    Publishes ClaudeUsageMonitor (framework-dependent) into %APPDATA%\ClaudeUsageMonitor and relaunches it.

.DESCRIPTION
    Installs the app into the same per-user folder as its config.json (%APPDATA%\ClaudeUsageMonitor), so
    the binary and its settings live together in one fixed location. A fixed location matters: Windows 11
    keys each tray icon's show/hide preference under HKCU\Control Panel\NotifyIconSettings by the
    executable's full path, so running from a new path each build (bin\Debug, bin\Release\...\publish, a
    different -o) leaves a fresh "Off" entry behind every time.

    The build is framework-dependent: it requires the .NET Desktop Runtime (win-x64) to be installed and is
    NOT self-contained. config.json already in the target folder is preserved — publish only overwrites its
    own output files, it does not clean the directory.

    Run this instead of a bare `dotnet publish`.
#>
$ErrorActionPreference = 'Stop'

$proj       = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\ClaudeUsageMonitor.App\ClaudeUsageMonitor.App.csproj'
$installDir = Join-Path $env:APPDATA 'ClaudeUsageMonitor'
$exe        = Join-Path $installDir 'ClaudeUsageMonitor.exe'

# The single-file exe is locked while running, so stop every instance before overwriting. This also clears
# any copy still running from an earlier build location, leaving a single instance from the new path.
Get-Process ClaudeUsageMonitor -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Framework-dependent, single-file publish co-located with config.json. --self-contained false keeps it off
# the self-contained path; PublishSingleFile (set in the csproj) requires the RID, hence -r win-x64.
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
dotnet publish $proj -c Release -r win-x64 --self-contained false -o $installDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
if (-not (Test-Path $exe)) { throw "Publish succeeded but $exe is missing." }

# Re-point "Start with Windows" to the new path if it is currently enabled. The app writes this HKCU\Run
# value (a quoted path) from Environment.ProcessPath, so after relocating the binary the entry would still
# autostart the old path until the tray toggle is flipped. Only touch it when the value already exists.
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runItem = Get-Item $runKey -ErrorAction SilentlyContinue
if ($runItem -and $runItem.GetValue('ClaudeUsageMonitor', $null)) {
    Set-ItemProperty -Path $runKey -Name 'ClaudeUsageMonitor' -Value "`"$exe`""
    Write-Host "Re-pointed 'Start with Windows' -> $exe"
}

Start-Process $exe
Write-Host "Installed and launched: $exe"
