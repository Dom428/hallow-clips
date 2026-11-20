// Hallow Clips - v1.11: step aside for Discord Clips. Discord's own Clips (discord_clips.exe, started by Discord when it sees a
// game) records the same game with its own hardware encoder, so two encoders would run at once. This tells when it's recording:
// the process is there and, where Windows' GPU counters say so, its video encoder is actually busy.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HallowClips
{
    public class DiscordClips
    {
        public const string Name = "Discord Clips";
        // tests: only a stand-in discord_clips.exe in this folder counts (the real one may be running)
        static string TestDir { get { return Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_DISCORD"); } }

        public static List<int> Pids()
        {
            var r = new List<int>(); var dir = TestDir;
            foreach (var p in Process.GetProcessesByName("discord_clips"))
            {
                try
                {
                    if (!string.IsNullOrEmpty(dir)) { var path = Tracker.PathOf(p.Id); if (path == null || !path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue; }
                    r.Add(p.Id);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return r;
        }

        // the result of the last Check
        public bool Running;            // discord_clips.exe is there
        public bool Recording;          // and it's recording (encoder busy, or running when that can't be told)
        public string How = "";         // "encoder" / "process" - how Recording was decided
        public double EncodePercent;    // its video encoder use, summed over engines (when the counters are there)

        readonly Dictionary<string, PerformanceCounter> counters = new Dictionary<string, PerformanceCounter>();
        DateTime lastBusy = DateTime.MinValue; bool countersBroken;

        // Call every few seconds (off the UI thread). Cheap when discord_clips.exe isn't running: one process lookup.
        public void Check()
        {
            var pids = Pids();
            Running = pids.Count > 0;
            if (!Running) { Recording = false; How = ""; EncodePercent = 0; lastBusy = DateTime.MinValue; Drop(counters.Keys.ToList()); return; }
            bool known = false, fresh = false; double sum = 0;
            if (!countersBroken)
            {
                try
                {
                    // GPU Engine instances look like pid_1234_luid_0x..._phys_0_eng_6_engtype_VideoEncode
                    var names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames()
                        .Where(n => n.IndexOf("engtype_videoencode", StringComparison.OrdinalIgnoreCase) >= 0 && pids.Any(id => n.StartsWith("pid_" + id + "_", StringComparison.OrdinalIgnoreCase))).ToList();
                    Drop(counters.Keys.Where(k => !names.Contains(k)).ToList());
                    foreach (var n in names)
                    {
                        PerformanceCounter c;
                        if (!counters.TryGetValue(n, out c)) { c = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true); c.NextValue(); counters[n] = c; fresh = true; continue; }   // the first reading is always 0
                        try { sum += c.NextValue(); known = true; } catch { }
                    }
                }
                catch { countersBroken = true; Drop(counters.Keys.ToList()); }
            }
            EncodePercent = Math.Round(sum, 2);
            if (known)
            {
                // the encoder tells: busy now, or within the last 10 s (a short gap isn't a stop)
                if (sum > 0.05) lastBusy = DateTime.Now;
                Recording = (DateTime.Now - lastBusy).TotalSeconds < 10;
                How = "encoder";
            }
            else if (fresh) { if (How == "") How = "encoder"; }   // counters just made: the first real reading comes next time, keep what we had
            else { Recording = true; How = "process"; }   // no encoder counters for it (or none on this PC): its being there is the sign
        }
        void Drop(List<string> keys) { foreach (var k in keys) { try { counters[k].Dispose(); } catch { } counters.Remove(k); } }
    }
}
