using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace FlightDeck
{
    /// <summary>
    /// A tiny, read-only SimConnect client, used only to know when the flight has loaded
    /// ("When flight is loaded" apps). It connects to the simulator's SimConnect pipe, asks once a
    /// second for the camera state and closes the connection as soon as it isn't needed any more.
    /// It never changes anything in the simulator and needs no SimConnect.dll.
    /// </summary>
    static class SimLink
    {
        // CAMERA STATE 2 = cockpit, 3 = external/chase: the views a flight starts in (same in MSFS 2020 and 2024).
        // Menus, world map, hangar and loading screens use other values (12, 15, 32…).
        const int FirstFlightCamera = 2, LastFlightCamera = 3;
        const int StableSeconds = 5;     // the flight counts as loaded after this many seconds in a flight camera

        static Thread worker;
        static volatile bool wanted;
        static volatile bool inFlight;
        static volatile int camera = -1;
        public static Action<string> Debug;

        /// <summary>True once the user has been in the aircraft for a few seconds.</summary>
        public static bool FlightLoaded { get { return inFlight; } }
        public static int CameraState { get { return camera; } }

        /// <summary>Keeps a connection open (and retrying) until <see cref="Stop"/>.</summary>
        public static void Start()
        {
            wanted = true;
            if (worker != null && worker.IsAlive) return;
            inFlight = false; camera = -1;
            worker = new Thread(Loop) { IsBackground = true, Name = "FlightDeck SimConnect" };
            worker.Start();
        }

        public static void Stop()
        {
            wanted = false;
            inFlight = false;
        }

        static void D(string m) { var h = Debug; if (h != null) try { h(m); } catch { } }

        static void Loop()
        {
            while (wanted)
            {
                string pipe = Native.SimConnectPipe();
                if (pipe == null) { inFlight = false; Sleep(3000); continue; }
                try { Session(pipe); }
                catch (Exception ex) { D("SimConnect: " + ex.GetType().Name + " — " + ex.Message); }
                inFlight = false; camera = -1;
                Sleep(3000);
            }
        }

        static void Sleep(int ms) { for (int i = 0; i < ms / 100 && wanted; i++) Thread.Sleep(100); }

        static void Session(string pipeName)
        {
            using (var p = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                p.Connect(2000);
                int sendId = 0;
                Send(p, 0x01, ref sendId, w =>                 // Open
                {
                    Str(w, "FlightDeck", 256);
                    w.Write(0u); w.Write((byte)0);
                    w.Write(Encoding.ASCII.GetBytes("HK\0"));   // MSFS protocol (2020 and 2024 accept it)
                    w.Write(11u); w.Write(0u); w.Write(62651u); w.Write(3u);
                });

                bool open = false;
                int stable = 0;
                var opened = DateTime.Now;
                while (wanted)
                {
                    byte[] msg = Read(p);
                    if (msg == null) { D("SimConnect: connection closed by the simulator"); return; }
                    uint id = BitConverter.ToUInt32(msg, 8);
                    switch (id)
                    {
                        case 2:   // OPEN
                            open = true;
                            D("SimConnect: connected");
                            Send(p, 0x0C, ref sendId, w =>          // AddToDataDefinition
                            {
                                w.Write(1u); Str(w, "CAMERA STATE", 256); Str(w, "number", 256);
                                w.Write(4u);  // FLOAT64
                                w.Write(0f); w.Write(0xFFFFFFFFu);
                            });
                            Send(p, 0x0E, ref sendId, w =>          // RequestDataOnSimObject, once a second
                            {
                                w.Write(1u); w.Write(1u); w.Write(0u); w.Write(4u /*SECOND*/);
                                w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);
                            });
                            break;
                        case 3:   // QUIT
                            D("SimConnect: the simulator is closing");
                            return;
                        case 1:   // EXCEPTION
                            D("SimConnect: exception " + (msg.Length >= 16 ? BitConverter.ToUInt32(msg, 12).ToString() : "?"));
                            break;
                        case 8:   // SIMOBJECT_DATA: request, object, define, flags, entry, outOf, count, then the double
                            if (msg.Length >= 12 + 28 + 8)
                            {
                                int cam = (int)Math.Round(BitConverter.ToDouble(msg, 12 + 28));
                                if (cam != camera) D("SimConnect: camera state " + cam);
                                camera = cam;
                                stable = cam >= FirstFlightCamera && cam <= LastFlightCamera ? stable + 1 : 0;
                                if (stable >= StableSeconds) inFlight = true;   // stays true until the sim disconnects
                            }
                            break;
                    }
                    if (!open && (DateTime.Now - opened).TotalSeconds > 10) { D("SimConnect: no answer to Open"); return; }
                }
            }
        }

        static void Str(BinaryWriter w, string s, int len)
        {
            var b = new byte[len];
            var src = Encoding.ASCII.GetBytes(s);
            Array.Copy(src, b, Math.Min(src.Length, len - 1));
            w.Write(b);
        }

        /// <summary>Packet: size, protocol, 0xF0000000 | type, send id, payload.</summary>
        static void Send(Stream s, uint type, ref int sendId, Action<BinaryWriter> body)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(0u); w.Write(5u); w.Write(0xF0000000u | type); w.Write((uint)sendId++);
                body(w);
                w.Flush();
                var b = ms.ToArray();
                BitConverter.GetBytes((uint)b.Length).CopyTo(b, 0);
                s.Write(b, 0, b.Length);
                s.Flush();
            }
        }

        /// <summary>One whole message from the simulator: size, protocol, message id, payload. Null when closed.</summary>
        static byte[] Read(Stream s)
        {
            var head = new byte[4];
            if (!Fill(s, head, 0, 4)) return null;
            int size = BitConverter.ToInt32(head, 0);
            if (size < 12 || size > 1 << 20) throw new IOException("bad message size " + size);
            var msg = new byte[size];
            Array.Copy(head, msg, 4);
            return Fill(s, msg, 4, size - 4) ? msg : null;
        }

        static bool Fill(Stream s, byte[] b, int off, int count)
        {
            while (count > 0)
            {
                int n = s.Read(b, off, count);
                if (n <= 0) return false;
                off += n; count -= n;
            }
            return true;
        }
    }
}
