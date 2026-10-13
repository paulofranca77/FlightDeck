using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace FlightDeck
{
    /// <summary>
    /// Runs FlightDeck as administrator (needed to move/close apps that run as administrator).
    /// After a one-time approval, a Windows scheduled task starts FlightDeck elevated without a UAC prompt.
    /// </summary>
    static class Elevation
    {
        const string TaskName = "FlightDeck\\FlightDeck (administrator)";
        static string PendingPath { get { return Path.Combine(ConfigStore.Folder, "pending-command.txt"); } }

        static int Schtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch { return -1; }
        }

        /// <summary>Creates/updates the task. Must run elevated.</summary>
        public static bool EnsureTask(Config cfg)
        {
            if (!Util.IsElevated()) return false;
            string exe = Application.ExecutablePath;
            if (cfg.ElevatedTaskPath == exe) return true;
            try
            {
                string user = WindowsIdentity.GetCurrent().Name;
                string xml =
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo><Description>Starts FlightDeck as administrator without a Windows prompt. Created by FlightDeck; turn off \"Run FlightDeck as administrator\" in its Settings to remove it.</Description></RegistrationInfo>\r\n" +
"  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
"  <Settings><MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
"<AllowHardTerminate>false</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>" +
"<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings></Settings>\r\n" +
"  <Actions Context=\"Author\"><Exec><Command>\"" + SecurityElement.Escape(exe) + "\"</Command><Arguments>--elevated</Arguments><WorkingDirectory>" + SecurityElement.Escape(Path.GetDirectoryName(exe)) + "</WorkingDirectory></Exec></Actions>\r\n" +
"</Task>\r\n";
                string tmp = Path.Combine(Path.GetTempPath(), "flightdeck-task.xml");
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                int rc = Schtasks("/create /tn \"" + TaskName + "\" /xml \"" + tmp + "\" /f");
                try { File.Delete(tmp); } catch { }
                if (rc != 0) return false;
                cfg.ElevatedTaskPath = exe;
                ConfigStore.Save(cfg);
                return true;
            }
            catch { return false; }
        }

        public static void RemoveTask(Config cfg)
        {
            if (Util.IsElevated()) Schtasks("/delete /tn \"" + TaskName + "\" /f");
            cfg.ElevatedTaskPath = null;
            ConfigStore.Save(cfg);
        }

        /// <summary>Starts an elevated FlightDeck that will run <paramref name="command"/> ("start", "end" or "").
        /// Returns false if the user cancelled the Windows prompt.</summary>
        public static bool Relaunch(Config cfg, string command)
        {
            try { Directory.CreateDirectory(ConfigStore.Folder); File.WriteAllText(PendingPath, command ?? ""); } catch { }
            // 1) no prompt: through the scheduled task (only if it points at this exe)
            if (cfg.ElevatedTaskPath == Application.ExecutablePath)
            {
                if (Schtasks("/run /tn \"" + TaskName + "\"") == 0) return true;
                cfg.ElevatedTaskPath = null;          // task missing or broken: recreate it next time we're elevated
                ConfigStore.Save(cfg);
            }
            // 2) classic Windows prompt (the elevated instance then creates the task for next time)
            try
            {
                using (Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--elevated") { UseShellExecute = true, Verb = "runas" })) { }
                return true;
            }
            catch (Win32Exception)
            {
                try { File.Delete(PendingPath); } catch { }
                return false;
            }
        }

        /// <summary>In the elevated instance: the command the non-elevated one asked for.</summary>
        public static string TakePendingCommand()
        {
            try
            {
                if (!File.Exists(PendingPath)) return null;
                bool fresh = (DateTime.Now - File.GetLastWriteTime(PendingPath)).TotalSeconds < 120;
                string c = File.ReadAllText(PendingPath).Trim();
                File.Delete(PendingPath);
                return fresh ? c : null;
            }
            catch { return null; }
        }
    }
}
