$ErrorActionPreference = 'Stop'
# SilentStartManager uninstall (lives in the DEV directory).
# Stops the engine, removes the logon task, and deletes the PROGRAM directory
# (%LOCALAPPDATA%\SilentStartManager). The dev dir (sources + scripts) is kept.
$deployDir = Join-Path $env:LOCALAPPDATA 'SilentStartManager'
$cli       = Join-Path $deployDir 'ssm.exe'
$taskName  = "SilentStartManager"

Write-Output "=== Uninstalling SilentStartManager (program dir: $deployDir) ==="

# 1) Gracefully stop the engine via CLI, fallback kill by name
if (Test-Path $cli) {
    try { & $cli stop | Out-Null } catch { }
}
Start-Sleep -Milliseconds 500
Stop-Process -Name 'ssm-engine' -Force -ErrorAction SilentlyContinue

# 2) Remove SSM-related scheduled tasks (current + any deprecated leftovers)
foreach ($tn in @($taskName, 'FeishuTrayHelper')) {
    $d = Unregister-ScheduledTask -TaskName $tn -Confirm:$false -ErrorAction SilentlyContinue
    if ($d) { Write-Output "task unregistered: $tn" } else { Write-Output "task: $tn not found (nothing to remove)" }
}

# 3) Delete the program dir (dev dir stays untouched)
Remove-Item -Path $deployDir -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $deployDir) { Write-Output "WARNING: dir still exists (locked?): $deployDir" } else { Write-Output "program dir removed: $deployDir" }
Write-Output "done. Sources and scripts remain in: " + $PSScriptRoot
