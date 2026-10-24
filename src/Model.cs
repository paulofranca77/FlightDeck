using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace FlightDeck
{
    public class AppEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Args { get; set; }
        public string Process { get; set; }
        public string Title { get; set; }
        public bool Enabled { get; set; }
        public bool Admin { get; set; }
        public bool CloseOnEnd { get; set; }
        public bool HasPos { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public bool Maximized { get; set; }
        public double Delay { get; set; }
        /// <summary>When Start Flight opens the app: "now", "delay" (StartDelay seconds later), "sim" (SimConnect is up) or "flight" (flight loaded).</summary>
        public string StartMode { get { return string.IsNullOrEmpty(startMode) ? "now" : startMode; } set { startMode = value; } }
        string startMode;
        public int StartDelay { get; set; }
        /// <summary>v2.3 setting, kept so older config files load as "sim".</summary>
        public bool WaitForSim
        {
            get { return StartMode == "sim"; }
            set { if (value) StartMode = "sim"; else if (StartMode == "sim") StartMode = "now"; }
        }

        [ScriptIgnore]
        public string StartLabel
        {
            get
            {
                switch (StartMode)
                {
                    case "delay": return "After " + StartDelay + " s";
                    case "sim": return "Sim ready";
                    case "flight": return "Flight loaded";
                    default: return "Right away";
                }
            }
        }

        /// <summary>"in 30 s", "when the simulator is ready"… for messages.</summary>
        [ScriptIgnore]
        public string WhenText
        {
            get
            {
                switch (StartMode)
                {
                    case "delay": return StartDelay + " s after Start Flight";
                    case "sim": return "when the simulator is ready (SimConnect)";
                    case "flight": return "when your flight has loaded";
                    default: return "right away";
                }
            }
        }

        public AppEntry()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = ""; Path = ""; Args = ""; Process = ""; Title = "";
            Enabled = true; CloseOnEnd = true; Delay = 2;
        }

        public AppEntry Clone() { return (AppEntry)MemberwiseClone(); }

        public void CopyFrom(AppEntry o)
        {
            foreach (var p in typeof(AppEntry).GetProperties()) if (p.CanWrite) p.SetValue(this, p.GetValue(o, null), null);
        }

        public void SetPlacement(WinInfo w)
        {
            X = w.Frame.Left; Y = w.Frame.Top; W = w.Frame.Width; H = w.Frame.Height;
            Maximized = w.Maximized; HasPos = true;
        }

        [ScriptIgnore]
        public bool IsUrl { get { return Regex.IsMatch(Path ?? "", @"^[a-zA-Z][a-zA-Z0-9+.-]*://"); } }
    }

    public class Profile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<AppEntry> Apps { get; set; }

        public Profile() { Id = Guid.NewGuid().ToString("N"); Name = "Default"; Apps = new List<AppEntry>(); }

        public Profile CloneAs(string name)
        {
            var p = new Profile { Name = name };
            foreach (var a in Apps) { var c = a.Clone(); c.Id = Guid.NewGuid().ToString("N"); p.Apps.Add(c); }
            return p;
        }
    }

    public class Config
    {
        public List<Profile> Profiles { get; set; }
        public string ActiveProfile { get; set; }
        public List<AppEntry> Apps { get; set; }      // legacy (single list) — migrated into a profile on load
        public int LaunchTimeout { get; set; }
        public int CloseTimeout { get; set; }
        public bool ForceClose { get; set; }
        public bool RunAsAdmin { get; set; }
        public bool TrayHintShown { get; set; }
        public bool LightMode { get; set; }
        public string IconRefreshVersion { get; set; }
        public string ElevatedTaskPath { get; set; }
        public bool SkipAdminPrompt { get; set; }
        public string AccentColor { get; set; }
        public string ShortcutIconsFor { get; set; }
        public bool ForceWhileSimRunning { get; set; }   // off = never force-close an app while the simulator is open
        public bool KeepOnTaskbar { get; set; }          // off = minimizing sends FlightDeck to the tray
        public int SafeCloseVersion { get; set; }

        public Config()
        {
            Profiles = new List<Profile> { new Profile() };
            LaunchTimeout = 120; CloseTimeout = 20; ForceClose = true; SafeCloseVersion = 1;
        }

        [ScriptIgnore]
        public Profile Active
        {
            get
            {
                if (Profiles == null || Profiles.Count == 0) Profiles = new List<Profile> { new Profile() };
                var p = Profiles.FirstOrDefault(x => x.Id == ActiveProfile);
                if (p == null) { p = Profiles[0]; ActiveProfile = p.Id; }
                return p;
            }
        }
    }

    static class ConfigStore
    {
        public static string Folder { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlightDeck"); } }
        static string FilePath { get { return System.IO.Path.Combine(Folder, "config.json"); } }

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(FilePath));
                    if (c != null)
                    {
                        if (c.Profiles == null) c.Profiles = new List<Profile>();
                        if (c.Profiles.Count == 0) c.Profiles.Add(new Profile { Name = "Default", Apps = c.Apps ?? new List<AppEntry>() });
                        c.Apps = null;
                        foreach (var p in c.Profiles)
                        {
                            if (string.IsNullOrEmpty(p.Id)) p.Id = Guid.NewGuid().ToString("N");
                            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Profile";
                            if (p.Apps == null) p.Apps = new List<AppEntry>();
                            foreach (var a in p.Apps) if (string.IsNullOrEmpty(a.Id)) a.Id = Guid.NewGuid().ToString("N");
                        }
                        if (c.LaunchTimeout <= 0) c.LaunchTimeout = 120;
                        if (c.CloseTimeout <= 0) c.CloseTimeout = 20;
                        if (c.SafeCloseVersion < 1)
                        {
                            // v2.3: apps get more time to close cleanly (a short wait forced add-ons closed while the sim was open)
                            if (c.CloseTimeout < 20) c.CloseTimeout = 20;
                            c.SafeCloseVersion = 1;
                        }
                        return c;
                    }
                }
            }
            catch
            {
                // keep the unreadable file instead of overwriting it
                try { File.Copy(FilePath, System.IO.Path.Combine(Folder, "config.unreadable.json"), true); } catch { }
            }
            return new Config();
        }

        public static void Save(Config c)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string json = Pretty(new JavaScriptSerializer().Serialize(c));
                File.WriteAllText(FilePath + ".tmp", json);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(FilePath + ".tmp", FilePath);
            }
            catch { }
        }

        // Minimal JSON indenter so the file stays readable/editable by hand.
        static string Pretty(string json)
        {
            var sb = new System.Text.StringBuilder();
            int ind = 0; bool str = false, esc = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (str)
                {
                    sb.Append(c);
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') str = false;
                    continue;
                }
                if (c == '"') { str = true; sb.Append(c); continue; }
                switch (c)
                {
                    case '{': case '[': sb.Append(c).Append('\n').Append(' ', ++ind * 2); break;
                    case '}': case ']': sb.Append('\n').Append(' ', --ind * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
