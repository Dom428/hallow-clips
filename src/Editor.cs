// Hallow Clips - v1.13: the clip editor. Trim (lossless at keyframes, or frame-exact), cut out a part in the middle,
// slow motion (or 2x) for a part, and blur a box (names, chat). Anything past a keyframe trim is one re-encode on the
// graphics card; the original clip always stays. The plan and the FFmpeg graph are plain code so the tests can check them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HallowClips
{
    public class EditPlan
    {
        public double Start, End;                       // the part kept
        public double CutFrom = -1, CutTo = -1;         // a part taken out of the middle (-1 = none)
        public double SlowFrom = -1, SlowTo = -1;       // a part played at Speed (-1 = none)
        public double Speed = 1;                        // 0.25, 0.5, 2
        public double[] Blur;                           // x, y, w, h as parts of the picture (0..1); null = none
        public bool Exact;                              // cut at any frame (re-encoded) instead of at keyframes
        public const double MinPart = 0.05;
        public bool HasCut { get { return CutFrom >= 0 && Math.Min(CutTo, End) - Math.Max(CutFrom, Start) > MinPart; } }
        public bool HasSpeed { get { return Math.Abs(Speed - 1) > 0.001 && SlowFrom >= 0 && Math.Min(SlowTo, End) - Math.Max(SlowFrom, Start) > MinPart; } }
        public bool HasBlur { get { return Blur != null && Blur.Length == 4 && Blur[2] >= 0.02 && Blur[3] >= 0.02; } }
        public bool Reencode { get { return Exact || HasCut || HasSpeed || HasBlur; } }

        public class Seg { public double A, B, Speed = 1; public double Length { get { return (B - A) / Speed; } } }
        // the pieces of the source that make the new clip, in order, each with its speed
        public List<Seg> Segments()
        {
            var keep = new List<Seg>();
            if (End - Start <= 0) return keep;
            if (HasCut)
            {
                double a = Math.Max(CutFrom, Start), b = Math.Min(CutTo, End);
                if (a - Start > 0.02) keep.Add(new Seg { A = Start, B = a });
                if (End - b > 0.02) keep.Add(new Seg { A = b, B = End });
            }
            else keep.Add(new Seg { A = Start, B = End });
            if (!HasSpeed) return keep;
            var r = new List<Seg>();
            foreach (var k in keep)
            {
                double a = Math.Max(k.A, SlowFrom), b = Math.Min(k.B, SlowTo);
                if (b - a <= 0.02) { r.Add(k); continue; }
                if (a - k.A > 0.02) r.Add(new Seg { A = k.A, B = a });
                r.Add(new Seg { A = a, B = b, Speed = Speed });
                if (k.B - b > 0.02) r.Add(new Seg { A = b, B = k.B });
            }
            return r;
        }
        public double OutLength() { return Segments().Sum(s => s.Length); }
        public string Describe()
        {
            var parts = new List<string>();
            if (HasCut) parts.Add("a part cut out");
            if (HasSpeed) parts.Add(Speed < 1 ? "slow motion" : Speed.ToString("0.##", CultureInfo.InvariantCulture) + "x speed");
            if (HasBlur) parts.Add("a blurred box");
            if (Exact && parts.Count == 0) parts.Add("frame-exact cut");
            return string.Join(", ", parts);
        }
    }

    public static class Editor
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double d) { return d.ToString("0.####", Inv); }
        // atempo takes 0.5-2 per step: 0.25 = two halves
        public static string Atempo(double speed)
        {
            var l = new List<string>(); double s = speed;
            while (s < 0.5 - 1e-9) { l.Add("atempo=0.5"); s /= 0.5; }
            while (s > 2 + 1e-9) { l.Add("atempo=2"); s /= 2; }
            if (Math.Abs(s - 1) > 1e-9) l.Add("atempo=" + F(s));
            return l.Count == 0 ? "anull" : string.Join(",", l);
        }
        // the blur box: crop it out (even sizes), blur it, put it back
        public static string BlurGraph(double[] b, string inLabel, string outLabel, int height)
        {
            double x = Clamp(b[0]), y = Clamp(b[1]), w = Math.Min(Clamp(b[2]), 1 - x), h = Math.Min(Clamp(b[3]), 1 - y);
            return "[" + inLabel + "]split[bm][bs];[bs]crop=w='max(2,trunc(iw*" + F(w) + "/2)*2)':h='max(2,trunc(ih*" + F(h) + "/2)*2)':x='trunc(iw*" + F(x) + "/2)*2':y='trunc(ih*" + F(y) + "/2)*2'," +
                "gblur=sigma=" + F(BlurSigma(height)) + ":steps=3[bb];" +
                "[bm][bb]overlay=x='trunc(main_w*" + F(x) + "/2)*2':y='trunc(main_h*" + F(y) + "/2)*2'[" + outLabel + "]";
        }
        // strong enough that names are unreadable at any size (gblur: boxblur is not in the LGPL FFmpeg)
        public static double BlurSigma(int height) { return Math.Max(6, (height > 0 ? height : 1080) * 0.025); }
        static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }

        // the filter graph for a plan: tracks = the sound tracks to mix (export choice), fps = the frame rate out
        public static string Graph(EditPlan p, List<int> tracks, double fps, int height = 0)
        {
            var segs = p.Segments(); int n = segs.Count; if (n == 0) return null;
            bool audio = tracks != null && tracks.Count > 0;
            var g = new StringBuilder();
            if (n == 1) g.Append("[0:v:0]null[vs0];"); else { g.Append("[0:v:0]split=" + n); for (int i = 0; i < n; i++) g.Append("[vs" + i + "]"); g.Append(";"); }
            if (audio)
            {
                g.Append(ClipOps.MixFilter(0, tracks, "am") + ";");
                g.Append("[am]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo");
                if (n == 1) g.Append("[as0];"); else { g.Append(",asplit=" + n); for (int i = 0; i < n; i++) g.Append("[as" + i + "]"); g.Append(";"); }
            }
            for (int i = 0; i < n; i++)
            {
                var s = segs[i];
                g.Append("[vs" + i + "]trim=start=" + F(s.A) + ":end=" + F(s.B) + ",setpts=" + (Math.Abs(s.Speed - 1) < 1e-9 ? "PTS-STARTPTS" : "(PTS-STARTPTS)/" + F(s.Speed)) + "[v" + i + "];");
                if (audio) g.Append("[as" + i + "]atrim=start=" + F(s.A) + ":end=" + F(s.B) + ",asetpts=PTS-STARTPTS," + Atempo(s.Speed) + "[a" + i + "];");
            }
            if (n == 1) g.Append("[v0]null[vc];" + (audio ? "[a0]anull[aout];" : ""));
            else
            {
                for (int i = 0; i < n; i++) g.Append("[v" + i + "]" + (audio ? "[a" + i + "]" : ""));
                g.Append("concat=n=" + n + ":v=1:a=" + (audio ? 1 : 0) + "[vc]" + (audio ? "[aout]" : "") + ";");
            }
            var last = "vc";
            if (p.HasBlur) { g.Append(BlurGraph(p.Blur, "vc", "vb", height) + ";"); last = "vb"; }
            g.Append("[" + last + "]fps=" + F(fps > 0 ? Math.Min(fps, 120) : 60) + ",format=yuv420p[vout]");
            return g.ToString();
        }

        public static string OutName(string clip, EditPlan p)
        {
            var name = Path.GetFileNameWithoutExtension(clip) + (p.HasCut || p.HasSpeed || p.HasBlur ? " edited" : " trimmed");
            return ClipOps.Free(Path.GetDirectoryName(clip), name, ".mp4");
        }

        // makes the edited copy (null + error when it fails); venc = the video encoder arguments (the graphics card's, or the CPU's in tests)
        public static string Run(string ffmpeg, string clip, ClipOps.Info info, EditPlan p, string venc, Action<string> progress, out string error)
        {
            error = null;
            if (!p.Reencode) return ClipOps.Trim(ffmpeg, clip, info.Codec, p.Start, p.End, out error);
            var graph = Graph(p, ClipOps.ExportTracks(info), info.Fps, info.Height);
            if (graph == null) { error = "Nothing is left to keep - move the start, end or cut."; return null; }
            var file = OutName(clip, p);
            var script = Path.Combine(Path.GetTempPath(), "hallowclips-edit-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(script, graph);
                bool audio = info.AudioTracks > 0 && ClipOps.ExportTracks(info).Count > 0;
                var args = "-hide_banner -loglevel error -y -i \"" + clip + "\" -/filter_complex \"" + script + "\" -map \"[vout]\" " + (audio ? "-map \"[aout]\" -c:a aac -b:a 192k " : "") +
                    venc + " -map_metadata 0 -map_chapters -1 -movflags +faststart \"" + file + "\"";
                if (progress != null) progress("Making the edited copy (" + p.Describe() + ") - re-encoded once...");
                string err; int exit; ClipOps.Run(ffmpeg, args, 3600000, out err, out exit);
                if (exit != 0 || !File.Exists(file) || new FileInfo(file).Length == 0) { try { File.Delete(file); } catch { } error = "The edit failed: " + Family.OneLine(err); return null; }
                try { File.SetLastWriteTime(file, File.GetLastWriteTime(clip).AddSeconds(1)); } catch { }   // sorts next to the original
                return file;
            }
            finally { try { File.Delete(script); } catch { } }
        }
        // the encoder for edits: the graphics card at the chosen quality; the CPU in tests
        public static string VideoArgs(string ffmpeg, string quality)
        {
            if (Recorder.TestScreen) return "-c:v mpeg4 -q:v 4";
            var q = quality == "max" ? 18 : quality == "medium" ? 28 : 23;
            return Recorder.VideoArgs(Recorder.GpuFamily(ffmpeg), "h264", q);
        }

        // ---- a strip of small pictures along the clip (one FFmpeg run; only keyframes are decoded when there are enough)
        public static string StripArgs(string clip, double duration, int count, bool keysOnly, string jpg)
        {
            var step = Math.Max(0.05, duration / count);
            return "-hide_banner -loglevel error -y " + (keysOnly ? "-skip_frame nokey " : "") + "-i \"" + clip + "\" -an -vf \"select='isnan(prev_selected_t)+gte(t-prev_selected_t\\," + F(step * 0.98) + ")',scale=160:90:force_original_aspect_ratio=decrease,pad=160:90:(ow-iw)/2:(oh-ih)/2,tile=" + count + "x1\" -fps_mode vfr -frames:v 1 \"" + jpg + "\"";
        }
        public static string Strip(string ffmpeg, string clip, ClipOps.Info info, int count, string jpg)
        {
            bool keys = info.Keys.Count >= count;
            string err; int exit; ClipOps.Run(ffmpeg, StripArgs(clip, info.Duration, count, keys, jpg), 60000, out err, out exit);
            return File.Exists(jpg) && new FileInfo(jpg).Length > 0 ? jpg : null;
        }
    }
}
