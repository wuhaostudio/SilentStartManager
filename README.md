# 🎬 SilentStartManager

<p align="center">
  <img src="assets/logo.svg" alt="SilentStartManager logo" width="120"><br>
  <strong>Windows apps that start at logon <em>completely silently</em> — no tray icon, no visible window, no console popup.<br>
  Just background processes, restored when you ask for them.</strong>
</p>

![GitHub stars](https://img.shields.io/github/stars/wuhaostudio/SilentStartManager?style=flat-square)
![GitHub forks](https://img.shields.io/github/forks/wuhaostudio/SilentStartManager?style=flat-square)
![GitHub issues](https://img.shields.io/github/issues/wuhaostudio/SilentStartManager?style=flat-square)
![Language C#](https://img.shields.io/badge/language-C%23-68267B?style=flat-square)
![Platform Windows](https://img.shields.io/badge/platform-Windows-0078D6?style=flat-square)
![Runtime .NET Framework 4.x](https://img.shields.io/badge/runtime-.NET%20Framework%204.x-512BD4?style=flat-square)
![License MIT](https://img.shields.io/badge/license-MIT-green?style=flat-square)

---

## ⚙️ How it works

Two tiny .NET executables, compiled with the stock `csc.exe` — no project files, no Visual Studio, no dependencies beyond the .NET Framework already on Windows:

| 组件 | 角色 |
|---|---|
| 🖥️ `ssm.exe` | 控制台 CLI:管理条目、查看状态、控制引擎 |
| 👻 `ssm-engine.exe` | 无窗口常驻引擎:登录时静默启动各应用,轮询隐藏窗口,随后待机 |

一个**计划任务**(由 `install_ssm.ps1` 注册)在登录时以无窗口方式拉起引擎。CLI 与引擎之间没有 IPC,只通过 `config.json`、pid 文件和 OS 进程表协作。

```
  ┌────────────┐   logon    ┌──────────────────┐   hide / show   ┌─────────────┐
  │ Task Plann │ ─────────▶ │  ssm-engine.exe  │ ◀──────────────▶ │  your apps  │
  └────────────┘            │  (no window)     │                 └─────────────┘
                            └────────┬─────────┘
                                     │ spawn / kill (pid file)
                            ┌────────┴─────────┐
                            │  ssm.exe (CLI)   │
                            └──────────────────┘
```

## ✨ Features

- 🤫 **Silent start** — 每个条目先按需启动,再隐藏其窗口(按进程名匹配,可选按标题子串过滤)
- ⏱️ **Per-item silent window** — 每条目可调的隐藏轮询时长(`--window ms`);默认已运行 10 s、新启动 20 s
- 🪄 **One-line restore** — `ssm show <name>` 一键找回隐藏窗口
- 🔍 **Program discovery** — `ssm scan [keyword]` 读取注册表 `Uninstall` 键,自动推断真实 exe
- 🪶 **Zero footprint** — 无托盘、无窗口、无 IPC;引擎在静默窗口结束后空闲待机
- 🔒 **Single-instance engine** — 命名互斥锁保证,重启安全

## 🚀 Quick start

**Requires** Windows with .NET Framework 4.x(自带 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`)。

**1️⃣ Build** — 在源码目录:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

生成 `ssm.exe` 和 `ssm-engine.exe` 到程序目录 `%LOCALAPPDATA%\SilentStartManager`(程序运行位置;配置、日志、pid 与 exe 同目录)。

**2️⃣ Install**(注册登录计划任务):

```powershell
powershell -ExecutionPolicy Bypass -File .\install_ssm.ps1
```

移除已废弃的相关计划任务,并注册每用户 **AtLogon** 任务,无窗口方式运行引擎。

**3️⃣ Manage programs**(在程序目录下):

```powershell
ssm scan Office          # 查找已安装程序,推断 exe
ssm add "C:\path\app.exe" --name app --title "Welcome" --window 15000
ssm list                # 查看配置条目
ssm status              # 运行 / 窗口显隐状态 + 引擎状态
ssm show app            # 恢复隐藏窗口
ssm start               # 一次性静默执行(不启动常驻引擎)
ssm run                 # 立即派生常驻引擎
ssm stop                # 停止引擎
```

> 💡 新条目下次登录生效(或立即 `ssm stop && ssm run`)。

**Uninstall**:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall_ssm.ps1
```

停止引擎、删除计划任务与程序目录;源码与脚本保留。

## 📂 Project layout

```
├── ssm_shared.cs      # 共享核心:配置模型、P/Invoke、窗口显隐、静默扫描
├── ssm_cli.cs         # ssm.exe  — CLI 入口(console target)
├── ssm_engine.cs      # ssm-engine.exe — 无窗口常驻引擎(GUI target)
├── build.ps1          # csc 编译两个 exe → %LOCALAPPDATA%\SilentStartManager
├── install_ssm.ps1    # 注册登录计划任务
└── uninstall_ssm.ps1  # 干净卸载
```

## 🛠️ Configuration

exe 同目录下的 `config.json`:

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

`silentWindowMs = 0` 表示 *auto*(已运行 10 s / 未运行 20 s)。

## 📄 License

[MIT](LICENSE)
