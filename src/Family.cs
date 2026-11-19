// Hallow Clips - the Hallow family link (v1): a named pipe other Hallow apps talk to ("HallowFamily.Clips.<user>"),
// "HallowClips.exe --cmd <line>" for scripts, and status.json for apps that only read. Only this Windows user can open the pipe,
// and never over the network. Nothing secret goes over it.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowClips
{
    public static class Family
    {
        public static string PipeName(string app) { return "HallowFamily." + app + "." + Environment.UserName; }

        // ---- server: one line in, one line out. handler gets the line and returns the reply ("ok ...", "error ...").
        public static void Serve(string app, Func<string, string> handler)
        {
            new Thread(() =>
            {
                var name = PipeName(app); int fails = 0;
                while (true)
                {
                    NamedPipeServerStream pipe = null;
                    try
                    {
                        var sec = new PipeSecurity();
                        sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                        sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                        pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, sec);
                        pipe.WaitForConnection(); fails = 0;
                        var p = pipe; pipe = null;
                        new Thread(() => Handle(p, handler)) { IsBackground = true, Name = "family pipe" }.Start();
                    }
                    catch
                    {
                        if (pipe != null) try { pipe.Dispose(); } catch { }
                        Thread.Sleep(Math.Min(30000, 500 * ++fails));   // e.g. another copy owns the name
                    }
                }
            }) { IsBackground = true, Name = "family server" }.Start();
        }
        static void Handle(NamedPipeServerStream p, Func<string, string> handler)
        {
            try
            {
                var line = ReadLine(p, 2000); string reply;
                if (line == null) reply = "error no command";
                else { try { reply = handler(line.Trim()) ?? "error unknown command"; } catch (Exception e) { reply = "error " + OneLine(e.Message); } }
                var b = new UTF8Encoding(false).GetBytes(reply.Replace("\r", " ").Replace("\n", " ") + "\n");
                p.Write(b, 0, b.Length); p.Flush();
                try { p.WaitForPipeDrain(); } catch { }
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        public static string OneLine(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }

        // reads up to '\n' (or the end) within `ms`; null on timeout
        static string ReadLine(Stream s, int ms)
        {
            string result = null;
            var t = new Thread(() =>
            {
                try
                {
                    var buf = new List<byte>(); var one = new byte[1];
                    while (buf.Count < 65536) { int n = s.Read(one, 0, 1); if (n <= 0 || one[0] == (byte)'\n') break; buf.Add(one[0]); }
                    result = Encoding.UTF8.GetString(buf.ToArray()).TrimEnd('\r');
                }
                catch { }
            }) { IsBackground = true };
            t.Start();
            if (!t.Join(ms)) { try { s.Dispose(); } catch { } return null; }
            return result;
        }

        // ---- client: sends a line to another running Hallow app. null = not running (or no answer in time).
        public static string Send(string app, string line, int replyMs)
        {
            try
            {
                using (var c = new NamedPipeClientStream(".", PipeName(app), PipeDirection.InOut))
                {
                    c.Connect(1500);
                    var b = new UTF8Encoding(false).GetBytes(line + "\n"); c.Write(b, 0, b.Length); c.Flush();
                    return ReadLine(c, replyMs);
                }
            }
            catch { return null; }
        }

        // ---- "HallowClips.exe --cmd <line>": prints the reply. 0 = ok, 1 = error, 2 = Hallow Clips isn't running.
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
        public static int RunCmd(string line)
        {
            var h = GetStdHandle(-11);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) AttachConsole(-1);   // not redirected: print to the console it was started from
            string reply = string.IsNullOrWhiteSpace(line) ? "error no command" : Send("Clips", line.Trim(), 40000);
            int code = reply == null ? 2 : reply == "ok" || reply.StartsWith("ok ") ? 0 : 1;
            try
            {
                var o = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                o.WriteLine(reply ?? "error Hallow Clips isn't running");
            }
            catch { }
            return code;
        }

        // ---- status.json, written atomically (status.json.tmp, then replace)
        public static string Json(object o) { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(o); }
        public static string Time(DateTime t) { return t.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture); }
        static readonly object fileLock = new object();
        public static void WriteStatus(string json)
        {
            lock (fileLock)
            {
                try
                {
                    Directory.CreateDirectory(ClipSettings.Dir);
                    var f = Path.Combine(ClipSettings.Dir, "status.json"); var tmp = f + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    for (int i = 0; ; i++)
                    {
                        try { if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f); break; }
                        catch { if (i >= 5) throw; Thread.Sleep(50); }   // a reader has it open for a moment
                    }
                }
                catch { }
            }
        }
    }

    // reads an MP4's length from its header (moov > mvhd) - no FFmpeg needed
    public static class Mp4
    {
        public static double Duration(string file)
        {
            try
            {
                using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long end = f.Length, pos = 0;
                    while (pos + 8 <= end)
                    {
                        f.Position = pos; long size = U32(f); var type = Type(f); long head = 8;
                        if (size == 1) { size = (long)U64(f); head = 16; } else if (size == 0) size = end - pos;
                        if (size < head) return 0;
                        if (type == "moov") { end = pos + size; pos += head; continue; }   // step inside
                        if (type == "mvhd")
                        {
                            int ver = f.ReadByte(); f.Position += 3;
                            if (ver == 1) { f.Position += 16; long ts = U32(f); var d = U64(f); return ts > 0 ? d / (double)ts : 0; }
                            f.Position += 8; long ts0 = U32(f); long d0 = U32(f); return ts0 > 0 ? d0 / (double)ts0 : 0;
                        }
                        pos += size;
                    }
                }
            }
            catch { }
            return 0;
        }
        // v1.7: the title stored in the MP4 (moov > udta > meta > ilst > (c)nam > data), or null
        public static string Title(string file)
        {
            try
            {
                using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long end = f.Length, pos = 0;
                    while (pos + 8 <= end)
                    {
                        f.Position = pos; long size = U32(f); var tb = new byte[4]; f.Read(tb, 0, 4); long head = 8;
                        if (size == 1) { size = (long)U64(f); head = 16; } else if (size == 0) size = end - pos;
                        if (size < head || pos + size > f.Length) return null;
                        var type = Encoding.ASCII.GetString(tb);
                        if (type == "moov" || type == "udta" || type == "ilst") { end = pos + size; pos += head; continue; }
                        if (type == "meta") { end = pos + size; pos += head + 4; continue; }   // a full box: 4 bytes version/flags
                        if (tb[0] == 0xA9 && type.EndsWith("nam"))
                        {
                            f.Position = pos + head; long dsize = U32(f); var dt = new byte[4]; f.Read(dt, 0, 4);
                            if (Encoding.ASCII.GetString(dt) != "data" || dsize < 16 || dsize > 4096) return null;
                            f.Position += 8; var b = new byte[dsize - 16]; f.Read(b, 0, b.Length);
                            var s = Encoding.UTF8.GetString(b).Trim(); return s.Length == 0 ? null : s;
                        }
                        pos += size;
                    }
                }
            }
            catch { }
            return null;
        }
        static long U32(Stream s) { var b = new byte[4]; s.Read(b, 0, 4); return ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3]; }
        static ulong U64(Stream s) { return ((ulong)U32(s) << 32) | (ulong)U32(s); }
        static string Type(Stream s) { var b = new byte[4]; s.Read(b, 0, 4); return Encoding.ASCII.GetString(b); }
    }
}
