using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

// V6 shared core: config, P/Invoke, window ops, silent pass.
// Linked into both ssm.exe (CLI) and ssm-engine.exe (resident).
// Self-contained: config/log/pid live next to the running exe (project dir).
namespace SSM.Shared
{
    [DataContract]
    public class Item
    {
        [DataMember] public string name = "";
        [DataMember] public string exe = "";
        [DataMember] public string processName = "";
        [DataMember] public string windowTitle = "";
        [DataMember] public int silentWindowMs = 0; // 0 = auto (running->10000, fresh->20000)
        [DataMember] public bool enabled = true;
    }

    [DataContract]
    public class Config
    {
        [DataMember] public List<Item> items = new List<Item>();
    }

    public static class P
    {
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool EnumWindows(Delegate cb, IntPtr p);
        public delegate bool EP(IntPtr h, IntPtr p);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    }

    public static class Core
    {
        public const int SW_HIDE = 0, SW_SHOW = 5;
        public const string EngineProcName = "ssm-engine";
        public const string EngineExeName = "ssm-engine.exe";
        public const string MutexName = "SilentStartManagerResident";

        // Self-contained: config/log/pid live next to the running exe (project dir).
        static string Dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        public static string CfgPath = Path.Combine(Dir, "config.json");
        public static string LogPath = Path.Combine(Dir, "ssm.log");
        public static string PidPath = Path.Combine(Dir, "ssm-engine.pid");
        public static string StopPath = Path.Combine(Dir, "ssm-engine.stop.txt");

        // ---------- config io ----------
        public static Config Load()
        {
            try
            {
                if (File.Exists(CfgPath))
                {
                    using (var fs = File.OpenRead(CfgPath))
                    {
                        var ser = new DataContractJsonSerializer(typeof(Config));
                        var c = (Config)ser.ReadObject(fs);
                        if (c != null) return c;
                    }
                }
            }
            catch { }
            return new Config();
        }

        public static void Save(Config c)
        {
            Directory.CreateDirectory(Dir);
            using (var fs = File.Create(CfgPath))
            {
                var ser = new DataContractJsonSerializer(typeof(Config));
                ser.WriteObject(fs, c);
            }
        }

        public static void Log(string m)
        {
            try { Directory.CreateDirectory(Dir); File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + "  " + m + Environment.NewLine); }
            catch { }
        }

        // ---------- process / window helpers ----------
        public static int ProcCount(string proc)
        {
            try { var a = Process.GetProcessesByName(proc); int n = a.Length; foreach (var p in a) p.Dispose(); return n; }
            catch { return 0; }
        }

        public static void StartApp(Item it)
        {
            if (!File.Exists(it.exe)) { Log("exe missing for '" + it.name + "': " + it.exe); return; }
            try
            {
                var psi = new ProcessStartInfo(it.exe) { UseShellExecute = false };
                Process.Start(psi);
                Log("started '" + it.name + "' -> " + it.exe);
            }
            catch (Exception ex) { Log("start failed '" + it.name + "': " + ex.Message); }
        }

        public static List<IntPtr> CollectWindows(string proc, string title)
        {
            var res = new List<IntPtr>();
            var pids = new HashSet<uint>();
            try
            {
                var ps = Process.GetProcessesByName(proc);
                foreach (var p in ps) { pids.Add((uint)p.Id); p.Dispose(); }
            }
            catch { }
            if (pids.Count == 0) return res;
            P.EP cb = delegate(IntPtr h, IntPtr x)
            {
                uint pid;
                P.GetWindowThreadProcessId(h, out pid);
                if (pids.Contains(pid))
                {
                    if (string.IsNullOrEmpty(title)) { res.Add(h); }
                    else
                    {
                        var sb = new StringBuilder(512);
                        P.GetWindowText(h, sb, sb.Capacity);
                        string t = sb.ToString();
                        if (t.Contains(title)) res.Add(h);
                    }
                }
                return true;
            };
            P.EnumWindows(cb, IntPtr.Zero);
            return res;
        }

        public static int HideWindows(string proc, string title)
        {
            int n = 0;
            foreach (var h in CollectWindows(proc, title))
                if (P.IsWindowVisible(h) && P.ShowWindow(h, SW_HIDE)) n++;
            return n;
        }

        public static int ShowWindows(string proc, string title)
        {
            int n = 0;
            foreach (var h in CollectWindows(proc, title))
                if (!P.IsWindowVisible(h) && P.ShowWindow(h, SW_SHOW)) n++;
            return n;
        }

        // ---------- one pass: start-if-needed + poll-hide for all enabled items ----------
        public static void DoPass(Config cfg)
        {
            var enabled = new List<Item>();
            foreach (var i in cfg.items) if (i.enabled) enabled.Add(i);
            if (enabled.Count == 0) { Log("pass: no enabled items"); return; }

            int maxCap = 0;
            foreach (var it in enabled)
            {
                int c = ProcCount(it.processName);
                int cap = it.silentWindowMs > 0 ? it.silentWindowMs : (c > 0 ? 10000 : 20000);
                if (c == 0) StartApp(it);
                if (cap > maxCap) maxCap = cap;
            }

            int elapsed = 0, iv = 250, hides = 0;
            while (elapsed < maxCap)
            {
                Thread.Sleep(iv); elapsed += iv;
                foreach (var it in enabled) hides += HideWindows(it.processName, it.windowTitle);
            }
            Log(string.Format("pass done: {0} enabled item(s), {1}ms window, {2} hide-op(s)", enabled.Count, elapsed, hides));
        }

        public static Item FindItem(Config cfg, string name)
        {
            foreach (var i in cfg.items) if (string.Equals(i.name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return null;
        }

        // path of the engine exe (same directory as this assembly's deploy folder)
        public static string EngineExe()
        {
            string d = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(d, EngineExeName);
        }

        // is the engine alive? returns pid or 0
        public static int EnginePid()
        {
            try
            {
                if (File.Exists(PidPath))
                {
                    int pid = int.Parse(File.ReadAllText(PidPath).Trim());
                    var p = Process.GetProcessById(pid);
                    if (p.ProcessName.IndexOf(EngineProcName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return pid;
                }
            }
            catch { }
            // fallback: by process name (first one)
            try
            {
                var ps = Process.GetProcessesByName(EngineProcName);
                int pid = 0;
                foreach (var p in ps) { pid = p.Id; break; }
                if (pid > 0) return pid;
            }
            catch { }
            return 0;
        }
    }
}
