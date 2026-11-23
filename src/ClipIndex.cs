// Hallow Clips - v1.12: the clip library behind the list - search, filter by game and date, storage used per game,
// and moving several clips to the Recycle Bin at once. No WPF here, so the unit tests can use it on its own.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HallowClips
{
    public static class ClipIndex
    {
        // every clip (MP4) in the clips folder, newest first
        public static List<FileInfo> All(string root)
        {
            try { if (Directory.Exists(root)) return new DirectoryInfo(root).GetFiles("*.mp4", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTime).ToList(); } catch { }
            return new List<FileInfo>();
        }

        // the game a file belongs to: the first folder under the clips folder ("Highlights" for reels); files right in it are "Other"
        public const string Other = "Other";
        public static string GameOf(string file, string root)
        {
            try
            {
                var r = Path.GetFullPath(root).TrimEnd('\\') + "\\"; var f = Path.GetFullPath(file);
                if (!f.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(Path.GetDirectoryName(f)) ?? Other;
                var rel = f.Substring(r.Length); var i = rel.IndexOf('\\');
                return i <= 0 ? Other : rel.Substring(0, i);
            }
            catch { return Other; }
        }

        // date filter: all, today, week (last 7 days), month (last 30 days)
        public static readonly string[] Ranges = { "all", "today", "week", "month" };
        public static bool InRange(DateTime t, string range, DateTime now)
        {
            switch (range ?? "all")
            {
                case "today": return t.Date == now.Date;
                case "week": return t >= now.Date.AddDays(-6);
                case "month": return t >= now.Date.AddDays(-29);
                default: return true;
            }
        }

        // search: every word has to be found in one of the fields (file name, title, game), ignoring case and accents
        public static bool Matches(string query, params string[] fields)
        {
            var words = (query ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return true;
            var hay = Fold(string.Join("\n", fields.Where(x => x != null)));
            return words.All(w => hay.Contains(Fold(w)));
        }
        static string Fold(string s)
        {
            var d = (s ?? "").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(d.Length);
            foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c == '_' ? ' ' : c);
            return sb.ToString();
        }

        public class Filter
        {
            public string Query = "", Game = "", Range = "all"; public bool FavoritesOnly;
            public bool IsEmpty { get { return string.IsNullOrWhiteSpace(Query) && string.IsNullOrEmpty(Game) && (Range ?? "all") == "all" && !FavoritesOnly; } }
        }
        // the clips that pass the filter (title: the clip's own title or null; favorite: starred)
        public static List<FileInfo> Apply(IEnumerable<FileInfo> clips, string root, Filter f, Func<FileInfo, string> title, Func<string, bool> favorite, DateTime now)
        {
            return clips.Where(c =>
            {
                var game = GameOf(c.FullName, root);
                if (!string.IsNullOrEmpty(f.Game) && !string.Equals(game, f.Game, StringComparison.OrdinalIgnoreCase)) return false;
                if (!InRange(c.LastWriteTime, f.Range, now)) return false;
                if (f.FavoritesOnly && (favorite == null || !favorite(c.FullName))) return false;
                if (!string.IsNullOrWhiteSpace(f.Query) && !Matches(f.Query, Path.GetFileNameWithoutExtension(c.Name), game, title == null ? null : title(c))) return false;
                return true;
            }).ToList();
        }

        // ---- storage used per game folder (clips, sessions, GIFs, screenshots - everything in it)
        public class Usage { public string Game; public long Bytes; public int Clips; }
        public static List<Usage> SizeByGame(string root)
        {
            var map = new Dictionary<string, Usage>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(root))
                    foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        var g = GameOf(f.FullName, root); Usage u;
                        if (!map.TryGetValue(g, out u)) map[g] = u = new Usage { Game = g };
                        try { u.Bytes += f.Length; } catch { }
                        if (f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)) u.Clips++;
                    }
            }
            catch { }
            return map.Values.OrderByDescending(u => u.Bytes).ThenBy(u => u.Game).ToList();
        }
        // free space on the clips folder's drive (-1 when unknown)
        public static long FreeBytes(string root)
        {
            try { var q = Path.GetPathRoot(Path.GetFullPath(root)); return new DriveInfo(q).AvailableFreeSpace; } catch { return -1; }
        }
        public static string Size(long b)
        {
            if (b < 0) return "?";
            if (b >= 1000L * 1000 * 1000) return (b / 1e9).ToString(b >= 100e9 ? "0" : "0.0", CultureInfo.InvariantCulture) + " GB";
            if (b >= 1000L * 1000) return (b / 1e6).ToString("0", CultureInfo.InvariantCulture) + " MB";
            return Math.Max(0, b / 1000).ToString(CultureInfo.InvariantCulture) + " KB";
        }

        // ---- several clips to the Recycle Bin. recycle does one file (throws when it can't); thumbnails and favourites follow.
        public class Removed { public int Count; public long Bytes; public List<string> Done = new List<string>(); public List<string> Failed = new List<string>(); public string Error; }
        public static Removed Recycle(IEnumerable<string> files, Action<string> recycle, Func<string, string> thumbOf)
        {
            var r = new Removed();
            foreach (var f in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                long len = 0; try { len = new FileInfo(f).Length; } catch { }
                try
                {
                    if (!File.Exists(f)) { r.Failed.Add(f); r.Error = "Not found: " + Path.GetFileName(f); continue; }
                    recycle(f); r.Count++; r.Bytes += len; r.Done.Add(f);
                    if (thumbOf != null) try { var t = thumbOf(f); if (t != null && File.Exists(t)) File.Delete(t); } catch { }   // our own small JPG, made again if needed
                }
                catch (Exception e) { r.Failed.Add(f); r.Error = e.Message; }
            }
            return r;
        }
        public static void ToRecycleBin(string file)
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }

        // ---- v1.12: Copy for Discord in three sizes (10 MB = free Discord; 25 / 50 MB for bigger upload limits)
        public static readonly int[] DiscordSizes = { 10, 25, 50 };
        public static int DiscordMB(int mb) { return DiscordSizes.Contains(mb) ? mb : 10; }

        // ---- v1.16: the clean-up - what goes to the Recycle Bin: clips older than keepDays (0 = any age), then the oldest while the
        // folder is over limitBytes (0 = no limit). Never a favourite or anything keep() says (the recording), or anything from the
        // last 2 minutes. files = (path, bytes, last written), oldest are taken first.
        public static readonly int[] KeepDayChoices = { 0, 30, 90, 365 };
        public static List<string> Cleanup(IEnumerable<Tuple<string, long, DateTime>> files, long limitBytes, int keepDays, Func<string, bool> keep, DateTime now)
        {
            var list = files.OrderBy(f => f.Item3).ToList(); long total = list.Sum(f => f.Item2); var r = new List<string>();
            foreach (var f in list)
            {
                bool old = keepDays > 0 && f.Item3 < now.AddDays(-keepDays), over = limitBytes > 0 && total > limitBytes;
                if (!old && !over) continue;
                if (keep(f.Item1) || f.Item3 > now.AddMinutes(-2)) continue;
                r.Add(f.Item1); total -= f.Item2;
            }
            return r;
        }
    }
}
