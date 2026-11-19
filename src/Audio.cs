// Hallow Clips - sound for the replay: what your speakers/headset play (WASAPI loopback) and your mic, each streamed to FFmpeg
// through a named pipe as raw 32-bit float audio. Silence is filled in, so the audio never stalls the video when nothing plays.
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;

namespace HallowClips
{
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator { int EnumAudioEndpoints(int f, int m, out IntPtr c); [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice d); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o); int OpenPropertyStore(int a, out IntPtr p); int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id); }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
        int GetBufferSize(out uint frames); int GetStreamLatency(out long l); int GetCurrentPadding(out uint p); int IsFormatSupported(int m, IntPtr f, out IntPtr c);
        [PreserveSig] int GetMixFormat(out IntPtr format); int GetDevicePeriod(out long d, out long m);
        [PreserveSig] int Start(); [PreserveSig] int Stop(); int Reset(); int SetEventHandle(IntPtr h);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong pos, out ulong qpc);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

    public class AudioPipe
    {
        public readonly string PipeName; public readonly bool Loopback;
        public int Rate = 48000, Channels = 2;   // the device's own format, read before FFmpeg starts
        public string Error;
        volatile bool stop; Thread thread; IAudioClient client; IAudioCaptureClient capture; NamedPipeServerStream pipe;

        public AudioPipe(bool loopback) { Loopback = loopback; PipeName = "hallowclips_" + (loopback ? "sys_" : "mic_") + Process.GetCurrentProcess().Id + "_" + Environment.TickCount; }

        // opens the device (loopback = default speakers/headset, else the default mic); false if there is none
        public bool Open()
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator(); IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(Loopback ? 0 : 1, 0, out dev) != 0 || dev == null) { Error = Loopback ? "No speakers/headset" : "No mic"; return false; }
                var iid = typeof(IAudioClient).GUID; object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) { Error = "Couldn't open the audio device"; return false; }
                client = (IAudioClient)o; IntPtr fmt;
                if (client.GetMixFormat(out fmt) != 0) { Error = "No audio format"; return false; }
                Channels = Marshal.ReadInt16(fmt, 2); Rate = Marshal.ReadInt32(fmt, 4); var bits = Marshal.ReadInt16(fmt, 14);
                if (bits != 32) { Error = "Unexpected audio format (" + bits + "-bit)"; return false; }   // shared-mode mix formats are 32-bit float
                int hr = client.Initialize(0, Loopback ? 0x00020000 : 0, 2000000, 0, fmt, IntPtr.Zero);   // 200 ms buffer
                Marshal.FreeCoTaskMem(fmt);
                if (hr != 0) { Error = "Couldn't start audio capture (0x" + hr.ToString("X") + ")"; return false; }
                var cid = typeof(IAudioCaptureClient).GUID; object c;
                if (client.GetService(ref cid, out c) != 0) { Error = "No capture service"; return false; }
                capture = (IAudioCaptureClient)c;
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.None, 0, 1 << 20);
                return true;
            }
            catch (Exception e) { Error = e.Message; return false; }
        }

        public void Start() { thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "clips audio" }; thread.Start(); }
        public void Stop() { stop = true; try { if (pipe != null) pipe.Dispose(); } catch { } }

        void Run()
        {
            try
            {
                pipe.WaitForConnection();   // FFmpeg opens the pipe when it starts
                client.Start();
                int frameBytes = 4 * Channels; long written = 0; var clock = Stopwatch.StartNew();
                var buf = new byte[frameBytes * Rate];   // up to 1 s per write
                while (!stop)
                {
                    uint next; capture.GetNextPacketSize(out next);
                    bool any = false;
                    while (next > 0 && !stop)
                    {
                        IntPtr data; uint frames, flags; ulong pos, qpc;
                        if (capture.GetBuffer(out data, out frames, out flags, out pos, out qpc) != 0) break;
                        int bytes = (int)frames * frameBytes; if (bytes > buf.Length) buf = new byte[bytes];
                        if ((flags & 0x2) != 0) Array.Clear(buf, 0, bytes); else Marshal.Copy(data, buf, 0, bytes);
                        capture.ReleaseBuffer(frames);
                        pipe.Write(buf, 0, bytes); written += frames; any = true;
                        capture.GetNextPacketSize(out next);
                    }
                    // nothing playing: Windows sends no loopback data, so keep the stream going with silence (keeps sound and picture in step)
                    long due = (long)(clock.Elapsed.TotalSeconds * Rate) - Rate / 20;
                    if (!any && due > written)
                    {
                        int n = (int)Math.Min(due - written, buf.Length / frameBytes);
                        Array.Clear(buf, 0, n * frameBytes); pipe.Write(buf, 0, n * frameBytes); written += n;
                    }
                    Thread.Sleep(8);
                }
            }
            catch { }   // FFmpeg stopped / pipe closed
            finally { try { client.Stop(); } catch { } }
        }
    }
}
