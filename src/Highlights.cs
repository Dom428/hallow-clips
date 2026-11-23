// Hallow Clips 1.16 - auto highlights from the game's own sound. Clips only listens to what Windows plays (the same sound that
// goes into the clip: the game's own track when it has one, otherwise everything) - it never looks inside the game, reads its
// memory or hooks it (anti-cheat).
//   * Kill sound: you teach it once from one of your clips (the hit marker / "eliminated" sound). Every 16 ms the sound is turned
//     into 24 frequency bands; what rises above the running background is compared with the taught sound (correlation, so the
//     game's volume doesn't matter).
//   * Loud moments: the sound jumps well above its own recent level (a guess - gunfire and music are loud too).
// A highlight becomes a marker (an MP4 chapter, like Ctrl+Alt+B) or, if you pick it, saves a clip by itself.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace HallowClips
{
    // ---- sound -> frames: 16 kHz mono, 512-sample Hann window every 256 samples (16 ms), 24 log-spaced bands 150 Hz - 7 kHz (dB)
    public sealed class SoundFrames
    {
        public const int Rate = 16000, Win = 512, Hop = 256, Bands = 24;
        public const double FrameSec = (double)Hop / Rate;
        static readonly int[] Edge = MakeEdges(); static readonly double[] Hann = MakeHann();
        static readonly double[] Cos = new double[Win / 2], Sin = new double[Win / 2]; static readonly int[] Rev = new int[Win];
        static SoundFrames()
        {
            for (int i = 0; i < Win / 2; i++) { Cos[i] = Math.Cos(-2 * Math.PI * i / Win); Sin[i] = Math.Sin(-2 * Math.PI * i / Win); }
            int bits = 9; for (int i = 0; i < Win; i++) { int r = 0; for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b); Rev[i] = r; }
        }
        static int[] MakeEdges()
        {
            var e = new int[Bands + 1]; double lo = 150.0 * Win / Rate, hi = 7000.0 * Win / Rate;
            for (int i = 0; i <= Bands; i++) e[i] = (int)Math.Round(lo * Math.Pow(hi / lo, i / (double)Bands));
            for (int i = 1; i <= Bands; i++) if (e[i] <= e[i - 1]) e[i] = e[i - 1] + 1;   // every band at least one FFT bin
            return e;
        }
        static double[] MakeHann() { var h = new double[Win]; for (int i = 0; i < Win; i++) h[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Win - 1)); return h; }

        readonly float[] buf = new float[Win]; int have;               // the newest samples (16 kHz)
        readonly double[] re = new double[Win], im = new double[Win];
        double acc, accN, pos;                                           // resampling: a box average for whole steps (48 / 32 kHz)...
        double lp, prev, nextOut; long inN; int lastRate;                // ...otherwise a low-pass and linear interpolation (44.1 kHz)
        public long Frames;
        public Action<float[], float> Frame;                             // band levels (dB) and the loudness (dBFS) of each frame

        // interleaved PCM straight from Windows: 16-bit integer or 32-bit float, any channel count and rate
        public void AddPcm(byte[] data, int bytes, int bits, int channels, int rate)
        {
            if (channels < 1 || rate < 8000) return;
            int fb = bits / 8 * channels, n = bytes / fb; double step = rate / (double)Rate;
            for (int i = 0; i < n; i++)
            {
                double s = 0; int o = i * fb;
                for (int c = 0; c < channels; c++, o += bits / 8)
                    s += bits == 16 ? BitConverter.ToInt16(data, o) / 32768.0 : BitConverter.ToSingle(data, o);
                if (rate % Rate == 0) Push(s / channels, step); else PushFrac(s / channels, rate);
            }
        }
        void PushFrac(double x, int rate)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
            if (rate != lastRate) { lastRate = rate; inN = 0; nextOut = 0; prev = lp = x; }
            lp += (x - lp) * (1 - Math.Exp(-2 * Math.PI * 6500.0 / rate));
            double step = rate / (double)Rate;
            while (nextOut <= inN) { double frac = inN == 0 ? 1 : nextOut - (inN - 1); Out((float)(prev + (lp - prev) * frac)); nextOut += step; }
            prev = lp; inN++;
        }
        public void AddMono16k(float[] s, int offset, int count) { for (int i = 0; i < count; i++) Push(s[offset + i], 1); }
        void Push(double x, double step)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
            acc += x; accN++; pos++;
            if (pos < step) return;
            pos -= step; var v = (float)(acc / accN); acc = 0; accN = 0;
            Out(v);
        }
        void Out(float v)
        {
            buf[have++] = v;
            if (have == Win) { Emit(); Array.Copy(buf, Hop, buf, 0, Win - Hop); have = Win - Hop; }
        }
        void Emit()
        {
            double sq = 0;
            for (int i = 0; i < Win; i++) { double x = buf[i]; if (i >= Win - Hop) sq += x * x; re[Rev[i]] = x * Hann[i]; im[Rev[i]] = 0; }
            for (int size = 2; size <= Win; size <<= 1)
            {
                int half = size >> 1, stepT = Win / size;
                for (int s = 0; s < Win; s += size)
                    for (int k = 0; k < half; k++)
                    {
                        double c = Cos[k * stepT], sn = Sin[k * stepT]; int a = s + k, b = a + half;
                        double tr = re[b] * c - im[b] * sn, ti = re[b] * sn + im[b] * c;
                        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                    }
            }
            var bands = new float[Bands];
            for (int bnd = 0; bnd < Bands; bnd++)
            {
                double p = 0; for (int k = Edge[bnd]; k < Edge[bnd + 1]; k++) p += re[k] * re[k] + im[k] * im[k];
                bands[bnd] = (float)Math.Max(-90, 10 * Math.Log10(p + 1e-12));
            }
            Frames++;
            var f = Frame; if (f != null) f(bands, (float)Math.Max(-120, 20 * Math.Log10(Math.Sqrt(sq / Hop) + 1e-9)));
        }
    }

    // ---- what is new in the sound: each band's level above its own background (the background falls fast, rises slowly)
    public sealed class Background
    {
        readonly double[] bg = new double[SoundFrames.Bands]; bool started;
        static readonly double Down = 1 - Math.Exp(-SoundFrames.FrameSec / 0.25), Up = 1 - Math.Exp(-SoundFrames.FrameSec / 4.0);
        public float[] Excess(float[] bands)
        {
            var e = new float[bands.Length];
            if (!started) { for (int i = 0; i < bands.Length; i++) bg[i] = bands[i]; started = true; return e; }
            for (int i = 0; i < bands.Length; i++)
            {
                double v = bands[i]; e[i] = (float)Math.Min(40, Math.Max(0, v - bg[i]));
                bg[i] += (v - bg[i]) * (v < bg[i] ? Down : Up);
            }
            return e;
        }
    }

    // ---- the taught kill sound (killsound.json in Clips' data folder)
    [DataContract]
    public class KillSound
    {
        [DataMember] public int Frames;                 // its length in 16 ms frames
        [DataMember] public float[] Data;               // Frames x 24 band levels above the background (dB)
        [DataMember] public string From = "";           // the clip it was taught from (file name)
        [DataMember] public double At, Length;          // where in that clip (s)
        [DataMember] public string Made = "";
        public static string FilePath { get { return Path.Combine(ClipSettings.Dir, "killsound.json"); } }
        public static KillSound Load()
        {
            try { if (File.Exists(FilePath)) using (var f = File.OpenRead(FilePath)) { var k = (KillSound)new DataContractJsonSerializer(typeof(KillSound)).ReadObject(f); if (k != null && k.Valid) return k; } } catch { }
            return null;
        }
        public bool Valid { get { return Frames >= 6 && Data != null && Data.Length == Frames * SoundFrames.Bands; } }
        public void Save() { if (ClipSettings.Frozen) return; Directory.CreateDirectory(ClipSettings.Dir); using (var f = File.Create(FilePath)) new DataContractJsonSerializer(typeof(KillSound)).WriteObject(f, this); }
        public static void Forget() { try { File.Delete(FilePath); } catch { } }
    }

    // ---- compares the newest frames with the kill sound
    public sealed class Matcher
    {
        readonly int k; readonly double[] t; readonly double thr, minMean; readonly float[][] ring; int next, filled, cool, pending = -1; double peak;
        public double Last;   // the last correlation (tests)
        public double Score;  // the best correlation of the last hit
        public int Late;      // a hit is reported this many frames after its best match (the start = now - Late - Length + 1)
        public int Length { get { return k; } }
        public Matcher(KillSound ks, double threshold)
        {
            k = ks.Frames; thr = threshold; ring = new float[k][]; t = new double[ks.Data.Length];
            double mean = ks.Data.Average(x => (double)x), ss = 0;
            for (int i = 0; i < t.Length; i++) { t[i] = ks.Data[i] - mean; ss += t[i] * t[i]; }
            var norm = Math.Sqrt(ss); for (int i = 0; i < t.Length; i++) t[i] = norm > 0 ? t[i] / norm : 0;
            minMean = mean * 0.3;   // the window must have about as much new sound as the kill sound had (not quiet noise)
        }
        public bool Step(float[] excess)
        {
            ring[next] = excess; next = (next + 1) % k; if (filled < k) filled++;
            Last = 0;
            if (filled < k) return false;
            if (cool > 0 && pending < 0) { cool--; return false; }
            double sum = 0; int nb = SoundFrames.Bands;
            for (int f = 0; f < k; f++) { var fr = ring[(next + f) % k]; for (int b = 0; b < nb; b++) sum += fr[b]; }
            double mean = sum / (k * nb);
            if (mean >= minMean && mean >= 0.5)
            {
                double dot = 0, ss = 0;
                for (int f = 0; f < k; f++) { var fr = ring[(next + f) % k]; for (int b = 0; b < nb; b++) { double d = fr[b] - mean; dot += d * t[f * nb + b]; ss += d * d; } }
                Last = ss > 0 ? dot / Math.Sqrt(ss) : 0;
            }
            // the first frames above the threshold are only partly lined up: follow the correlation to its peak, then report
            if (pending >= 0)
            {
                pending++;
                if (Last > peak) { peak = Last; pending = 0; return false; }
                if (pending < 3) return false;
                Score = peak; Late = pending; pending = -1; cool = k + 30 - Late;   // one hit per sound (and half a second after it)
                return true;
            }
            if (Last >= thr) { pending = 0; peak = Last; }
            return false;
        }
    }

    // ---- a jump in loudness well above the recent level
    public sealed class LoudDetector
    {
        readonly double jump; double floor = double.NaN; int cool;
        static readonly double Down = 1 - Math.Exp(-SoundFrames.FrameSec / 0.5), Up = 1 - Math.Exp(-SoundFrames.FrameSec / 6.0);
        public LoudDetector(double jumpDb) { jump = jumpDb; }
        public bool Step(float db)
        {
            if (double.IsNaN(floor)) { floor = Math.Max(db, -70); return false; }
            bool hit = cool == 0 && db > -36 && db - Math.Max(floor, -70) > jump;
            floor += (db - floor) * (db < floor ? Down : Up);
            if (cool > 0) cool--;
            if (hit) cool = (int)(3 / SoundFrames.FrameSec);
            return hit;
        }
    }

    public static class Highlights
    {
        public static readonly string[] Modes = { "off", "kill", "loud", "both" };
        public static string Mode(string m) { return Modes.Contains(m) ? m : "off"; }
        public static bool Kill(string m) { return m == "kill" || m == "both"; }
        public static bool Loud(string m) { return m == "loud" || m == "both"; }
        // sensitivity: fewer (stricter), normal, more
        public static double Threshold(string sens) { return sens == "more" ? 0.5 : sens == "fewer" ? 0.75 : 0.6; }   // tests: the kill sound scores 0.6-1.0 (0.6 with a gunshot right on it), other sounds under 0.25
        public static double LoudJump(string sens) { return sens == "more" ? 12 : sens == "fewer" ? 20 : 16; }
        public static string KindTitle(string kind) { return kind == "kill" ? "Kill sound" : kind == "loud" ? "Loud moment" : "Marker"; }

        public class Hit { public double At; public string Kind; public double Score; }
        // a whole clip's sound, analysed once (the teach window scans it again for every pick)
        public class Analysis
        {
            public float[] Mono = new float[0];                    // 16 kHz
            public List<float[]> Excess = new List<float[]>(); public List<float> Db = new List<float>();
            public double Seconds { get { return Mono.Length / (double)SoundFrames.Rate; } }
            // where a frame's window starts in the sound (s)
            public static double TimeOf(int frame) { return frame * SoundFrames.FrameSec; }
        }
        public static Analysis Analyze(float[] mono)
        {
            var a = new Analysis { Mono = mono }; var bg = new Background(); var sf = new SoundFrames();
            sf.Frame = (b, db) => { a.Excess.Add(bg.Excess(b)); a.Db.Add(db); };
            sf.AddMono16k(mono, 0, mono.Length); return a;
        }
        // the frames from `start` for `length` seconds become the kill sound; null + why when that part has nothing in it
        public static KillSound Teach(Analysis a, double start, double length, string from, out string error)
        {
            error = null;
            int f0 = (int)Math.Round(start / SoundFrames.FrameSec), k = Math.Max(6, (int)Math.Round(length / SoundFrames.FrameSec));
            if (f0 < 0) f0 = 0;
            if (f0 + k > a.Excess.Count) { error = "That's past the end of the clip's sound."; return null; }
            var data = new float[k * SoundFrames.Bands];
            for (int f = 0; f < k; f++) Array.Copy(a.Excess[f0 + f], 0, data, f * SoundFrames.Bands, SoundFrames.Bands);
            var mean = data.Average(x => (double)x); var max = data.Max();
            if (max < 20 || mean < 3.5) { error = "Nothing stands out there - pick the moment the kill sound starts."; return null; }
            return new KillSound { Frames = k, Data = data, From = from ?? "", At = Math.Round(f0 * SoundFrames.FrameSec, 3), Length = Math.Round(k * SoundFrames.FrameSec, 3), Made = DateTime.Now.ToString("s") };
        }
        // runs the same detectors as live over a clip's sound; At = where the sound starts (s)
        public static List<Hit> Scan(Analysis a, KillSound ks, bool loud, string sens)
        {
            var r = new List<Hit>(); var m = ks != null ? new Matcher(ks, Threshold(sens)) : null; var ld = loud ? new LoudDetector(LoudJump(sens)) : null;
            for (int i = 0; i < a.Excess.Count; i++)
            {
                if (m != null && m.Step(a.Excess[i])) r.Add(new Hit { At = Analysis.TimeOf(i - m.Late - m.Length + 1), Kind = "kill", Score = m.Score });
                if (ld != null && ld.Step(a.Db[i])) r.Add(new Hit { At = Analysis.TimeOf(i), Kind = "loud" });
            }
            return r;
        }
        // where the most new sound starts (a good first guess for the kill sound)
        public static double Strongest(Analysis a, double length)
        {
            int k = Math.Max(1, (int)(length / SoundFrames.FrameSec)), best = 0; double bestSum = -1;
            for (int i = 0; i + k <= a.Excess.Count; i++) { double s = 0; for (int f = 0; f < Math.Min(k, 4); f++) s += a.Excess[i + f].Sum(); if (s > bestSum) { bestSum = s; best = i; } }
            return Math.Max(0, Analysis.TimeOf(best) - 0.03);
        }
        // the clip's game sound: the "Game" track when it has one, otherwise the first (everything)
        public static int GameTrack(ClipOps.Info info)
        {
            for (int i = 0; i < info.TrackTitles.Count; i++) if (string.Equals(info.TrackTitles[i], "Game", StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }
        public static string TrackName(ClipOps.Info info, int track) { return track < info.TrackTitles.Count && !string.IsNullOrEmpty(info.TrackTitles[track]) ? info.TrackTitles[track] : "Sound track " + (track + 1); }
        // one sound track of a clip as 16 kHz mono (FFmpeg, in the background)
        public static float[] DecodeMono(string ffmpeg, string clip, int track, out string error)
        {
            error = null;
            try
            {
                var p = new Process { StartInfo = new ProcessStartInfo(ffmpeg, "-hide_banner -loglevel error -i \"" + clip + "\" -map 0:a:" + track + " -ac 1 -ar 16000 -f f32le -") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
                var err = new System.Text.StringBuilder(); p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.Start(); Job.Add(p); p.BeginErrorReadLine(); try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ms = new MemoryStream(); p.StandardOutput.BaseStream.CopyTo(ms); p.WaitForExit(30000);
                var b = ms.ToArray(); if (b.Length < 4 * SoundFrames.Win) { lock (err) error = "This clip has no sound to listen to" + (err.Length > 0 ? " (" + Family.OneLine(err.ToString()) + ")" : "") + "."; return null; }
                var f = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, f, 0, f.Length * 4); return f;
            }
            catch (Exception e) { error = e.Message; return null; }
        }
        // a WAV of part of the sound (the teach window's Listen button)
        public static byte[] Wav(float[] mono, double start, double length)
        {
            int a = Math.Max(0, (int)(start * SoundFrames.Rate)), n = Math.Max(0, Math.Min(mono.Length - a, (int)(length * SoundFrames.Rate)));
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(SoundFrames.Rate); w.Write(SoundFrames.Rate * 2); w.Write((short)2); w.Write((short)16); w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
            for (int i = 0; i < n; i++) w.Write((short)Math.Max(-32768, Math.Min(32767, mono[a + i] * 32767)));
            w.Flush(); return ms.ToArray();
        }
    }

    // ---- live: fed from the audio thread (a copy is queued), worked out on a low-priority thread of its own
    public sealed class LiveHighlights
    {
        readonly Queue<Tuple<byte[], int, int, int, DateTime>> q = new Queue<Tuple<byte[], int, int, int, DateTime>>(); int queuedBytes;
        readonly SoundFrames sf = new SoundFrames(); readonly Background bg = new Background(); readonly Matcher m; readonly LoudDetector ld;
        readonly Action<DateTime, string> hit; volatile bool stop; readonly Thread thread; DateTime packetTime;
        public long Heard, Dropped; public double WorkMs, AudioSec;   // what it cost (status, tests)
        public LiveHighlights(KillSound ks, bool loud, string sens, Action<DateTime, string> onHit)
        {
            hit = onHit; m = ks != null && ks.Valid ? new Matcher(ks, Highlights.Threshold(sens)) : null; ld = loud ? new LoudDetector(Highlights.LoudJump(sens)) : null;
            sf.Frame = (b, db) =>
            {
                var ex = bg.Excess(b);
                if (m != null && m.Step(ex)) { Heard++; hit(packetTime.AddSeconds(-(m.Late + m.Length) * SoundFrames.FrameSec), "kill"); }
                if (ld != null && ld.Step(db)) { Heard++; hit(packetTime, "loud"); }
            };
            thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "clips highlights" }; thread.Start();
        }
        public bool Listening { get { return m != null || ld != null; } }
        public void Feed(byte[] data, int bytes, int bits, int channels, int rate)
        {
            if (stop || bytes <= 0) return;
            var c = new byte[bytes]; Buffer.BlockCopy(data, 0, c, 0, bytes);
            lock (q)
            {
                if (queuedBytes > rate * channels * (bits / 8) * 2) { Dropped++; return; }   // more than 2 s behind: skip (never slows the recording)
                q.Enqueue(Tuple.Create(c, bits, channels, rate, DateTime.Now)); queuedBytes += bytes; Monitor.Pulse(q);
            }
        }
        void Run()
        {
            var sw = new Stopwatch();
            while (!stop)
            {
                Tuple<byte[], int, int, int, DateTime> p;
                lock (q) { while (q.Count == 0 && !stop) Monitor.Wait(q, 250); if (stop) break; p = q.Dequeue(); queuedBytes -= p.Item1.Length; }
                try
                {
                    sw.Restart();
                    int fb = p.Item2 / 8 * p.Item3; double secs = p.Item1.Length / (double)(fb * p.Item4);
                    packetTime = p.Item5.AddSeconds(secs);   // the end of this packet (a frame is reported as it completes)
                    sf.AddPcm(p.Item1, p.Item1.Length, p.Item2, p.Item3, p.Item4);
                    WorkMs += sw.Elapsed.TotalMilliseconds; AudioSec += secs;
                }
                catch { }
            }
        }
        public void Stop() { stop = true; lock (q) Monitor.PulseAll(q); }
    }
}
