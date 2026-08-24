# Kiosk supervisor for AttendanceAgent.exe.
#
# Why this exists: the native ZKFinger library (zkfp2.DBMatch / DBMerge) can crash the whole
# process with an uncatchable AccessViolationException -- confirmed against real hardware, see
# README.md's "Known risk, not yet mitigated" note. No amount of C# try/catch inside the agent
# can prevent that; a crash there kills the process outright. This script does not attempt to
# prevent the crash -- it bounds the damage: relaunch automatically instead of leaving a kiosk
# dark until an operator notices and restarts it by hand.
#
# Point Windows Task Scheduler (or the Startup folder) at THIS script instead of directly at
# AttendanceAgent.exe.

param(
    [string]$ExePath = (Join-Path $PSScriptRoot "src\AttendanceAgent\bin\Release\net8.0-windows\AttendanceAgent.exe"),
    [string]$LogPath = (Join-Path $PSScriptRoot "run-agent.log")
)

$maxRestartsPerWindow = 5
$windowMinutes = 5
$restartTimestamps = New-Object System.Collections.Generic.List[datetime]

function Write-Log([string]$message) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $message"
    Add-Content -Path $LogPath -Value $line
    Write-Output $line
}

if (-not (Test-Path $ExePath)) {
    Write-Log "FATAL: AttendanceAgent.exe not found at '$ExePath'. Build it first (dotnet build -c Release) or pass -ExePath."
    exit 1
}

Write-Log "Watchdog starting. Supervising: $ExePath"

while ($true) {
    $startedAt = Get-Date
    Write-Log "Launching AttendanceAgent.exe..."
    $proc = Start-Process -FilePath $ExePath -PassThru
    $proc.WaitForExit()
    $exitCode = $proc.ExitCode
    $ranFor = (Get-Date) - $startedAt
    Write-Log "AttendanceAgent.exe exited with code $exitCode after $([math]::Round($ranFor.TotalSeconds, 1))s."

    $now = Get-Date
    $restartTimestamps.Add($now)
    $stale = $restartTimestamps | Where-Object { ($now - $_).TotalMinutes -gt $windowMinutes }
    foreach ($t in $stale) { $restartTimestamps.Remove($t) | Out-Null }

    if ($restartTimestamps.Count -gt $maxRestartsPerWindow) {
        Write-Log "FATAL: $($restartTimestamps.Count) restarts within $windowMinutes minutes -- this looks like a persistent problem (corrupted agent.db, missing DLL, bad config), not a one-off crash. Stopping the watchdog instead of restart-looping forever. Fix the underlying problem, then re-run this script."
        exit 1
    }

    Start-Sleep -Seconds 3
}
