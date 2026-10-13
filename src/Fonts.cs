using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FlightDeck
{
    /// <summary>Cal Sans Bold, display size (SIL Open Font License), embedded in the exe — used only for the FlightDeck wordmark.</summary>
    static class Fonts
    {
        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        static readonly PrivateFontCollection pfc = new PrivateFontCollection();
        static IntPtr mem = IntPtr.Zero;      // must stay alive for the whole process
        public static readonly FontFamily Cal = Load();

        static FontFamily Load()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("FlightDeck.Brand.ttf"))
                {
                    if (s == null) return null;
                    var data = new byte[s.Length];
                    s.Read(data, 0, data.Length);
                    mem = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, mem, data.Length);
                    pfc.AddMemoryFont(mem, data.Length);                 // for GDI+ (Font objects)
                    uint n = 0;
                    AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n);   // for GDI (TextRenderer, Labels)
                    return pfc.Families.Length > 0 ? pfc.Families[0] : null;
                }
            }
            catch { return null; }
        }

        public static Font Heading(float size)
        {
            try { if (Cal != null) return new Font(Cal, size, FontStyle.Regular); } catch { }
            return new Font("Segoe UI Semibold", size);
        }
    }
}
