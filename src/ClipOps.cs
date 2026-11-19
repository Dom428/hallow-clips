// Hallow Clips - working with saved clips (v1.2): trim without re-encoding, a small copy for Discord, rename.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HallowClips
{
    public static class ClipOps
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double d) { return d.ToString("0.###", Inv); }

        public static string Probe(string ffmpeg) { var p = Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe.exe"); return File.Exists(p) ? p : null; }

        // runs FFmpeg/ffprobe hidden and returns stdout (stderr in err); null when it couldn't run
        public static string Run(string exe, string args, int timeoutMs, out string err, out int exit)
        {
            err = ""; exit = -1;
            try
            {
                using (var p = new Process { StartInfo = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 } })
                {
                    var eb = new StringBuilder();
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (eb) { eb.AppendLine(e.Data); if (eb.Length > 6000) eb.Remove(0, eb.Length - 6000); } };
                    p.Start(); Job.Add(p); p.BeginErrorReadLine();
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    var o = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } err = "It took too long."; return null; }
                    p.WaitForExit(); exit = p.ExitCode; lock (eb) err = eb.ToString().Trim();
                    return o.Result;
                }
            }
            catch (Exception e) { err = e.Message; return null; }
        }

        // ---- what's in a clip: length, video codec, number of sound tracks, and the keyframes (where a copy-cut can start)
        public class Info { public double Duration, VideoDuration, Fps; public string Codec = ""; public int AudioTracks, Width, Height, SampleRate; public List<double> Keys = new List<double>(); }
        public static Info Inspect(string ffmpeg, string clip)
        {
            var info = new Info(); var probe = Probe(ffmpeg); if (probe == null) return info;
            string err; int exit;
            var s = Run(probe, "-v error -show_entries format=duration:stream=codec_type,codec_name,width,height,avg_frame_rate,sample_rate,duration -of compact=p=0:nk=0 \"" + clip + "\"", 15000, out err, out exit) ?? "";
            foreach (var line in s.Split('\n'))
            {
                Func<string, string> val = key => { var m = System.Text.RegularExpressions.Regex.Match(line, "(?:^|\\|)" + key + "=([^|\\r\\n]+)"); return m.Success ? m.Groups[1].Value.Trim() : ""; };
                if (line.Contains("codec_type=video") && info.Codec == "")
                {
                    info.Codec = val("codec_name"); int.TryParse(val("width"), out info.Width); int.TryParse(val("height"), out info.Height);
                    double.TryParse(val("duration"), NumberStyles.Float, Inv, out info.VideoDuration);
                    var fr = val("avg_frame_rate").Split('/'); double n, dd;
                    if (fr.Length == 2 && double.TryParse(fr[0], NumberStyles.Float, Inv, out n) && double.TryParse(fr[1], NumberStyles.Float, Inv, out dd) && dd > 0) info.Fps = Math.Round(n / dd, 2);
                }
                if (line.Contains("codec_type=audio")) { info.AudioTracks++; if (info.SampleRate == 0) int.TryParse(val("sample_rate"), out info.SampleRate); }
                var d = System.Text.RegularExpressions.Regex.Match(line, "^duration=([0-9.]+)\\s*$"); if (d.Success) double.TryParse(d.Groups[1].Value, NumberStyles.Float, Inv, out info.Duration);
            }
            // packet flags (no decoding): K = keyframe
            var k = Run(probe, "-v error -select_streams v:0 -show_entries packet=pts_time,flags -of csv=p=0 \"" + clip + "\"", 30000, out err, out exit) ?? "";
            foreach (var line in k.Split('\n'))
            {
                var parts = line.Trim().Split(','); double t;
                if (parts.Length >= 2 && parts[1].Contains("K") && double.TryParse(parts[0], NumberStyles.Float, Inv, out t)) info.Keys.Add(t);
            }
            info.Keys.Sort(); if (info.Keys.Count == 0) info.Keys.Add(0);
            return info;
        }

        // a free file name next to `file`: "<name> <suffix>.mp4", "<name> <suffix> 2.mp4", ...
        public static string Free(string dir, string name, string ext)
        {
            var f = Path.Combine(dir, name + ext);
            for (int i = 2; File.Exists(f); i++) f = Path.Combine(dir, name + " " + i + ext);
            return f;
        }

        // ---- trim: copies the part from `start` (a keyframe) to `end` - no re-encoding, so it's quick and loses nothing
        public static string Trim(string ffmpeg, string clip, string codec, double start, double end, out string error)
        {
            error = null;
            var dir = Path.GetDirectoryName(clip); var file = Free(dir, Path.GetFileNameWithoutExtension(clip) + " trimmed", ".mp4");
            var args = "-hide_banner -loglevel error -y -ss " + F(start) + " -i \"" + clip + "\" -t " + F(end - start) + " -map 0 -c copy -avoid_negative_ts make_zero " +
                (codec == "hevc" ? "-tag:v hvc1 " : "") + "-movflags +faststart \"" + file + "\"";
            string err; int exit; Run(ffmpeg, args, 120000, out err, out exit);
            if (exit != 0 || !File.Exists(file) || new FileInfo(file).Length == 0) { try { File.Delete(file); } catch { } error = "Trimming failed: " + Family.OneLine(err); return null; }
            try { File.SetLastWriteTime(file, File.GetLastWriteTime(clip).AddSeconds(1)); } catch { }   // sorts next to the original
            return file;
        }

        // ---- Copy for Discord: a re-encoded copy that fits under Discord's 10 MB upload limit.
        // Bitrate from the length ("2-pass-ish": if the first try comes out too big, it's redone a bit smaller).
        public const long DiscordLimit = 10L * 1000 * 1000;
        public static string DiscordDir { get { return Path.Combine(ClipSettings.Dir, "discord"); } }
        public static string ForDiscord(string ffmpeg, string clip, Info info, Action<string> progress, out string error)
        {
            error = null;
            if (info.Duration <= 0) { error = "Couldn't read the clip's length."; return null; }
            const int audioK = 96;
            double target = DiscordLimit * 0.93;   // room for the MP4 container
            double videoK = target * 8 / 1000 / info.Duration - audioK;
            if (videoK < 200) { error = "This clip is too long for Discord's 10 MB - trim it to under " + (int)(target * 8 / 1000 / (200 + audioK)) + " s first."; return null; }
            Directory.CreateDirectory(DiscordDir);
            foreach (var old in Directory.GetFiles(DiscordDir, "*.mp4")) try { if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-2)) File.Delete(old); } catch { }
            var file = Path.Combine(DiscordDir, Path.GetFileNameWithoutExtension(clip) + ".mp4");
            var enc = Recorder.Encoder(ffmpeg);
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                int v = (int)videoK;
                int h = v >= 5000 ? 1080 : v >= 2000 ? 720 : v >= 900 ? 540 : 360;
                int fps = v >= 4000 ? 60 : 30;
                string venc = enc == "h264_nvenc" ? "-c:v h264_nvenc -preset p5 -multipass fullres -rc cbr -b:v " + v + "k -maxrate " + v + "k -bufsize " + (v * 2) + "k"
                    : enc == "h264_amf" ? "-c:v h264_amf -rc cbr -b:v " + v + "k -maxrate " + v + "k -bufsize " + (v * 2) + "k"
                    : enc == "h264_qsv" ? "-c:v h264_qsv -b:v " + v + "k -maxrate " + v + "k -bufsize " + (v * 2) + "k"
                    : "-c:v h264_mf -b:v " + v + "k";
                var a = new StringBuilder("-hide_banner -loglevel error -y -i \"" + clip + "\" ");
                if (info.AudioTracks >= 2) a.Append("-filter_complex \"[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[a]\" -map 0:v:0 -map \"[a]\" ");   // game + mic in one track
                else a.Append("-map 0:v:0 -map 0:a:0? ");
                a.Append("-vf \"scale=-2:'min(" + h + ",ih)',fps=" + fps + ",format=yuv420p\" " + venc + " -c:a aac -b:a " + audioK + "k -ac 2 -movflags +faststart \"" + file + "\"");
                if (progress != null) progress(attempt == 1 ? "Making a Discord copy (" + h + "p)..." : "A bit smaller (try " + attempt + ")...");
                string err; int exit; Run(ffmpeg, a.ToString(), 600000, out err, out exit);
                if (exit != 0 || !File.Exists(file)) { error = "The Discord copy failed: " + Family.OneLine(err); return null; }
                long size = new FileInfo(file).Length;
                if (size <= DiscordLimit * 0.98) return file;
                videoK = (videoK + audioK) * (target / size) * 0.95 - audioK;   // scale down by how much it overshot
                if (videoK < 150) break;
            }
            error = "Couldn't get it under 10 MB - trim the clip first."; return null;
        }

        // ---- rename (same folder); the thumbnail follows
        public static string Rename(string clip, string newName, out string error)
        {
            error = null; newName = (newName ?? "").Trim();
            if (newName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) newName = newName.Substring(0, newName.Length - 4).Trim();
            if (newName.Length == 0) { error = "Type a name."; return null; }
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { error = "A name can't contain \\ / : * ? \" < > |"; return null; }
            var dest = Path.Combine(Path.GetDirectoryName(clip), newName + ".mp4");
            if (string.Equals(dest, clip, StringComparison.Ordinal)) return clip;
            if (File.Exists(dest) && !string.Equals(dest, clip, StringComparison.OrdinalIgnoreCase)) { error = "There's already a clip called " + newName + "."; return null; }
            try { File.Move(clip, dest); } catch (Exception e) { error = "Couldn't rename it: " + e.Message; return null; }
            try
            {
                var t0 = MainWindow.ThumbPath(clip); var t1 = MainWindow.ThumbPath(dest);
                if (File.Exists(t0)) { if (File.Exists(t1)) File.Delete(t1); File.Move(t0, t1); }
            }
            catch { }
            return dest;
        }
    }
}
