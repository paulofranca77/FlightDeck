using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace FlightDeck
{
    /// <summary>Loads and caches the real icon of each program.</summary>
    static class AppIcons
    {
        static readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Clear() { icons.Clear(); paths.Clear(); }

        public static Bitmap Get(AppEntry a, int size)
        {
            string path = Resolve(a);
            if (path == "") return null;
            return Load(path, size);
        }

        static string Resolve(AppEntry a)
        {
            string key = a.Id + "|" + a.Path + "|" + a.Process;
            string p;
            if (paths.TryGetValue(key, out p)) return p;
            p = "";
            try
            {
                string raw = Environment.ExpandEnvironmentVariables((a.Path ?? "").Trim().Trim('"'));
                if (!a.IsUrl && raw != "" && File.Exists(raw)) p = raw;
                else
                    foreach (var pid in Engine.Pids(a))
                        if (p == "") p = Native.GetProcessPath(pid);
            }
            catch { p = ""; }
            paths[key] = p;
            return p;
        }

        public static Bitmap Load(string path, int size)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = path + "|" + size;
            Bitmap b;
            if (icons.TryGetValue(key, out b)) return b;
            b = null;
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".exe" || ext == ".dll" || ext == ".ico")
                {
                    var h = new IntPtr[1]; var ids = new uint[1];
                    uint n = Native.PrivateExtractIcons(path, 0, size, size, h, ids, 1, 0);
                    if (n > 0 && n != 0xFFFFFFFF && h[0] != IntPtr.Zero)
                    {
                        using (var ic = Icon.FromHandle(h[0])) b = ic.ToBitmap();
                        Native.DestroyIcon(h[0]);
                    }
                }
                else
                {
                    using (var ic = Icon.ExtractAssociatedIcon(path)) if (ic != null) b = ic.ToBitmap();
                }
            }
            catch { b = null; }
            icons[key] = b;
            return b;
        }
    }
}
