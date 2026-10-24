using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FlightDeck
{
    static class Engine
    {
        /// <summary>Simulator processes. FlightDeck never closes these, even if one is added as an app.</summary>
        public static readonly string[] SimProcesses = { "FlightSimulator2024", "FlightSimulator", "Prepar3D", "X-Plane", "X-Plane-x86_64" };

        /// <summary>The app's process name as Windows lists it ("Volanta", not "Volanta.exe").</summary>
        public static string ProcessName(AppEntry a)
        {
            string n = (a.Process ?? "").Trim();
            return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n;
        }

        /// <summary>The app's running processes. The caller disposes them.</summary>
        public static Process[] Processes(AppEntry a)
        {
            string n = ProcessName(a);
            return n == "" ? new Process[0] : Process.GetProcessesByName(n);
        }

        public static bool IsSim(AppEntry a) { var n = ProcessName(a); return SimProcesses.Any(x => x.Equals(n, StringComparison.OrdinalIgnoreCase)); }

        /// <summary>Name of the simulator that is running, or null.</summary>
        public static string RunningSim()
        {
            foreach (var n in SimProcesses)
            {
                var ps = Process.GetProcessesByName(n);
                foreach (var p in ps) p.Dispose();
                if (ps.Length > 0) return n.StartsWith("FlightSimulator") ? "Microsoft Flight Simulator" : n.StartsWith("X-Plane") ? "X-Plane" : n;
            }
            return null;
        }

        public static bool IsRunning(AppEntry a) { return Pids(a).Count > 0; }

        public static WinInfo FindWindow(AppEntry a, List<WinInfo> all = null)
        {
            var pids = Pids(a);
            if (pids.Count == 0) return null;

            WinInfo best = null;
            foreach (var w in all ?? Native.ListWindows())
            {
                if (!pids.Contains(w.Pid)) continue;
                if (!string.IsNullOrWhiteSpace(a.Title) && w.Title.IndexOf(a.Title.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (best == null || w.Area > best.Area) best = w;
            }
            return best;
        }

        public static HashSet<int> Pids(AppEntry a)
        {
            var set = new HashSet<int>();
            foreach (var p in Processes(a)) { set.Add(p.Id); p.Dispose(); }
            return set;
        }

        /// <summary>If the app is running but hiding in the tray, show its main window. Returns the window title shown, or null.</summary>
        public static string RevealFromTray(AppEntry a)
        {
            var pids = Pids(a);
            if (pids.Count == 0) return null;
            var c = Native.HiddenCandidates(pids, a.Title, a.Name);
            if (c.Count == 0) return null;
            Native.Reveal(c[0].Handle);
            return c[0].Title;
        }

        public static void Launch(AppEntry a, bool selfElevated)
        {
            string path = Environment.ExpandEnvironmentVariables((a.Path ?? "").Trim().Trim('"'));
            ProcessStartInfo psi;
            if (a.IsUrl)
            {
                psi = new ProcessStartInfo(path) { UseShellExecute = true };
            }
            else
            {
                if (!File.Exists(path)) throw new FileNotFoundException("file not found: " + path);
                bool isExe = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                if (selfElevated && !a.Admin && isExe && string.IsNullOrWhiteSpace(a.Args))
                {
                    // Start as a normal user even though FlightDeck is elevated.
                    psi = new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true };
                }
                else
                {
                    psi = new ProcessStartInfo(path, a.Args ?? "") { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(path) };
                    if (a.Admin && !selfElevated) psi.Verb = "runas";
                }
            }
            try { using (Process.Start(psi)) { } }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) throw new Exception("administrator prompt was cancelled");
                throw;
            }
        }
    }
}
