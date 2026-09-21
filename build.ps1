$ErrorActionPreference='Continue'
# Build script (lives in the DEV directory: C:\project\SilentStartManager).
# Reads the .cs next to this script, outputs ssm.exe + ssm-engine.exe to the
# DEPLOY directory (%LOCALAPPDATA%\SilentStartManager) where the program runs.
$devDir   = $PSScriptRoot
$deployDir = Join-Path $env:LOCALAPPDATA 'SilentStartManager'
$csc64    = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fw       = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs     = @("$fw\System.dll","$fw\System.Runtime.Serialization.dll","$fw\System.Core.dll")
$refArg   = ($refs | Where-Object { Test-Path $_ }) -join ','

if (-not (Test-Path $csc64)) { throw "csc.exe not found at $csc64 (install .NET Framework 4.x developer components)" }
if (-not (Test-Path "$devDir\ssm_shared.cs")) { throw "ssm_shared.cs not found next to this script" }
New-Item $deployDir -ItemType Directory -Force | Out-Null

function BuildExe($name, $target, $csFile) {
  $out = Join-Path $deployDir "$name.exe"
  Remove-Item $out -Force -ErrorAction SilentlyContinue
  $files = @((Join-Path $devDir 'ssm_shared.cs'), (Join-Path $devDir $csFile))
  & $csc64 /nologo "/target:$target" "/out:$out" "/reference:$refArg" $files 2>&1 | ForEach-Object { Write-Output $_ }
  if (Test-Path $out) {
    $i = Get-Item $out
    Write-Output ("BUILD OK -> $out ($($i.Length) bytes, $($i.LastWriteTime))")
  } else { Write-Output "BUILD FAILED -> $name.exe" }
}

Write-Output "=== build dir: $devDir ==="
Write-Output "=== deploy dir: $deployDir ==="
Write-Output "building ssm.exe (CLI, console /target:exe) ..."
BuildExe 'ssm' 'exe' 'ssm_cli.cs'
Write-Output "building ssm-engine.exe (engine, GUI /target:winexe) ..."
BuildExe 'ssm-engine' 'winexe' 'ssm_engine.cs'
Write-Output "done. Run install_ssm.ps1 (next to this script) to (re)point the logon task."
