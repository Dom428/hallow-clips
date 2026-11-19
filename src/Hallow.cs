// Hallow Clips - family-wide look and safety (v1.6): the shared Hallow accent colour (theme.json from Hallow Toolkit) and
// crash reports that stay on this PC.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace HallowClips
{
    // ---- the accent colour: Clips' own orange, or the Hallow accent when "Use the Hallow accent" is on and theme.json is valid
    public static class Accent
    {
        public const string Own = "#F07820", OwnLight = "#F59A50";
        public static string Hex = Own;
        public static string FilePath
        {
            get
            {
                var t = Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_THEME");   // tests use their own file
                return !string.IsNullOrEmpty(t) ? t : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hallow", "theme.json");
            }
        }
        // the colour in theme.json, or null (missing, unreadable or not #RRGGBB)
        public static string Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                string text; using (var f = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var r = new StreamReader(f)) text = r.ReadToEnd();
                var m = Regex.Match(text, "\"accent\"\\s*:\\s*\"(#[0-9A-Fa-f]{6})\"");
                return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
            }
            catch { return null; }
        }
        public static string Pick(bool useHallow) { return useHallow ? (Read() ?? Own) : Own; }
        public static Color Color { get { return (Color)ColorConverter.ConvertFromString(Hex); } }
        // a lighter shade for borders (a third of the way to white)
        public static string LightHex
        {
            get
            {
                if (Hex == Own) return OwnLight;
                var c = Color; Func<byte, byte> up = v => (byte)(v + (255 - v) / 3);
                return "#" + up(c.R).ToString("X2") + up(c.G).ToString("X2") + up(c.B).ToString("X2");
            }
        }
        public static SolidColorBrush Brush { get { var b = new SolidColorBrush(Color); b.Freeze(); return b; } }
        // Theme.xaml with the accent swapped in
        public static string Themed(string xaml) { return xaml.Replace(Own, Hex).Replace(OwnLight, LightHex); }
    }

    // ---- crash reports: <data folder>\crashes\<time>.txt, newest 20 kept. Never settings, keys or file contents - only what broke.
    public static class Crash
    {
        public static string Dir { get { return Path.Combine(ClipSettings.Dir, "crashes"); } }
        static int written;
        public static void Write(Exception e, string where)
        {
            if (e == null || System.Threading.Interlocked.Exchange(ref written, 1) == 1) return;   // once per run
            try
            {
                Directory.CreateDirectory(Dir);
                var now = DateTime.Now; var sb = new StringBuilder();
                sb.AppendLine("Hallow Clips " + Program.Version + " closed unexpectedly");
                sb.AppendLine("Time:    " + now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Windows: " + WindowsVersion());
                sb.AppendLine("Where:   " + where);
                sb.AppendLine();
                for (var x = e; x != null; x = x.InnerException)
                {
                    if (x != e) sb.AppendLine().AppendLine("--- caused by ---");
                    sb.AppendLine(x.GetType().FullName + ": " + x.Message);
                    sb.AppendLine(x.StackTrace ?? "(no stack)");
                }
                sb.AppendLine().AppendLine("This file stays on your PC - Hallow Clips never sends it anywhere.");
                var file = Path.Combine(Dir, now.ToString("yyyy-MM-dd HH-mm-ss") + ".txt");
                File.WriteAllText(file, sb.ToString());
                foreach (var old in Files().Skip(20)) try { File.Delete(old.FullName); } catch { }
            }
            catch { }
        }
        static string WindowsVersion()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var build = k.GetValue("CurrentBuild") as string; int b; int.TryParse(build, out b);
                    return (b >= 22000 ? "Windows 11" : "Windows 10") + " " + (k.GetValue("DisplayVersion") as string ?? "") + " (build " + build + ")" + (Environment.Is64BitOperatingSystem ? " 64-bit" : "");
                }
            }
            catch { return Environment.OSVersion.VersionString; }
        }
        // newest first
        public static List<FileInfo> Files()
        {
            try { if (Directory.Exists(Dir)) return new DirectoryInfo(Dir).GetFiles("*.txt").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList(); } catch { }
            return new List<FileInfo>();
        }
        public static DateTime? Last()
        {
            var f = Files().FirstOrDefault(); if (f == null) return null;
            DateTime t; return DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f.Name), "yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t) ? t : f.LastWriteTime;
        }
    }

    // ---- the notice after a crash (shown once per report)
    public class CrashNotice : Dialog
    {
        public CrashNotice(string report) : base("Hallow Clips", 520)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading("Hallow Clips closed unexpectedly last time"));
            sp.Children.Add(new TextBlock { Text = "A crash report was saved on this PC (" + Path.GetFileName(report) + "). Nothing was sent anywhere. Your clips are fine; a session that was recording is finished into an MP4 by itself.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var details = new Button { Content = "Show details", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            details.Click += (s, e) => { try { Process.Start("notepad.exe", "\"" + report + "\""); } catch { } };
            var folder = new Button { Content = "Open folder", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            folder.Click += (s, e) => { try { Process.Start("explorer.exe", "/select,\"" + report + "\""); } catch { } };
            var ok = new Button { Content = "OK", Style = (Style)FindResource("Accent"), MinWidth = 80, IsDefault = true, IsCancel = true };
            ok.Click += (s, e) => Close();
            foot.Children.Add(details); foot.Children.Add(folder); foot.Children.Add(ok); sp.Children.Add(foot);
            Content = sp;
        }
    }
}
