// Hallow Clips - settings and the replay buffer: FFmpeg grabs the screen (Desktop Duplication, on the GPU) and encodes it on the
// graphics card into 2-second pieces that keep overwriting each other; saving a clip joins the newest pieces without re-encoding.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace HallowClips
{
    [DataContract]
    public class ClipSettings
    {
        [DataMember] public bool On = true;              // replay buffer on
        [DataMember] public bool OnlyInGame = true;      // record only while a game runs
        [DataMember] public int Seconds = 30;            // how much a clip keeps
        [DataMember] public int Fps = 60;
        [DataMember] public string Quality = "high";     // medium, high, max
        [DataMember] public bool Mic = true;             // your mic, on its own audio track
        [DataMember] public int Screen = 0;              // which screen (Desktop Duplication output number)
        [DataMember] public string Folder = "";          // "" = Videos\Hallow Clips
        [DataMember] public bool TrayOnClose = true;
        [DataMember] public string FfmpegPath = "";
        [OnDeserializing] void D(StreamingContext c) { On = true; OnlyInGame = true; Seconds = 30; Fps = 60; Quality = "high"; Mic = true; Screen = 0; Folder = ""; TrayOnClose = true; FfmpegPath = ""; }
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HallowClips"); } }
        static string FilePath { get { return Path.Combine(Dir, "clips.json"); } }
        public static bool Frozen;
        public static ClipSettings Load()
        {
            try { if (File.Exists(FilePath)) using (var f = File.OpenRead(FilePath)) { var s = (ClipSettings)new DataContractJsonSerializer(typeof(ClipSettings)).ReadObject(f); if (s != null) return s; } } catch { }
            return new ClipSettings();
        }
        public void Save()
        {
            if (Frozen) return;
            try { Directory.CreateDirectory(Dir); using (var f = File.Create(FilePath)) new DataContractJsonSerializer(typeof(ClipSettings)).WriteObject(f, this); } catch { }
        }
        public string ClipsFolder { get { return !string.IsNullOrEmpty(Folder) ? Folder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Hallow Clips"); } }
        public string Ffmpeg
        {
            get
            {
                if (!string.IsNullOrEmpty(FfmpegPath) && File.Exists(FfmpegPath)) return FfmpegPath;
                var own = Path.Combine(Dir, @"ffmpeg\ffmpeg.exe"); return File.Exists(own) ? own : null;
            }
        }
    }

    public class Recorder
    {
        readonly ClipSettings S;
        Process ff; AudioPipe sys, mic; public string Error; public DateTime Started;
        public string BufferDir { get { return Path.Combine(ClipSettings.Dir, "buffer"); } }
        public bool Running { get { try { return ff != null && !ff.HasExited; } catch { return false; } } }
        public Recorder(ClipSettings s) { S = s; }
        readonly StringBuilder log = new StringBuilder();
        public string LastLog { get { lock (log) return log.ToString(); } }

        static string Encoder(string ffmpeg)
        {
            // NVIDIA -> NVENC, AMD -> AMF, Intel -> Quick Sync, otherwise Windows' own Media Foundation encoder
            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"))) return "h264_nvenc";
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (var o in s.Get()) { var n = (o["Name"] as string ?? "").ToLowerInvariant(); if (n.Contains("radeon") || n.Contains("amd")) return "h264_amf"; if (n.Contains("intel")) return "h264_qsv"; }
            }
            catch { }
            return "h264_mf";
        }

        public bool Start()
        {
            Stop(); Error = null;
            var exe = S.Ffmpeg; if (exe == null) { Error = "FFmpeg isn't set up yet."; return false; }
            Directory.CreateDirectory(BufferDir);
            foreach (var f in Directory.GetFiles(BufferDir, "seg*.ts")) try { File.Delete(f); } catch { }
            sys = new AudioPipe(true); if (!sys.Open()) { Error = "Game sound: " + sys.Error; sys = null; }
            mic = null; if (S.Mic) { mic = new AudioPipe(false); if (!mic.Open()) mic = null; }
            int seg = 2, wrap = S.Seconds / seg + 6;
            var enc = Encoder(exe);
            var q = S.Quality == "max" ? 18 : S.Quality == "medium" ? 28 : 23;
            var v = enc == "h264_nvenc" ? "-c:v h264_nvenc -preset p4 -tune hq -rc vbr -cq " + q + " -b:v 0 -maxrate 60M -bufsize 60M"
                  : enc == "h264_amf" ? "-c:v h264_amf -quality balanced -rc cqp -qp_i " + q + " -qp_p " + (q + 2)
                  : enc == "h264_qsv" ? "-c:v h264_qsv -global_quality " + q
                  : "-vf hwdownload,format=bgra -c:v h264_mf -b:v 20M";
            var a = new StringBuilder();
            a.Append("-hide_banner -loglevel warning -y ");
            a.AppendFormat(CultureInfo.InvariantCulture, "-f lavfi -i ddagrab=output_idx={0}:framerate={1}:draw_mouse=1 ", S.Screen, S.Fps);
            int idx = 1; var maps = "-map 0:v ";
            foreach (var p in new[] { sys, mic })
            {
                if (p == null) continue;
                a.AppendFormat(CultureInfo.InvariantCulture, "-f f32le -ar {0} -ac {1} -i \\\\.\\pipe\\{2} ", p.Rate, p.Channels, p.PipeName);
                maps += "-map " + idx + ":a "; idx++;
            }
            a.Append(maps);
            a.Append(v + " ");
            a.AppendFormat(CultureInfo.InvariantCulture, "-g {0} -force_key_frames \"expr:gte(t,n_forced*{1})\" ", S.Fps * seg, seg);
            if (idx > 1) a.Append("-c:a aac -b:a 160k -ac 2 -ar 48000 ");
            if (sys != null && mic != null) a.Append("-metadata:s:a:0 title=\"Game and chat\" -metadata:s:a:1 title=\"Your mic\" ");
            a.AppendFormat(CultureInfo.InvariantCulture, "-f segment -segment_time {0} -segment_format mpegts -segment_wrap {1} -reset_timestamps 1 \"{2}\"", seg, wrap, Path.Combine(BufferDir, "seg%03d.ts"));
            try
            {
                ff = new Process { StartInfo = new ProcessStartInfo(exe, a.ToString()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = true } };
                ff.ErrorDataReceived += (o, e) => { if (e.Data != null) lock (log) { log.AppendLine(e.Data); if (log.Length > 8000) log.Remove(0, log.Length - 8000); } };
                ff.Start(); ff.BeginErrorReadLine();
                try { ff.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                if (sys != null) sys.Start(); if (mic != null) mic.Start();
                Started = DateTime.Now;
                return true;
            }
            catch (Exception e) { Error = "Couldn't start FFmpeg: " + e.Message; Stop(); return false; }
        }

        public void Stop()
        {
            if (ff != null)
            {
                try { if (!ff.HasExited) { ff.StandardInput.Write("q"); ff.StandardInput.Flush(); if (!ff.WaitForExit(3000)) ff.Kill(); } } catch { try { ff.Kill(); } catch { } }
                ff = null;
            }
            if (sys != null) { sys.Stop(); sys = null; } if (mic != null) { mic.Stop(); mic = null; }
        }

        // joins the pieces covering the last `seconds` into one MP4 (no re-encoding, so it takes a moment and no GPU)
        public string Save(int seconds, string name, out string error)
        {
            error = null;
            var since = DateTime.Now.AddSeconds(-seconds - 1);
            var segs = Directory.GetFiles(BufferDir, "seg*.ts").Select(f => new FileInfo(f)).Where(f => f.Length > 0 && f.LastWriteTime >= since).OrderBy(f => f.LastWriteTime).ToList();
            if (segs.Count == 0) { error = "Nothing recorded yet."; return null; }
            var dir = Path.Combine(S.ClipsFolder, Safe(name)); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Safe(name) + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".mp4");
            // copy the pieces first: FFmpeg keeps writing the newest one
            var tmp = Path.Combine(ClipSettings.Dir, "save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
            try
            {
                var list = new StringBuilder();
                for (int i = 0; i < segs.Count; i++) { var c = Path.Combine(tmp, i.ToString("000") + ".ts"); try { File.Copy(segs[i].FullName, c, true); list.AppendLine("file '" + c.Replace("\\", "/") + "'"); } catch { } }
                var lf = Path.Combine(tmp, "list.txt"); File.WriteAllText(lf, list.ToString());
                var p = Process.Start(new ProcessStartInfo(S.Ffmpeg, "-hide_banner -loglevel error -y -f concat -safe 0 -i \"" + lf + "\" -map 0 -c copy -movflags +faststart \"" + file + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
                var err = p.StandardError.ReadToEnd(); p.WaitForExit(30000);
                if (!File.Exists(file) || new FileInfo(file).Length == 0) { error = "Saving failed: " + err.Trim(); return null; }
                return file;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        static string Safe(string s) { var r = new string((s ?? "Desktop").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' '); return r.Length == 0 ? "Desktop" : r; }
    }
}
