$ErrorActionPreference = 'Stop'
# SilentStartManager install (lives in the DEV directory).
# Deploys/points to the PROGRAM directory: %LOCALAPPDATA%\SilentStartManager.
# Removes any deprecated SSM/FeishuTray scheduled tasks, then registers the logon
# task pointing at ssm-engine.exe in the deploy directory.
$deployDir = Join-Path $env:LOCALAPPDATA 'SilentStartManager'
$engine    = Join-Path $deployDir 'ssm-engine.exe'
$taskName  = "SilentStartManager"

if (-not (Test-Path $engine)) {
  throw "ssm-engine.exe not found in $deployDir. Run build.ps1 (next to this script) first."
}

# 1) 清理 taskschd 里废弃的相关任务（旧指向 / 旧版本），直接删除
$stale = @('SilentStartManager','FeishuTrayHelper','SSM','ssm-engine')
foreach ($tn in $stale) {
  $t = Get-ScheduledTask -TaskName $tn -ErrorAction SilentlyContinue
  if ($t) {
    # 只删"废弃"的：指向不存在路径的，或名字匹配旧版
    $exec = $t.Actions | Select-Object -First 1 -ExpandProperty Execute -ErrorAction SilentlyContinue
    $deprecated = $tn -ne 'SilentStartManager' -or -not $exec -or -not (Test-Path $exec)
    if ($tn -ne 'SilentStartManager') {
      Unregister-ScheduledTask -TaskName $tn -Confirm:$false -ErrorAction SilentlyContinue
      Write-Output ("removed deprecated task: " + $tn)
    }
  }
}
# 旧版任务（指向旧 exe）先注销，稍后重建指向新引擎
$old = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
$oldExec = if ($old) { ($old.Actions | Select-Object -First 1 -ExpandProperty Execute) } else { $null }
if ($old -and $oldExec -and $oldExec -ne $engine) {
  Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
  Write-Output "removed deprecated task 'SilentStartManager' (was -> $oldExec)"
}

# 2) 注册登录任务 -> 部署目录的无窗口引擎（Set 优先原地更新，Register 兜底）
$trigger  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$action   = New-ScheduledTaskAction -Execute $engine -WorkingDirectory $deployDir
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew
$desc = "SilentStartManager: engine silent start + resident standby (no tray, no window). Deploy: $deployDir; CLI: ssm.exe; stop: ssm stop"

$reg = $null
for ($i = 0; $i -lt 6; $i++) {
  $reg = Register-ScheduledTask -TaskName $taskName -Trigger $trigger -Action $action -Description $desc -Settings $settings -Force -ErrorAction SilentlyContinue
  if ($reg) { break }
  Start-Sleep 2
}
if (-not $reg) { throw "failed to register task after retries" }

Write-Output "=== SilentStartManager installed ==="
Write-Output ("  task: '" + $reg.TaskName + "'  State=" + $reg.State + "  -> " + $engine)
Write-Output "  program dir: " + $deployDir
Write-Output "  dev dir: " + $PSScriptRoot
Write-Output "  config: " + (Join-Path $deployDir 'config.json')
Write-Output "  BEHAVIOR: at logon the engine silently starts every enabled item, re-hides popups during the silent window, then stays resident (no tray, no window)."
Write-Output "  - add a program:   ssm add <exePath> [--name n] [--title t] [--window ms]   (takes effect next logon)"
Write-Output "  - see programs:    ssm scan / list / status"
Write-Output "  - show a window:   ssm show <name>"
Write-Output "  - start/stop:      ssm run / ssm stop"
