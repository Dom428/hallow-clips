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

    // v1.9: Windows 10 2004+ per-process capture (ActivateAudioInterfaceAsync with PROCESS_LOOPBACK)
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceAsyncOperation { void GetActivateResult(out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object iface); }
    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceCompletionHandler { void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op); }
    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject { }
    public class ActivateDone : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEvent Done = new ManualResetEvent(false);
        internal IAudioClient Client; public int Hr; public string Error;
        // the result is taken here, on Windows' own thread (casting it elsewhere fails)
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op)
        {
            try { object o; op.GetActivateResult(out Hr, out o); if (Hr == 0 && o != null) Client = (IAudioClient)o; }
            catch (Exception e) { Error = "[callback] " + e.Message; }
            finally { Done.Set(); }
        }
    }
    public static class ProcessAudio
    {
        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, IntPtr activationParams, IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation op);
        // Windows 10 2004 (build 19041) and newer
        public static bool Supported { get { try { using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) { int b; return int.TryParse(k.GetValue("CurrentBuild") as string, out b) && b >= 19041; } } catch { return false; } } }
        // an IAudioClient capturing what `pid` and its child processes play (null + error when it can't)
        internal static IAudioClient Activate(int pid, out string error)
        {
            error = null; IAudioClient result = null; string err = null;
            var t = new Thread(() =>
            {
                IntPtr prm = Marshal.AllocHGlobal(12), pv = Marshal.AllocHGlobal(24);
                try
                {
                    Marshal.WriteInt32(prm, 0, 1);       // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
                    Marshal.WriteInt32(prm, 4, pid);     // TargetProcessId
                    Marshal.WriteInt32(prm, 8, 0);       // PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
                    for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(pv, i, 0);
                    Marshal.WriteInt16(pv, 0, 65);       // VT_BLOB
                    Marshal.WriteInt32(pv, 8, 12); Marshal.WriteIntPtr(pv, 16, prm);
                    var h = new ActivateDone(); IActivateAudioInterfaceAsyncOperation op;
                    ActivateAudioInterfaceAsync("VAD\\Process_Loopback", typeof(IAudioClient).GUID, pv, h, out op);
                    if (!h.Done.WaitOne(5000)) { err = "Windows didn't answer"; return; }
                    if (h.Client == null) { err = h.Error ?? "per-app capture refused (0x" + h.Hr.ToString("X") + ")"; return; }
                    result = h.Client;
                }
                catch (Exception e) { err = e.Message; }
                finally { Marshal.FreeHGlobal(prm); Marshal.FreeHGlobal(pv); }
            });
            t.SetApartmentState(ApartmentState.MTA); t.IsBackground = true; t.Start(); t.Join(8000);
            error = err ?? (result == null ? "timed out" : null); return result;
        }
    }

    public class AudioPipe
    {
        public readonly string PipeName; public readonly bool Loopback;
        public int Rate = 48000, Channels = 2;   // the device's own format, read before FFmpeg starts
        public int Bits = 32; public string Format { get { return Bits == 16 ? "s16le" : "f32le"; } }
        public readonly int Pid; public string Title;   // v1.9: one app's sound (Pid > 0)
        public string Error;
        volatile bool stop; Thread thread; IAudioClient client; IAudioCaptureClient capture; NamedPipeServerStream pipe;

        public AudioPipe(bool loopback) { Loopback = loopback; PipeName = "hallowclips_" + (loopback ? "sys_" : "mic_") + Process.GetCurrentProcess().Id + "_" + Environment.TickCount; }
        public AudioPipe(int pid, string title) { Loopback = true; Pid = pid; Title = title; PipeName = "hallowclips_app" + pid + "_" + Process.GetCurrentProcess().Id + "_" + Environment.TickCount; }

        // opens the device (loopback = default speakers/headset, else the default mic); false if there is none
        public bool Open()
        {
            if (Pid > 0) return OpenProcess();
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

        // v1.9: one process tree's sound - 48 kHz stereo 16-bit, Windows converts whatever the app plays
        // Windows' per-app audio objects only work on MTA threads (the window's thread is STA), so set up there; Run is MTA too
        bool OpenProcess()
        {
            bool ok = false;
            var t = new Thread(() => ok = OpenProcessMta()); t.SetApartmentState(ApartmentState.MTA); t.IsBackground = true; t.Start();
            if (!t.Join(10000)) { Error = "Per-app capture didn't start in time"; return false; }
            return ok;
        }
        bool OpenProcessMta()
        {
            try
            {
                string err; client = ProcessAudio.Activate(Pid, out err);
                if (client == null) { Error = err; return false; }
                Rate = 48000; Channels = 2; Bits = 16;
                var fmt = Marshal.AllocHGlobal(18);
                try
                {
                    Marshal.WriteInt16(fmt, 0, 1); Marshal.WriteInt16(fmt, 2, 2); Marshal.WriteInt32(fmt, 4, 48000); Marshal.WriteInt32(fmt, 8, 48000 * 4);
                    Marshal.WriteInt16(fmt, 12, 4); Marshal.WriteInt16(fmt, 14, 16); Marshal.WriteInt16(fmt, 16, 0);
                    // LOOPBACK | EVENTCALLBACK | AUTOCONVERTPCM | SRC_DEFAULT_QUALITY
                    int hr = client.Initialize(0, 0x00020000 | 0x00040000 | unchecked((int)0x80000000) | 0x08000000, 2000000, 0, fmt, IntPtr.Zero);
                    if (hr != 0) { Error = "Couldn't start per-app capture (0x" + hr.ToString("X") + ")"; return false; }
                }
                finally { Marshal.FreeHGlobal(fmt); }
                evt = new AutoResetEvent(false); client.SetEventHandle(evt.SafeWaitHandle.DangerousGetHandle());
                var cid = typeof(IAudioCaptureClient).GUID; object c;
                if (client.GetService(ref cid, out c) != 0) { Error = "No capture service"; return false; }
                capture = (IAudioCaptureClient)c;
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.None, 0, 1 << 16);
                return true;
            }
            catch (Exception e) { Error = e.Message; return false; }
        }
        AutoResetEvent evt;

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
                int frameBytes = Bits / 8 * Channels; long written = 0; var clock = Stopwatch.StartNew(); bool wasLive = false;
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
