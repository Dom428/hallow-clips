// Hallow Clips - v1.7: highlight reels, clip titles and thumbnails, GIF and phone (9:16) exports.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HallowClips
{
    public static class Exports
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double d) { return d.ToString("0.###", Inv); }
        static string Arg(string s) { return "\"" + (s ?? "").Replace("\"", "'") + "\""; }
        static bool Ok(string file) { return File.Exists(file) && new FileInfo(file).Length > 0; }

        // ---- highlight reel: clips joined in the order given. A straight copy when every clip has the same format and nothing
        // is added; otherwise one re-encode on the graphics card (all clips scaled to the first one's size, sound mixed to one track).
        public static bool SameFormat(List<ClipOps.Info> infos)
        {
            var a = infos[0];
            return infos.All(i => i.Codec == a.Codec && i.Width == a.Width && i.Height == a.Height && Math.Abs(i.Fps - a.Fps) < 0.01 && i.AudioTracks == a.AudioTracks && i.SampleRate == a.SampleRate);
        }
        public const double Fade = 0.5, TitleLength = 2.5;
        public static string Reel(string ffmpeg, string folder, List<string> clips, List<ClipOps.Info> infos, bool crossfade, string title, int q, Action<string> progress, out string error)
        {
            error = null;
            if (clips.Count < 2 && string.IsNullOrWhiteSpace(title)) { error = "Pick at least two clips."; return null; }
            if (infos.Any(i => i.Duration <= 0 || i.Width == 0)) { error = "Couldn't read one of the clips."; return null; }
            var dir = Path.Combine(folder, "Highlights"); Directory.CreateDirectory(dir);
            var file = ClipOps.Free(dir, "Highlights " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"), ".mp4");
            string err; int exit; var tmp = Path.Combine(ClipSettings.Dir, "reel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
            try
            {
                if (!crossfade && string.IsNullOrWhiteSpace(title) && SameFormat(infos))
                {
                    if (progress != null) progress("Joining " + clips.Count + " clips (no re-encoding)...");
                    var list = Path.Combine(tmp, "list.txt");
                    File.WriteAllText(list, string.Join("\n", clips.Select(c => "file '" + c.Replace("\\", "/").Replace("'", "'\\''") + "'")));
                    ClipOps.Run(ffmpeg, "-hide_banner -loglevel error -y -f concat -safe 0 -i " + Arg(list) + " -map 0 -c copy " + (infos[0].Codec == "hevc" ? "-tag:v hvc1 " : "") + "-movflags +faststart " + Arg(file), 600000, out err, out exit);
                    if (exit == 0 && Ok(file)) return file;
                    try { File.Delete(file); } catch { }   // the copy didn't work after all: re-encode below
                }
                int W = infos[0].Width - infos[0].Width % 2, H = infos[0].Height - infos[0].Height % 2; double fps = infos[0].Fps > 0 ? Math.Min(infos[0].Fps, 60) : 60;
                var inputs = new StringBuilder(); var fc = new StringBuilder(); var parts = new List<double>(); int n = 0;
                Func<string> norm = () => "scale=" + W + ":" + H + ":force_original_aspect_ratio=decrease,pad=" + W + ":" + H + ":(ow-iw)/2:(oh-ih)/2,setsar=1,fps=" + F(fps) + ",format=yuv420p,settb=AVTB";
                const string anorm = "aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,asettb=AVTB";
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var tf = Path.Combine(tmp, "title.txt"); File.WriteAllText(tf, title.Trim(), new UTF8Encoding(false));
                    var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeuib.ttf");
                    Func<string, string> esc = p => p.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
                    var accent = Accent.Hex.Replace("#", "0x");
                    fc.Append("color=c=0x16121C:s=" + W + "x" + H + ":r=" + F(fps) + ":d=" + F(TitleLength) + ",drawtext=textfile='" + esc(tf) + "'" + (File.Exists(font) ? ":fontfile='" + esc(font) + "'" : "") +
                        ":fontcolor=white:fontsize=" + (H / 12) + ":x=(w-text_w)/2:y=(h-text_h)/2,drawbox=x=(iw-iw/5)/2:y=ih/2+" + (H / 12) + ":w=iw/5:h=" + Math.Max(4, H / 160) + ":color=" + accent + ":t=fill," + "format=yuv420p,setsar=1,settb=AVTB[v" + n + "];");
                    fc.Append("anullsrc=r=48000:cl=stereo,atrim=duration=" + F(TitleLength) + "," + anorm + "[a" + n + "];");
                    parts.Add(TitleLength); n++;
                }
                for (int i = 0; i < clips.Count; i++)
                {
                    inputs.Append("-i " + Arg(clips[i]) + " ");
                    var len = infos[i].VideoDuration > 0 ? Math.Min(infos[i].VideoDuration, infos[i].Duration) : infos[i].Duration;
                    fc.Append("[" + i + ":v:0]" + norm() + ",trim=duration=" + F(len) + ",setpts=PTS-STARTPTS[v" + n + "];");
                    if (infos[i].AudioTracks >= 2) fc.Append("[" + i + ":a:0][" + i + ":a:1]amix=inputs=2:duration=first:normalize=0," + anorm + ",apad,atrim=duration=" + F(len) + ",asetpts=PTS-STARTPTS[a" + n + "];");
                    else if (infos[i].AudioTracks == 1) fc.Append("[" + i + ":a:0]" + anorm + ",apad,atrim=duration=" + F(len) + ",asetpts=PTS-STARTPTS[a" + n + "];");
                    else fc.Append("anullsrc=r=48000:cl=stereo,atrim=duration=" + F(len) + "," + anorm + "[a" + n + "];");
                    parts.Add(len); n++;
                }
                if (crossfade && n > 1)
                {
                    string v = "v0", a = "a0"; double t = parts[0];
                    for (int k = 1; k < n; k++)
                    {
                        var off = t - Fade; var last = k == n - 1;
                        fc.Append("[" + v + "][v" + k + "]xfade=transition=fade:duration=" + F(Fade) + ":offset=" + F(off) + (last ? "[vout];" : "[vx" + k + "];"));
                        fc.Append("[" + a + "][a" + k + "]acrossfade=d=" + F(Fade) + (last ? "[aout];" : "[ax" + k + "];"));
                        v = "vx" + k; a = "ax" + k; t = off + parts[k];
                    }
                }
                else if (n > 1)
                {
                    for (int k = 0; k < n; k++) fc.Append("[v" + k + "][a" + k + "]");
                    fc.Append("concat=n=" + n + ":v=1:a=1[vout][aout];");
                }
                else fc.Append("[v0]null[vout];[a0]anull[aout];");
                var script = Path.Combine(tmp, "graph.txt"); File.WriteAllText(script, fc.ToString().TrimEnd(';'));
                var fam = Recorder.GpuFamily(ffmpeg);
                if (progress != null) progress("Making the highlight reel (" + clips.Count + " clips" + (crossfade ? ", crossfades" : "") + (string.IsNullOrWhiteSpace(title) ? "" : ", title") + ")...");
                ClipOps.Run(ffmpeg, "-hide_banner -loglevel error -y " + inputs + "-/filter_complex " + Arg(script) + " -map \"[vout]\" -map \"[aout]\" " +
                    Recorder.VideoArgs(fam, "h264", q) + " -c:a aac -b:a 192k -movflags +faststart " + Arg(file), 3600000, out err, out exit);
                if (exit != 0 || !Ok(file)) { try { File.Delete(file); } catch { } error = "The highlight reel failed: " + Family.OneLine(err); return null; }
                return file;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // ---- GIF: palettegen + paletteuse (two passes in one go), capped length, 15 fps
        public const int GifMaxSeconds = 15;
        public static string Gif(string ffmpeg, string clip, double start, double length, int width, out string error)
        {
            error = null; length = Math.Max(0.5, Math.Min(length, GifMaxSeconds));
            var file = ClipOps.Free(Path.GetDirectoryName(clip), Path.GetFileNameWithoutExtension(clip), ".gif");
            var vf = "fps=15,scale=" + width + ":-1:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle";
            string err; int exit;
            ClipOps.Run(ffmpeg, "-hide_banner -loglevel error -y -ss " + F(start) + " -t " + F(length) + " -i " + Arg(clip) + " -filter_complex \"" + vf + "\" -loop 0 " + Arg(file), 600000, out err, out exit);
            if (exit != 0 || !Ok(file)) { try { File.Delete(file); } catch { } error = "The GIF failed: " + Family.OneLine(err); return null; }
            return file;
        }

        // ---- phone: a 9:16 crop (pos 0 = left edge, 0.5 = centre, 1 = right edge), under ~50 MB
        public const long PhoneLimit = 50L * 1000 * 1000;
        public static string PhoneFilter(double pos) { pos = Math.Max(0, Math.Min(1, pos)); return "crop=w=trunc(ih*9/16/2)*2:h=trunc(ih/2)*2:x=(iw-ow)*" + F(pos) + ":y=0,scale=-2:'min(1920,ih)'"; }
        public static string Phone(string ffmpeg, string clip, ClipOps.Info info, double pos, Action<string> progress, out string error)
        {
            error = null;
            if (info.Duration <= 0) { error = "Couldn't read the clip's length."; return null; }
            var file = ClipOps.Free(Path.GetDirectoryName(clip), Path.GetFileNameWithoutExtension(clip) + " phone", ".mp4");
            const int audioK = 160; double target = PhoneLimit * 0.93;
            double videoK = Math.Min(12000, target * 8 / 1000 / info.Duration - audioK);
            if (videoK < 300) { error = "This clip is too long for a 50 MB phone version - trim it to under " + (int)(target * 8 / 1000 / (300 + audioK)) + " s first."; return null; }
            var fam = Recorder.GpuFamily(ffmpeg); var enc = "h264_" + fam;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                int v = (int)videoK;
                string venc = fam == "nvenc" ? "-c:v h264_nvenc -preset p5 -rc vbr -b:v " + v + "k -maxrate " + (v * 3 / 2) + "k -bufsize " + (v * 2) + "k"
                    : fam == "mf" ? "-c:v h264_mf -b:v " + v + "k" : "-c:v " + enc + " -b:v " + v + "k -maxrate " + (v * 3 / 2) + "k -bufsize " + (v * 2) + "k";
                var a = new StringBuilder("-hide_banner -loglevel error -y -i " + Arg(clip) + " ");
                if (info.AudioTracks >= 2) a.Append("-filter_complex \"[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[a]\" -map 0:v:0 -map \"[a]\" ");
                else a.Append("-map 0:v:0 -map 0:a:0? ");
                a.Append("-vf \"" + PhoneFilter(pos) + ",format=yuv420p\" " + venc + " -c:a aac -b:a " + audioK + "k -ac 2 -movflags +faststart " + Arg(file));
                if (progress != null) progress(attempt == 1 ? "Making the phone version..." : "A bit smaller (try " + attempt + ")...");
                string err; int exit; ClipOps.Run(ffmpeg, a.ToString(), 1800000, out err, out exit);
                if (exit != 0 || !Ok(file)) { try { File.Delete(file); } catch { } error = "The phone version failed: " + Family.OneLine(err); return null; }
                long size = new FileInfo(file).Length;
                if (size <= PhoneLimit) return file;
                videoK = (videoK + audioK) * (target / size) * 0.95 - audioK;
            }
            try { File.Delete(file); } catch { }
            error = "Couldn't get it under 50 MB - trim the clip first."; return null;
        }

        // ---- title: stored in the MP4 itself (title metadata), rewritten without re-encoding
        public static bool SetTitle(string ffmpeg, string clip, string title, out string error)
        {
            error = null; var tmp = clip + ".title.tmp.mp4"; var when = File.GetLastWriteTime(clip);
            string err; int exit;
            ClipOps.Run(ffmpeg, "-hide_banner -loglevel error -y -i " + Arg(clip) + " -map 0 -c copy -map_metadata 0 -metadata title=" + Arg((title ?? "").Trim()) +
                (VideoCodec(ffmpeg, clip) == "hevc" ? " -tag:v hvc1" : "") + " -movflags +faststart " + Arg(tmp), 600000, out err, out exit);
            if (exit != 0 || !Ok(tmp)) { try { File.Delete(tmp); } catch { } error = "Couldn't set the title: " + Family.OneLine(err); return false; }
            try { File.Replace(tmp, clip, null); File.SetLastWriteTime(clip, when); }   // keeps its place in the list
            catch (Exception e) { try { File.Delete(tmp); } catch { } error = "Couldn't set the title: " + e.Message; return false; }
            return true;
        }

        static string VideoCodec(string ffmpeg, string clip)
        {
            var p = ClipOps.Probe(ffmpeg); if (p == null) return "";
            string err; int exit; return (ClipOps.Run(p, "-v error -select_streams v:0 -show_entries stream=codec_name -of csv=p=0 " + Arg(clip), 15000, out err, out exit) ?? "").Trim();
        }

        // ---- thumbnail from a chosen moment
        public static bool ThumbAt(string ffmpeg, string clip, double t, string jpg)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(jpg)); } catch { }
            var tmp = jpg + ".new.jpg"; string err; int exit;
            ClipOps.Run(ffmpeg, "-hide_banner -loglevel error -y -ss " + F(t) + " -i " + Arg(clip) + " -frames:v 1 -vf scale=256:-1 " + Arg(tmp), 30000, out err, out exit);
            if (!Ok(tmp)) return false;
            try { if (File.Exists(jpg)) File.Delete(jpg); File.Move(tmp, jpg); return true; } catch { return false; }
        }
    }

}
