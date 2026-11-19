// Hallow Clips - sound for the replay: what your speakers/headset play (WASAPI loopback) and your mic, each streamed to FFmpeg
// through a named pipe as raw 32-bit float audio. Silence is filled in, so the audio never stalls the video when nothing plays.
using System;
using System.Collections.Generic;
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
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.None, 0, 1 << 16);   // small: FFmpeg gets fresh sound, not a backlog
                return true;
            }
            catch (Exception e) { Error = e.Message; return false; }
        }

        public void Start() { thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "clips audio" }; thread.Start(); }
        public void Stop() { stop = true; lock (queue) Monitor.PulseAll(queue); try { if (pipe != null) pipe.Dispose(); } catch { } }

        // v1.1: set when FFmpeg starts recording. Until then only silence is sent (FFmpeg reads a little while it starts up);
        // from then on the audio clock counts from the same moment as the picture.
        public volatile bool Live;
        // what happened, for recorder.log: frames from Windows, silence added, frames dropped, glitches Windows reported
        public long FramesIn, FramesPadded, FramesDropped, Glitches; public int StartupMs;   // StartupMs: FFmpeg opened the pipe this long before it started recording

        readonly Queue<byte[]> queue = new Queue<byte[]>(); int queued;
        void Enqueue(byte[] src, int bytes)
        {
            var b = new byte[bytes]; Buffer.BlockCopy(src, 0, b, 0, bytes);
            lock (queue)
            {
                while (queued > Rate * 4 * Channels * 5 && queue.Count > 0) queued -= queue.Dequeue().Length;   // FFmpeg stuck: keep at most 5 s
                queue.Enqueue(b); queued += bytes; Monitor.Pulse(queue);
            }
        }
        // FFmpeg reads the pipe on its own schedule; a separate writer means a slow read never makes us miss sound from Windows
        void Writer()
        {
            try
            {
                while (!stop)
                {
                    byte[] b;
                    lock (queue) { while (queue.Count == 0 && !stop) Monitor.Wait(queue, 200); if (stop) break; b = queue.Dequeue(); queued -= b.Length; }
                    pipe.Write(b, 0, b.Length);
                }
            }
            catch { }   // FFmpeg stopped / pipe closed
        }

        // Keeps the sound locked to the clock: whatever Windows delivers is passed on, gaps (nothing playing, glitches) are
        // filled with silence and anything that runs ahead is trimmed - so over hours the sound can't drift from the picture.
        void Run()
        {
            try
            {
                pipe.WaitForConnection();   // FFmpeg opens the pipe when it starts
                client.Start();
                new Thread(Writer) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "clips audio out" }.Start();
                int frameBytes = 4 * Channels; long written = 0; var clock = Stopwatch.StartNew(); bool wasLive = false;
                var buf = new byte[frameBytes * Rate];   // up to 1 s per packet
                long ahead = Rate / 6, behind = Rate / 25, keep = Rate / 100;   // trim past 166 ms ahead, fill past 40 ms behind (to 10 ms)
                while (!stop)
                {
                    if (Live && !wasLive)
                    {
                        // recording really starts now: drop the start-up silence still waiting and count from here
                        wasLive = true; lock (queue) { queue.Clear(); queued = 0; } StartupMs = (int)clock.ElapsedMilliseconds; clock.Restart(); written = 0;
                    }
                    uint next; capture.GetNextPacketSize(out next);
                    while (next > 0 && !stop)
                    {
                        IntPtr data; uint frames, flags; ulong pos, qpc;
                        if (capture.GetBuffer(out data, out frames, out flags, out pos, out qpc) != 0) break;
                        int bytes = (int)frames * frameBytes; if (bytes > buf.Length) buf = new byte[bytes];
                        if ((flags & 0x2) != 0) Array.Clear(buf, 0, bytes); else Marshal.Copy(data, buf, 0, bytes);
                        capture.ReleaseBuffer(frames);
                        if ((flags & 0x1) != 0) Glitches++;
                        FramesIn += frames;
                        long due = (long)(clock.Elapsed.TotalSeconds * Rate);
                        if (wasLive && written + frames > due + ahead) FramesDropped += frames;   // running ahead of the clock
                        else if (wasLive) { Enqueue(buf, bytes); written += frames; }
                        capture.GetNextPacketSize(out next);
                    }
                    long now = (long)(clock.Elapsed.TotalSeconds * Rate);
                    if (now - written > behind || !wasLive)
                    {
                        // nothing playing (Windows sends nothing then), a glitch, or still starting up: silence up to the clock
                        long n = Math.Min(now - keep - written, buf.Length / frameBytes);
                        if (n > 0) { Array.Clear(buf, 0, (int)n * frameBytes); Enqueue(buf, (int)n * frameBytes); written += n; if (wasLive) FramesPadded += n; }
                    }
                    Thread.Sleep(5);
                }
            }
            catch { }   // FFmpeg stopped / pipe closed
            finally { try { client.Stop(); } catch { } }
        }
    }
}
