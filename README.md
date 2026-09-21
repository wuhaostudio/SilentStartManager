# SilentStartManager

SilentStartManager (`ssm`) is a lightweight Windows utility that starts your chosen applications at logon **completely silently** and keeps their windows hidden until you ask to see them. No tray icon, no visible window, no console popup — just background processes that come back to life with a single command.

## How it works

Two small .NET executables, built with the plain `csc.exe` compiler — no project files, no Visual Studio, no dependencies beyond the .NET Framework already on Windows:

| Component | Role |
|---|---|
| `ssm.exe` | Console CLI: configure items, inspect state, control the engine |
| `ssm-engine.exe` | Window-less resident engine: at logon it starts each enabled app, poll-hides its windows during a per-item silent window, then stands by |

A logon **scheduled task** (registered by `install_ssm.ps1`) launches the engine with no console window. The CLI and engine are stateless with each other — they coordinate only through `config.json`, a pid file, and the OS process table.

## Features

- **Silent start**: each app is started if not running, then its windows are hidden (matched by process name, optionally filtered by a title substring)
- **Per-item silent window**: tunable hide-poll duration (`--window ms`); defaults to 10 s for already-running apps, 20 s for fresh starts
- **One-line restore**: `ssm show <name>` brings a hidden window back
- **Program discovery**: `ssm scan [keyword]` reads the registry `Uninstall` keys and guesses the real exe for any installed program
- **Zero footprint**: no tray, no window, no IPC; the engine idles in standby after the pass
- **Single-instance engine** via named mutex; safe to restart

## Quick start

### 1. Build

Requires Windows with .NET Framework 4.x (the built-in `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`). From the source directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Outputs `ssm.exe` and `ssm-engine.exe` into the program directory `%LOCALAPPDATA%\SilentStartManager` (where the program runs; config, log, and pid files live next to the exes).

### 2. Install (register logon task)

```powershell
powershell -ExecutionPolicy Bypass -File .\install_ssm.ps1
```

This removes any deprecated related scheduled tasks and registers a per-user **AtLogon** task that runs the engine window-less.

### 3. Manage programs

```powershell
# from the program directory (where ssm.exe lives)
ssm scan Office              # find installed programs, guess their exe
ssm add "C:\path\app.exe" --name app --title "Welcome" --window 15000
ssm list                    # configured items
ssm status                  # running / visible-hidden window state + engine
ssm show app                # restore a hidden window
ssm start                   # one-shot silent pass without the resident engine
ssm run                     # spawn the detached engine now
ssm stop                    # stop the engine
```

New items take effect at the next logon (or immediately via `ssm stop && ssm run`).

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall_ssm.ps1
```

Stops the engine, removes the scheduled task, and deletes the program directory. Sources and scripts are kept.

## Project layout

```
ssm_shared.cs      shared core: config model, P/Invoke, window hide/show, silent pass
ssm_cli.cs         ssm.exe  — CLI entry point (console target)
ssm_engine.cs      ssm-engine.exe — resident window-less engine (GUI target)
build.ps1          compiles both exes with csc.exe into %LOCALAPPDATA%\SilentStartManager
install_ssm.ps1    registers the logon scheduled task
uninstall_ssm.ps1  clean removal
```

## Configuration

`config.json` next to the exes:

```json
{
  "items": [
    {
      "name": "app",
      "exe": "C:\\path\\app.exe",
      "processName": "app",
      "windowTitle": "Welcome",
      "silentWindowMs": 15000,
      "enabled": true
    }
  ]
}
```

`silentWindowMs = 0` means *auto* (10 s if the process is already running, 20 s otherwise).

## License

[MIT](LICENSE)
