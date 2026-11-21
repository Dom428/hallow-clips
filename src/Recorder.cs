// Hallow Clips - settings and the replay buffer: FFmpeg grabs the screen (Desktop Duplication, on the GPU) and encodes it on the
// graphics card into 2-second pieces that keep overwriting each other; saving a clip joins the newest pieces without re-encoding.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
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
        [DataMember] public string LastSeenVersion = "";   // v1.1: for What's new
        [DataMember] public List<string> Favorites = new List<string>();   // v1.2: full paths of clips you starred
        [DataMember] public string Codec = "h264";       // v1.3: h264, hevc, av1
        [DataMember] public int SpaceLimitGB = 0;        // v1.3: 0 = no limit
        [DataMember] public string LastUpdateCheck = "";  // v1.8
        [DataMember] public bool PerAppSound = true;      // v1.9: the game and AppTracks on their own sound tracks
        [DataMember] public bool GameTrack = true;        // v1.9: the running game on its own track
        [DataMember] public List<string> AppTracks = new List<string> { "Discord" };   // v1.9: exe names
        [DataMember] public string ExportSound = "all";   // v1.9: all (everything + mic), nomic, game, gamemic
        [DataMember] public string Webcam = "";           // v1.10: camera name, "" = no webcam
        [DataMember] public string WebcamCorner = "br";   // v1.10: tl, tr, bl, br
        [DataMember] public string WebcamSize = "medium"; // v1.10: small, medium, large
        [DataMember] public bool RamBuffer = false;       // v1.10: keep the replay buffer in memory
        [DataMember] public string DiscordClips = "pause"; // v1.11: when Discord Clips also records: pause (Clips steps aside) or both
        [DataMember] public int DiscordMB = 10;           // v1.12: Copy for Discord size: 10, 25 or 50 MB
        [DataMember] public bool UseHallowAccent = true; // v1.6: the Hallow accent colour from theme.json
        [DataMember] public string Look = "follow";     // v1.14: follow (Hallow Toolkit's light / dark), light or dark
        [DataMember] public string LastCrashShown = "";  // v1.6: the crash report already told about
        [DataMember] public bool UseHallowIcon = true;   // v1.5: hide our tray icon while Hallow Hub hosts the family icon
        [DataMember] public string CodecsFor = "";       // v1.3: which FFmpeg + graphics card the list below was found for
        [DataMember] public List<string> Codecs = new List<string>();   // v1.3: hevc / av1 when this PC can record them
        [OnDeserializing] void D(StreamingContext c) { On = true; OnlyInGame = true; Seconds = 30; Fps = 60; Quality = "high"; Mic = true; Screen = 0; Folder = ""; TrayOnClose = true; FfmpegPath = ""; LastSeenVersion = ""; Favorites = new List<string>(); Codec = "h264"; SpaceLimitGB = 0; UseHallowIcon = true; UseHallowAccent = true; Look = "follow"; LastCrashShown = ""; LastUpdateCheck = ""; PerAppSound = true; GameTrack = true; AppTracks = new List<string> { "Discord" }; ExportSound = "all"; Webcam = ""; WebcamCorner = "br"; WebcamSize = "medium"; RamBuffer = false; DiscordClips = "pause"; DiscordMB = 10; CodecsFor = ""; Codecs = new List<string>(); }
        public static string Dir { get { var t = Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_DATA"); return !string.IsNullOrEmpty(t) ? t : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HallowClips"); } }   // tests use their own folder
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
        public bool IsFavorite(string file) { return Favorites != null && Favorites.Any(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)); }
        public void SetFavorite(string file, bool on) { if (Favorites == null) Favorites = new List<string>(); Favorites.RemoveAll(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)); if (on) Favorites.Add(file); }
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
        Process ff; AudioPipe sys, mic; List<AudioPipe> apps = new List<AudioPipe>(); public string Error; public DateTime Started;
        // v1.9: the running game's process (its own sound track); set before Start
        public int GamePid;
        IEnumerable<AudioPipe> Pipes { get { return new[] { sys, mic }.Concat(apps).Where(p => p != null); } }
        public int AppTracks { get { return apps.Count; } }
        // v1.10: webcam + memory buffer. The app sets NoWebcam / NoRam after a start that failed with them.
        public bool NoWebcam, NoRam, WebcamUsed; RamBuffer ram, lastRam;
        public bool InMemory { get { return ram != null; } }
        public bool Crashed { get { try { return ff != null && ff.HasExited; } catch { return false; } } }   // FFmpeg ended by itself
        public long MemoryBytes { get { var r = ram; return r == null ? 0 : r.Bytes; } }
        public static bool TestScreen { get { return Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_SCREEN") == "1"; } }   // tests: never the real screen
        public static string TestCamera { get { return Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_CAMERA"); } }   // tests: a generated picture instead of a camera
        static string WebcamInput(string name)
        {
            if (!string.IsNullOrEmpty(TestCamera)) return "-f lavfi -i testsrc2=s=640x360:r=30";
            return "-f dshow -rtbufsize 64M -video_size 1280x720 -i video=\"" + name.Replace("\"", "") + "\"";
        }
        // the webcams DirectShow lists (names) - reading the list doesn't switch a camera on
        public static List<string> Webcams(string ffmpeg)
        {
            var r = new List<string>(); if (!string.IsNullOrEmpty(TestCamera)) { r.Add("Test camera"); return r; }
            string err; int exit; ClipOps.Run(ffmpeg, "-hide_banner -list_devices true -f dshow -i dummy", 15000, out err, out exit);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(err ?? "", "\"([^\"]+)\" \\(video\\)")) if (!r.Contains(m.Groups[1].Value)) r.Add(m.Groups[1].Value);
            return r;
        }
        List<string> trackTitles = new List<string>();
        static string TitleArgs(List<string> titles) { var sb = new StringBuilder(); for (int k = 0; k < titles.Count; k++) sb.Append("-metadata:s:a:" + k + " title=\"" + titles[k].Replace("\"", "") + "\" "); return sb.ToString(); }
        public string BufferDir { get { return Path.Combine(ClipSettings.Dir, "buffer"); } }
        public bool Running { get { try { return ff != null && !ff.HasExited; } catch { return false; } } }
        public Recorder(ClipSettings s) { S = s; }
        // v1.3: a whole-session recording goes straight to this file (Matroska while recording, so it survives a crash;
        // made into an MP4 when it stops). null = the replay buffer.
        public string SessionFile;
        public string Codec = "h264"; string segExt = "ts";   // what the current recording uses

        // v1.3: the video formats this PC can record: h264 always, hevc / av1 when FFmpeg and the graphics card can encode them
        // (found with a one-frame test encode with the same settings a recording uses)
        public static string EncoderFor(string family, string codec) { return codec + "_" + family; }
        public static string GpuFamily(string ffmpeg) { var e = Encoder(ffmpeg); return e.Substring(e.IndexOf('_') + 1); }
        public static string VideoArgs(string family, string codec, int q)
        {
            var e = EncoderFor(family, codec);
            if (codec == "av1") q += 6;   // AV1's quality scale is finer (0-63)
            return family == "nvenc" ? "-c:v " + e + " -preset p4 -tune hq -rc vbr -cq " + q + " -b:v 0 -maxrate 60M -bufsize 60M"
                 : family == "amf" ? "-c:v " + e + " -quality balanced -rc cqp -qp_i " + q + " -qp_p " + (q + 2)
                 : family == "qsv" ? "-c:v " + e + " -global_quality " + q
                 : "-c:v " + e + " -b:v " + (codec == "h264" ? "20M" : "14M");
        }
        public static bool CanEncode(string ffmpeg, string codec)
        {
            var fam = GpuFamily(ffmpeg);
            var args = "-hide_banner -loglevel error -f lavfi -i color=black:s=1280x720:r=30 -frames:v 1 " + (fam == "mf" ? "-pix_fmt nv12 " : "") + VideoArgs(fam, codec, 23) + " -f null -";
            string err; int exit; ClipOps.Run(ffmpeg, args, 20000, out err, out exit);
            return exit == 0;
        }
        readonly StringBuilder log = new StringBuilder();
        public string LastLog { get { lock (log) return log.ToString(); } }

        public static string Encoder(string ffmpeg)
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
            Stop(); Error = null; lastRam = null;
            var exe = S.Ffmpeg; if (exe == null) { Error = "FFmpeg isn't set up yet."; return false; }
            Codec = S.Codec == "hevc" || S.Codec == "av1" ? S.Codec : "h264";
            segExt = Codec == "av1" ? "mkv" : "ts";   // MPEG-TS can't carry AV1
            if (SessionFile == null)
            {
                Directory.CreateDirectory(BufferDir);
                KillLeftovers(exe, BufferDir);
                foreach (var f in Directory.GetFiles(BufferDir, "seg*.*")) try { File.Delete(f); } catch { }
            }
            else Directory.CreateDirectory(Path.GetDirectoryName(SessionFile));
            sys = new AudioPipe(true); if (!sys.Open()) { Error = "Game sound: " + sys.Error; sys = null; }
            mic = null; if (S.Mic) { mic = new AudioPipe(false); if (!mic.Open()) mic = null; }
            // v1.9: the game and the apps you picked on their own tracks (Windows 10 2004+; otherwise just everything + mic)
            apps = new List<AudioPipe>();
            if (S.PerAppSound && ProcessAudio.Supported)
            {
                var want = new List<Tuple<int, string>>();
                if (GamePid > 0 && S.GameTrack) want.Add(Tuple.Create(GamePid, "Game"));
                foreach (var app in (S.AppTracks ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var pid = RootPid(app); if (pid > 0 && pid != GamePid && !want.Any(w => w.Item1 == pid)) want.Add(Tuple.Create(pid, AppTitle(app)));
                }
                foreach (var w in want) { var p = new AudioPipe(w.Item1, w.Item2); if (p.Open()) apps.Add(p); }
            }
            int seg = 2, wrap = S.Seconds / seg + 6;
            var fam = GpuFamily(exe);
            var q = S.Quality == "max" ? 18 : S.Quality == "medium" ? 28 : 23;
            // v1.10: webcam in a corner (the screen comes off the graphics card for that); NoWebcam = it failed last time
            var cam = !NoWebcam && !string.IsNullOrEmpty(S.Webcam) ? S.Webcam : null; WebcamUsed = cam != null;
            var v = (fam == "mf" && cam == null ? "-vf hwdownload,format=bgra " : "") + VideoArgs(fam, Codec, q);
            if (TestScreen) { cam = null; WebcamUsed = false; v = "-c:v mpeg2video -q:v 8"; }   // tests: a small generated picture on the CPU, not the screen or the graphics card
            var a = new StringBuilder();
            a.Append("-hide_banner -loglevel info -nostats -y ");   // info: "Press [q]" tells us when recording really starts
            if (TestScreen) a.Append("-f lavfi -i testsrc2=s=320x180:r=10 ");
            else a.AppendFormat(CultureInfo.InvariantCulture, "-f lavfi -i ddagrab=output_idx={0}:framerate={1}:draw_mouse=1 ", S.Screen, S.Fps);
            int idx = 1; var maps = cam != null ? "-map \"[vout]\" " : "-map 0:v ";
            foreach (var p in Pipes)
            {
                a.AppendFormat(CultureInfo.InvariantCulture, "-f {3} -ar {0} -ac {1} -i \\\\.\\pipe\\{2} ", p.Rate, p.Channels, p.PipeName, p.Format);
                maps += "-map " + idx + ":a "; idx++;
            }
            if (cam != null)
            {
                a.Append(WebcamInput(cam) + " ");
                double size = S.WebcamSize == "large" ? 0.30 : S.WebcamSize == "small" ? 0.15 : 0.22;
                var corner = S.WebcamCorner ?? "br";
                var x = corner.EndsWith("l") ? "24" : "main_w-overlay_w-24"; var y = corner.StartsWith("t") ? "24" : "main_h-overlay_h-24";
                a.Append("-filter_complex \"[0:v]hwdownload,format=bgra,split[s0][sref];[" + idx + ":v]setpts=PTS-STARTPTS,format=bgra[c0];[c0][sref]scale=w=trunc(rw*" + size.ToString(CultureInfo.InvariantCulture) + "/2)*2:h=-2[c1];[s0][c1]overlay=x=" + x + ":y=" + y + ":eof_action=pass:format=auto[vout]\" ");   // camera width = a share of the screen width
            }
            a.Append(maps);
            a.Append(v + " ");
            a.AppendFormat(CultureInfo.InvariantCulture, "-g {0} -force_key_frames \"expr:gte(t,n_forced*{1})\" ", S.Fps * seg, seg);
            if (idx > 1) a.Append("-c:a aac -b:a 160k -ac 2 -ar 48000 ");
            trackTitles = Pipes.Select(p => p == sys ? "Game and chat" : p == mic ? "Your mic" : p.Title).ToList();
            a.Append(TitleArgs(trackTitles));   // exports pick tracks by these names
            if (SessionFile != null) a.Append("-f matroska \"" + Path.ChangeExtension(SessionFile, ".mkv") + "\"");
            else
            {
                // v1.10: in memory - FFmpeg writes each 2 s piece into a pipe that Clips holds in RAM (nothing on the SSD)
                ram = S.RamBuffer && !NoRam ? new RamBuffer(wrap, segExt) : null;
                a.AppendFormat(CultureInfo.InvariantCulture, "-f segment -segment_time {0} -segment_format {1} -segment_wrap {2} -reset_timestamps 1 \"{3}\"", seg, segExt == "ts" ? "mpegts" : "matroska", wrap, ram != null ? ram.Pattern : Path.Combine(BufferDir, "seg%03d." + segExt));
                if (ram != null) ram.Start();
            }
            try
            {
                ff = new Process { StartInfo = new ProcessStartInfo(exe, a.ToString()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = true } };
                var all = Pipes.ToList();
                ff.ErrorDataReceived += (o, e) =>
                {
                    if (e.Data == null) return;
                    if (e.Data.StartsWith("Press [q]")) foreach (var p in all) p.Live = true;
                    if (e.Data.Contains("Opening '") && e.Data.Contains("for writing")) return;   // a new 2 s piece - not worth keeping
                    lock (log) { log.AppendLine(e.Data); if (log.Length > 8000) log.Remove(0, log.Length - 8000); }
                };
                // an FFmpeg that never says it started (shouldn't happen): start the audio clock anyway
                new Timer(_ => { foreach (var p in all) p.Live = true; }, null, 5000, Timeout.Infinite);
                ff.Start(); Job.Add(ff); ff.BeginErrorReadLine();
                try { ff.PriorityClass = TestScreen ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal; } catch { }
                foreach (var p in all) p.Start();
                Started = DateTime.Now;
                logTimer = new Timer(_ => WriteLog(), null, 30000, 60000);   // also while recording, in case the app is closed hard
                return true;
            }
            catch (Exception e) { Error = "Couldn't start FFmpeg: " + e.Message; Stop(); return false; }
        }

        public void Stop()
        {
            if (ff != null)
            {
                try { if (!ff.HasExited) { ff.StandardInput.Write("q"); ff.StandardInput.Flush(); if (!ff.WaitForExit(SessionFile != null ? 15000 : 3000)) ff.Kill(); } } catch { try { ff.Kill(); } catch { } }
                ff = null;
            }
            if (logTimer != null) { logTimer.Dispose(); logTimer = null; }
            WriteLog();
            if (sys != null) { sys.Stop(); sys = null; } if (mic != null) { mic.Stop(); mic = null; }
            foreach (var p in apps) p.Stop(); apps = new List<AudioPipe>();
            if (ram != null) { lastRam = ram; ram.Stop(); ram = null; }   // its pieces stay readable until the next start
        }

        // v1.1: recorder.log next to the settings - how the last recording went (sound kept in step, FFmpeg's warnings)
        Timer logTimer;
        void WriteLog()
        {
            var sys = this.sys; var mic = this.mic; var apps = this.apps;
            if (Started == DateTime.MinValue || (sys == null && mic == null && ff == null)) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Recording " + Started.ToString("yyyy-MM-dd HH:mm:ss") + " - " + DateTime.Now.ToString("HH:mm:ss") + " (" + (int)(DateTime.Now - Started).TotalSeconds + " s)");
                foreach (var p in new[] { sys, mic }.Concat(apps))
                    if (p != null) sb.AppendLine((p.Pid > 0 ? p.Title + " (process " + p.Pid + ")" : p.Loopback ? "Game sound" : "Mic") + ": " + p.Rate + " Hz, " + p.Channels + " ch; from Windows " + p.FramesIn + " frames, silence added " + p.FramesPadded + ", trimmed " + p.FramesDropped + ", glitches " + p.Glitches + "; recording started " + p.StartupMs + " ms after the pipe opened");
                sb.AppendLine(); sb.Append(LastLog);
                File.WriteAllText(Path.Combine(ClipSettings.Dir, SessionFile != null ? "session.log" : "recorder.log"), sb.ToString());
            }
            catch { }
        }

        // joins the pieces covering the last `seconds` into one MP4 (no re-encoding, so it takes a moment and no GPU)
        public string Save(int seconds, string name, out string error) { return Save(seconds, name, null, out error); }
        // v1.8: marks = times you pressed the marker hotkey; those inside the clip become chapters
        public string Save(int seconds, string name, List<DateTime> marks, out string error)
        {
            error = null;
            var since = DateTime.Now.AddSeconds(-seconds - 1);
            // pieces: (file or bytes in memory, when it ended)
            var r = ram ?? lastRam; List<Tuple<string, byte[], DateTime>> segs;
            if (r != null) segs = r.Since(since).Select(x => Tuple.Create((string)null, x.Item1, x.Item2)).ToList();
            else segs = Directory.GetFiles(BufferDir, "seg*." + segExt).Select(f => new FileInfo(f)).Where(f => f.Length > 0 && f.LastWriteTime >= since).OrderBy(f => f.LastWriteTime).Select(f => Tuple.Create(f.FullName, (byte[])null, f.LastWriteTime)).ToList();
            if (segs.Count == 0) { error = "Nothing recorded yet."; return null; }
            var clipStart = segs[0].Item3.AddSeconds(-2);   // a finished piece is written up to its end; pieces are 2 s
            var dir = Path.Combine(S.ClipsFolder, Safe(name)); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Safe(name) + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".mp4");
            // copy the pieces first: FFmpeg keeps writing the newest one
            var tmp = Path.Combine(ClipSettings.Dir, "save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
            try
            {
                var list = new StringBuilder();
                for (int i = 0; i < segs.Count; i++) { var c = Path.Combine(tmp, i.ToString("000") + "." + segExt); try { if (segs[i].Item2 != null) File.WriteAllBytes(c, segs[i].Item2); else File.Copy(segs[i].Item1, c, true); list.AppendLine("file '" + c.Replace("\\", "/") + "'"); } catch { } }
                var lf = Path.Combine(tmp, "list.txt"); File.WriteAllText(lf, list.ToString());
                var meta = Chapters(tmp, marks, clipStart, (DateTime.Now - clipStart).TotalSeconds);
                var p = Process.Start(new ProcessStartInfo(S.Ffmpeg, "-hide_banner -loglevel error -y -f concat -safe 0 -i \"" + lf + "\" " + (meta != null ? "-f ffmetadata -i \"" + meta + "\" -map_chapters 1 " : "") + "-map 0 -c copy " + TitleArgs(trackTitles) + (Codec == "hevc" ? "-tag:v hvc1 " : "") + "-movflags +faststart \"" + file + "\"")   // MPEG-TS pieces can't carry track names: added here
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
                var err = p.StandardError.ReadToEnd(); p.WaitForExit(30000);
                if (!File.Exists(file) || new FileInfo(file).Length == 0) { error = "Saving failed: " + err.Trim(); return null; }
                return file;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        // v1.3: a finished session recording (.mkv) becomes an MP4 next to it (no re-encoding). Also used for one left over
        // from a crash or a hard close. Returns the MP4, or null (the .mkv then stays, it plays too).
        // v1.8: chapters for markers (an FFmpeg metadata file), or null when none fall inside
        public static string Chapters(string dir, List<DateTime> marks, DateTime start, double length)
        {
            if (marks == null) return null;
            var t = marks.Select(m => (m - start).TotalSeconds).Where(s => s > 0.5 && s < length).OrderBy(s => s).ToList();
            if (t.Count == 0) return null;
            var sb = new StringBuilder(";FFMETADATA1\n"); var at = new List<double> { 0 }; at.AddRange(t);
            for (int i = 0; i < at.Count; i++)
            {
                var end = i + 1 < at.Count ? at[i + 1] : length;
                sb.Append("[CHAPTER]\nTIMEBASE=1/1000\nSTART=" + (long)(at[i] * 1000) + "\nEND=" + (long)(end * 1000) + "\ntitle=" + (i == 0 ? "Start" : "Marker " + i) + "\n");
            }
            var f = Path.Combine(dir, "chapters-" + Guid.NewGuid().ToString("N") + ".txt"); File.WriteAllText(f, sb.ToString()); return f;
        }
        public static string FinishSession(string ffmpeg, string mkv, out string error) { return FinishSession(ffmpeg, mkv, null, DateTime.MinValue, out error); }
        public static string FinishSession(string ffmpeg, string mkv, List<DateTime> marks, DateTime started, out string error)
        {
            error = null; var mp4 = Path.ChangeExtension(mkv, ".mp4");
            if (!File.Exists(mkv) || new FileInfo(mkv).Length == 0) { try { File.Delete(mkv); } catch { } error = "Nothing was recorded."; return null; }
            var info = ClipOps.Inspect(ffmpeg, mkv);
            string err; int exit;
            var meta = marks != null && info.Duration > 0 ? Chapters(Path.GetTempPath(), marks, started, info.Duration) : null;
            ClipOps.Run(ffmpeg, "-hide_banner -loglevel error -y -i \"" + mkv + "\" " + (meta != null ? "-f ffmetadata -i \"" + meta + "\" -map_chapters 1 " : "") + "-map 0 -c copy " + (info.Codec == "hevc" ? "-tag:v hvc1 " : "") + "-movflags +faststart \"" + mp4 + "\"", 3600000, out err, out exit);
            if (meta != null) try { File.Delete(meta); } catch { }
            if (exit != 0 || !File.Exists(mp4) || new FileInfo(mp4).Length == 0) { try { File.Delete(mp4); } catch { } error = "Couldn't make the MP4 (" + Family.OneLine(err) + ") - the recording is kept as " + Path.GetFileName(mkv) + "."; return null; }
            try { File.Delete(mkv); } catch { }
            return mp4;
        }
        public string SessionName(string game) { var g = Safe(game); return Path.Combine(S.ClipsFolder, g, g + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " session.mp4"); }

        // v1.10: the replay buffer in memory. FFmpeg's segment muxer opens \\.\pipe\<id>_NNN.ts for each 2 s piece; the pipe for the
        // next piece is ready as soon as the current one connects, and a piece's bytes are kept until its slot comes round again.
        class RamBuffer
        {
            readonly string id = "hallowclips_seg_" + Process.GetCurrentProcess().Id + "_" + Environment.TickCount; readonly int wrap; readonly string ext;
            readonly object gate = new object(); volatile bool stop;
            readonly Dictionary<int, Tuple<byte[], DateTime>> done = new Dictionary<int, Tuple<byte[], DateTime>>();
            MemoryStream current; NamedPipeServerStream waiting;
            public RamBuffer(int wrap, string ext) { this.wrap = wrap; this.ext = ext; }
            public string Pattern { get { return "\\\\.\\pipe\\" + id + "_%03d." + ext; } }
            public void Start() { Serve(0); }
            void Serve(int i)
            {
                if (stop) return;
                NamedPipeServerStream p;
                try { p = new NamedPipeServerStream(id + "_" + (i % wrap).ToString("000") + "." + ext, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1 << 20, 0); }
                catch { return; }
                lock (gate) waiting = p;
                p.BeginWaitForConnection(ar =>
                {
                    try { p.EndWaitForConnection(ar); } catch { try { p.Dispose(); } catch { } return; }
                    Serve(i + 1);   // the next piece's pipe is ready long before FFmpeg needs it
                    new Thread(() => Read(p, i % wrap)) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "clips ram" }.Start();
                }, null);
            }
            void Read(NamedPipeServerStream p, int slot)
            {
                var ms = new MemoryStream(); var buf = new byte[1 << 16];
                lock (gate) current = ms;
                try { int n; while ((n = p.Read(buf, 0, buf.Length)) > 0) lock (gate) ms.Write(buf, 0, n); } catch { }
                finally { try { p.Dispose(); } catch { } }
                lock (gate) { if (ms.Length > 0) done[slot] = Tuple.Create(ms.ToArray(), DateTime.Now); if (current == ms) current = null; }
            }
            public void Stop() { stop = true; lock (gate) { if (waiting != null) try { waiting.Dispose(); } catch { } } }
            public long Bytes { get { lock (gate) return done.Values.Sum(d => (long)d.Item1.Length) + (current != null ? current.Length : 0); } }
            // the pieces that ended after `since`, oldest first, plus the one being written
            public List<Tuple<byte[], DateTime>> Since(DateTime since)
            {
                lock (gate)
                {
                    var r = done.Values.Where(d => d.Item2 >= since).OrderBy(d => d.Item2).ToList();
                    if (current != null && current.Length > 0) r.Add(Tuple.Create(current.ToArray(), DateTime.Now));
                    return r;
                }
            }
        }

        // v1.9: the oldest process of that name whose parent isn't the same app (Discord runs several; its sound comes from a child)
        public static int RootPid(string exe)
        {
            try
            {
                var name = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : exe + ".exe";
                var list = new List<Tuple<int, int, DateTime>>();
                using (var s = new System.Management.ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, CreationDate FROM Win32_Process WHERE Name = '" + name.Replace("'", "") + "'"))
                    foreach (var o in s.Get())
                    {
                        DateTime when = DateTime.MaxValue; try { when = System.Management.ManagementDateTimeConverter.ToDateTime(o["CreationDate"] as string); } catch { }
                        list.Add(Tuple.Create((int)(uint)o["ProcessId"], (int)(uint)o["ParentProcessId"], when));
                    }
                var roots = list.Where(p => !list.Any(q => q.Item1 == p.Item2)).OrderBy(p => p.Item3).ToList();
                return roots.Count > 0 ? roots[0].Item1 : list.Count > 0 ? list[0].Item1 : 0;
            }
            catch { return 0; }
        }
        public static string AppTitle(string exe)
        {
            var n = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
            return n == "discord" ? "Discord" : n == "spotify" ? "Spotify" : n == "chrome" || n == "msedge" || n == "firefox" ? "Browser" : n.StartsWith("ts3client") || n == "teamspeak" ? "TeamSpeak" : Path.GetFileNameWithoutExtension(exe);
        }

        // v1.1: an FFmpeg left over from a copy of Hallow Clips that was closed hard (older versions, an update) still writes to
        // our folder - stop it before starting our own. Only FFmpeg runs of Hallow Clips' own FFmpeg that mention that folder.
        public static void KillLeftovers(string exe, string folder)
        {
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'ffmpeg.exe'"))
                    foreach (var o in s.Get())
                    {
                        var path = o["ExecutablePath"] as string; var cmd = o["CommandLine"] as string;
                        if (path == null || cmd == null || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) continue;
                        if (cmd.IndexOf(folder, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try { using (var p = Process.GetProcessById((int)(uint)o["ProcessId"])) { p.Kill(); p.WaitForExit(3000); } } catch { }
                    }
            }
            catch { }
        }
        public static string Safe(string s) { var r = new string((s ?? "Desktop").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' '); return r.Length == 0 ? "Desktop" : r; }
    }

    // v1.1: FFmpeg runs in a job that Windows closes with Hallow Clips - if the app is ended hard, its recordings stop too
    public static class Job
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, ref ExtendedLimits info, int size);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong a, b, c, d, e, f; }
        [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
        static IntPtr job;
        public static void Add(Process p)
        {
            try
            {
                lock (typeof(Job))
                {
                    if (job == IntPtr.Zero)
                    {
                        var h = CreateJobObject(IntPtr.Zero, null); if (h == IntPtr.Zero) return;
                        var info = new ExtendedLimits(); info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        if (!SetInformationJobObject(h, 9, ref info, Marshal.SizeOf(typeof(ExtendedLimits)))) return;
                        job = h;   // kept open for the life of the app
                    }
                    AssignProcessToJobObject(job, p.Handle);
                }
            }
            catch { }
        }
    }
}