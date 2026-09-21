using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SSM.Shared;
using Microsoft.Win32;

// V6 CLI: ssm.exe (console subsystem, /target:exe).
// All commands are stateless local operations: read/write config.json next to the
// exe, enumerate OS processes/windows directly, and spawn/kill the engine when
// needed. No IPC with the resident engine.
namespace SSM.Cli
{
    static class Program
    {
        static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            switch (cmd)
            {
                case "scan": DoScan(args); break;
                case "add": DoAdd(args); break;
                case "remove": DoRemove(args); break;
                case "list": DoList(); break;
                case "status": DoStatus(); break;
                case "show": DoShow(args); break;
                case "start": DoStart(); break;
                case "run": DoRun(); break;
                case "stop": DoStop(); break;
                default: Help(); break;
            }
            return 0;
        }

        // ---------- scan ----------
        static void DoScan(string[] a)
        {
            string kw = a.Length >= 2 ? a[1] : "";
            var seen = new HashSet<string>();
            var rows = new List<string[]>();
            ScanRoot(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", seen, rows);
            ScanRoot(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", seen, rows);
            ScanRoot(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", seen, rows);

            var filtered = new List<string[]>();
            foreach (var r in rows)
                if (kw.Length == 0 || r[0].IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) filtered.Add(r);

            Console.WriteLine(string.Format("Found {0} installed program(s){1}. Filter='{2}'",
                filtered.Count, filtered.Count == 1 ? "" : "s", kw));
            Console.WriteLine(string.Format("{0,-40} {1}", "PROGRAM", "EXE (guessed)"));
            Console.WriteLine("------------------------------------------------------------");
            foreach (var r in filtered)
                Console.WriteLine(string.Format("{0,-40} {1}", r[0], r[1]));
            Console.WriteLine();
            Console.WriteLine("To manage one:  ssm add <exePath> [--name n] [--title t] [--window ms]");
        }

        static void ScanRoot(RegistryKey root, string sub, HashSet<string> seen, List<string[]> rows)
        {
            try
            {
                using (var baseKey = root.OpenSubKey(sub))
                {
                    if (baseKey == null) return;
                    foreach (var sn in baseKey.GetSubKeyNames())
                    {
                        using (var k = baseKey.OpenSubKey(sn))
                        {
                            if (k == null) continue;
                            var disp = (k.GetValue("DisplayName") ?? "") as string;
                            var icon = (k.GetValue("DisplayIcon") ?? "") as string;
                            var loc = (k.GetValue("InstallLocation") ?? "") as string;
                            if (string.IsNullOrWhiteSpace(disp)) continue;
                            string exe = ResolveExe(icon, loc);
                            if (exe == null) continue;
                            if (!File.Exists(exe)) continue;
                            string key = exe.ToLowerInvariant();
                            if (!seen.Add(key)) continue;
                            rows.Add(new string[] { disp, exe, Path.GetFileNameWithoutExtension(exe) });
                        }
                    }
                }
            }
            catch { }
        }

        static string ResolveExe(string icon, string loc)
        {
            if (!string.IsNullOrWhiteSpace(icon))
            {
                string s = icon.Trim().Trim('"');
                int comma = s.IndexOf(',');
                if (comma >= 0) s = s.Substring(0, comma).Trim().Trim('"');
                if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(s)) return s;
            }
            if (!string.IsNullOrWhiteSpace(loc) && Directory.Exists(loc))
            {
                try
                {
                    var exes = Directory.GetFiles(loc, "*.exe");
                    if (exes.Length > 0) return exes[0];
                }
                catch { }
            }
            return null;
        }

        // ---------- add / remove / list ----------
        static void DoAdd(string[] a)
        {
            string exe = null, name = null, title = null;
            int window = 0;
            for (int i = 1; i < a.Length; i++)
            {
                string t = a[i].ToLowerInvariant();
                if (t == "--name") { if (i + 1 < a.Length) name = a[++i]; }
                else if (t == "--title") { if (i + 1 < a.Length) title = a[++i]; }
                else if (t == "--window") { if (i + 1 < a.Length) int.TryParse(a[++i], out window); }
                else if (exe == null) exe = a[i];
            }
            if (exe == null)
            {
                Console.WriteLine("usage: ssm add <exePath> [--name n] [--title t] [--window ms]");
                return;
            }
            if (!File.Exists(exe)) Console.WriteLine("[warn] exe not found: " + exe + " (added anyway)");
            if (name == null) name = Path.GetFileNameWithoutExtension(exe);
            var cfg = Core.Load();
            var it = new Item();
            it.name = name; it.exe = exe; it.processName = Path.GetFileNameWithoutExtension(exe);
            it.windowTitle = title == null ? "" : title; it.silentWindowMs = window; it.enabled = true;
            if (Core.FindItem(cfg, name) != null) Console.WriteLine("[note] replacing existing item '" + name + "'");
            cfg.items.Add(it);
            Core.Save(cfg);
            Core.Log("cli add '" + name + "' -> " + exe);
            Console.WriteLine("added '" + name + "' -> " + exe + "  (processName=" + it.processName + ", silentWindowMs=" + (it.silentWindowMs > 0 ? it.silentWindowMs + "ms" : "auto") + ")");
            Console.WriteLine("Takes effect at next logon (or run 'ssm stop && ssm run' now).");
        }

        static void DoRemove(string[] a)
        {
            string nm = a.Length >= 2 ? a[1] : null;
            if (nm == null) { Console.WriteLine("usage: ssm remove <name>"); return; }
            var cfg = Core.Load();
            int idx = -1;
            for (int i = 0; i < cfg.items.Count; i++)
                if (string.Equals(cfg.items[i].name, nm, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
            if (idx < 0) { Console.WriteLine("no item named '" + nm + "'"); return; }
            var removed = cfg.items[idx];
            cfg.items.RemoveAt(idx);
            Core.Save(cfg);
            Core.Log("cli remove '" + nm + "'");
            Console.WriteLine("removed '" + nm + "' (" + removed.exe + ")");
            Console.WriteLine("Takes effect at next logon (the running engine already did its pass).");
        }

        static void DoList()
        {
            var cfg = Core.Load();
            if (cfg.items.Count == 0) { Console.WriteLine("(no items configured. use: ssm add <exePath>)"); return; }
            Console.WriteLine(string.Format("{0,-16} {1,-10} {2}", "NAME", "PROCESS", "EXE"));
            Console.WriteLine("------------------------------------------------------------");
            foreach (var it in cfg.items)
            {
                string win = it.silentWindowMs > 0 ? it.silentWindowMs + "ms" : "auto";
                string on = it.enabled ? "" : " [disabled]";
                string tl = string.IsNullOrEmpty(it.windowTitle) ? "" : " title~\"" + it.windowTitle + "\"";
                Console.WriteLine(string.Format("{0,-16} {1,-10} {2}{3}{4}", it.name, it.processName, it.exe, tl, on));
            }
            Console.WriteLine(string.Format("\n{0} item(s). config: {1}", cfg.items.Count, Core.CfgPath));
        }

        // ---------- status ----------
        static void DoStatus()
        {
            var cfg = Core.Load();
            Console.WriteLine(string.Format("{0,-16} {1,-12} {2,-18} {3}", "NAME", "RUNNING", "WINDOWS(v/h)", "STATE"));
            Console.WriteLine("------------------------------------------------------------");
            foreach (var it in cfg.items)
            {
                int procs = Core.ProcCount(it.processName);
                var wins = Core.CollectWindows(it.processName, it.windowTitle);
                int vis = 0, hid = 0;
                foreach (var h in wins) { if (P.IsWindowVisible(h)) vis++; else hid++; }
                string st = it.enabled ? (hid > 0 ? "HIDDEN(silent)" : (vis > 0 ? "visible" : "no-window")) : "disabled";
                Console.WriteLine(string.Format("{0,-16} {1,-12} {2,-18} {3}",
                    it.name, (procs > 0 ? procs + " proc" : "-"), "(" + vis + "/" + hid + ")", st));
            }
            Console.WriteLine();
            int e = Core.EnginePid();
            Console.WriteLine("engine (ssm-engine): " + (e > 0 ? "running (pid " + e + ")" : "not running"));
        }

        // ---------- show ----------
        static void DoShow(string[] a)
        {
            string nm = a.Length >= 2 ? a[1] : null;
            if (nm == null) { Console.WriteLine("usage: ssm show <name>"); return; }
            var cfg = Core.Load();
            var it = Core.FindItem(cfg, nm);
            if (it == null) { Console.WriteLine("no item named '" + nm + "'"); return; }
            int n = Core.ShowWindows(it.processName, it.windowTitle);
            Core.Log("cli show '" + nm + "': restored " + n + " window(s)");
            Console.WriteLine("restored " + n + " window(s) for '" + nm + "'");
        }

        // ---------- start: one-shot pass (no engine needed) ----------
        static void DoStart()
        {
            Core.Log("cli start (one-shot)");
            Core.DoPass(Core.Load());
            Console.WriteLine("one-shot pass complete. 'ssm status' to verify.");
        }

        // ---------- run: spawn the detached resident engine, CLI exits ----------
        static void DoRun()
        {
            int existing = Core.EnginePid();
            if (existing > 0)
            {
                Console.WriteLine("engine already running (pid " + existing + "); not starting another");
                return;
            }
            string exe = Core.EngineExe();
            if (!File.Exists(exe))
            {
                Console.WriteLine("engine not found: " + exe);
                Console.WriteLine("run build.ps1 first (or place ssm-engine.exe next to ssm.exe)");
                return;
            }
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForInputIdle(2000);
                    Core.Log("cli run: spawned engine pid=" + p.Id);
                    Console.WriteLine("engine started (pid " + p.Id + "); silent window in progress, CLI exiting");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("failed to start engine: " + ex.Message);
            }
        }

        // ---------- stop ----------
        static void DoStop()
        {
            string msg = "";
            bool killed = false;
            try
            {
                int pid = Core.EnginePid();
                if (pid > 0)
                {
                    Process.GetProcessById(pid).Kill();
                    killed = true;
                    msg = "stopped engine pid " + pid;
                }
            }
            catch { }
            try
            {
                foreach (var p in Process.GetProcessesByName(Core.EngineProcName))
                {
                    try { p.Kill(); killed = true; msg += " killed-by-name " + p.Id; } catch { }
                }
            }
            catch { }
            if (!killed && msg == "") msg = "engine not running";
            try { Directory.CreateDirectory(Path.GetDirectoryName(Core.StopPath)); File.WriteAllText(Core.StopPath, DateTime.Now + "  " + msg + "\n"); } catch { }
            Core.Log("cli stop: " + msg);
            Console.WriteLine("stop: " + msg);
        }

        // ---------- help ----------
        static void Help()
        {
            Console.WriteLine("ssm — SilentStartManager V6 CLI (console). Engine = ssm-engine.exe (no window).");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  scan [keyword]   list installed programs (registry Uninstall) with guessed exe");
            Console.WriteLine("  add <exe> [--name n] [--title t] [--window ms]   add a managed item (next logon)");
            Console.WriteLine("  remove <name>    remove a managed item (next logon)");
            Console.WriteLine("  list             show configured items");
            Console.WriteLine("  status            show running / window(hidden) state + engine");
            Console.WriteLine("  show <name>       restore a hidden window");
            Console.WriteLine("  start             one-shot: start-if-needed + hide all enabled, then exit");
            Console.WriteLine("  run               spawn the resident engine (detached), then CLI exits");
            Console.WriteLine("  stop              stop the engine");
            Console.WriteLine();
            Console.WriteLine("Config: " + Core.CfgPath);
            Console.WriteLine("Log:    " + Core.LogPath);
        }
    }
}
