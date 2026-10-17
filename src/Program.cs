using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("FlightDeck")]
[assembly: AssemblyProduct("FlightDeck")]
[assembly: AssemblyDescription("Opens, arranges and closes your flight sim companion apps")]
[assembly: AssemblyVersion("2.2.2.0")]
[assembly: AssemblyFileVersion("2.2.2.0")]

namespace FlightDeck
{
    static class Program
    {
        static void Report(Exception ex) { Report(ex, true); }

        static void Report(Exception ex, bool fatal)
        {
            string text = ex == null ? "Unknown error" : ex.ToString();
            try
            {
                System.IO.Directory.CreateDirectory(ConfigStore.Folder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(ConfigStore.Folder, "crash.log"),
                    "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====" + Environment.NewLine + text + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            MessageBox.Show((fatal ? "FlightDeck hit an error and has to close." : "FlightDeck hit an error.") + "\n\n" + (ex != null ? ex.GetType().Name + ": " + ex.Message : "") +
                "\n\nDetails were saved to %AppData%\\FlightDeck\\crash.log", "FlightDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception);
            try { Run(args); }
            catch (Exception ex) { Report(ex); }
        }

        static void Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MainForm.CmdMessage = Native.RegisterWindowMessage("FlightDeck.Command.v2");

            int cmd = args.Any(a => a.Equals("--start", StringComparison.OrdinalIgnoreCase)) ? 1
                    : args.Any(a => a.Equals("--end", StringComparison.OrdinalIgnoreCase)) ? 2 : 0;

            var cfg = ConfigStore.Load();
            bool elevated = Util.IsElevated();
            var argList = args.ToList();

            if (elevated)
            {
                // started by a non-elevated FlightDeck: pick up what it wanted to do
                string pending = Elevation.TakePendingCommand();
                if (pending == "start" && cmd == 0) { cmd = 1; argList.Add("--start"); }
                else if (pending == "end" && cmd == 0) { cmd = 2; argList.Add("--end"); }
                if (cfg.RunAsAdmin) Elevation.EnsureTask(cfg);   // so next time there's no Windows prompt
            }
            else if (cfg.RunAsAdmin && !args.Contains("--elevated"))
            {
                if (Elevation.Relaunch(cfg, cmd == 1 ? "start" : cmd == 2 ? "end" : "")) return;
                // prompt cancelled → continue as a normal user
            }

            bool created;
            try { instance = new Mutex(true, "FlightDeck.SingleInstance.v2", out created); }
            catch (UnauthorizedAccessException) { created = false; }   // owned by an elevated FlightDeck
            if (!created)
            {
                // Already open: ask that window to show itself (and start/end the flight).
                Native.AllowSetForegroundWindow(-1);
                Native.PostMessage(Native.HWND_BROADCAST, MainForm.CmdMessage, new IntPtr(cmd), IntPtr.Zero);
                return;
            }
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = Math.Max(1f, g.DpiX / 96f);
                Color accent;
                Theme.BaseAccent = Theme.TryHex(cfg.AccentColor, out accent) ? accent : Theme.DefaultAccent;
                Theme.Apply(!cfg.LightMode);
                try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch { }   // apps we start inherit it
                Application.Run(new MainForm(cfg, argList.ToArray()));
            }
            finally { ReleaseSingleInstance(); }
        }

        static Mutex instance;

        /// <summary>Lets a freshly started (elevated) FlightDeck take over.</summary>
        public static void ReleaseSingleInstance()
        {
            try { if (instance != null) { instance.ReleaseMutex(); instance.Dispose(); } } catch { }
            instance = null;
        }
    }
}
