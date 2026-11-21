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
        // v1.14: the accent for text and thin lines - darkened in light mode so it stays readable (4.5:1)
        public static string TextHex { get { return Look.IsLight ? Look.TextAccent(Hex) : Hex; } }
        public static SolidColorBrush Brush { get { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(TextHex)); b.Freeze(); return b; } }
        // the accent for filled areas (bars, buttons, chips): the real accent; v1.15: in light mode the family fill rule (darkened when
        // neither dark nor white text would reach 4.5:1 on it, e.g. violet), and the text on it is dark or white, whichever reads better
        public static string FillHex { get { return Look.IsLight ? Look.AccentFill(Hex) : Hex; } }
        public static string FillTextHex { get { return Look.IsLight ? Look.FillText(FillHex) : "#1A0E04"; } }
        public static SolidColorBrush FillBrush { get { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(FillHex)); b.Freeze(); return b; } }
        // Theme.xaml with the accent swapped in, and in light mode the light palette (v1.14). Tokens in Theme.xaml:
        // #F07821 = accent as a thin line / caret, #FFFFFE = the hover overlay (white on dark, black on light), #FFFFFD = slider knob hover,
        // v1.15: #ECE8F3 = slider knob, #16121D = slider knob ring, #1A0E04 = text on an accent fill, <!--light:fader--> = the slider's
        // filled part in light mode (the darkened accent; dark keeps WPF's own), <!--light:hover--> = hover overlay strength (fill x 0.9).
        public static string Themed(string xaml)
        {
            var light = Look.IsLight;
            if (light)   // v1.15: accent fills follow the family rule; knobs are white with an accent ring
                xaml = xaml.Replace(Own, FillHex).Replace(OwnLight, FillHex).Replace("#1A0E04", FillTextHex)
                    .Replace("#ECE8F3", Look.LPanel).Replace("#16121D", "#F07821").Replace("#FFFFFD", Look.LPanel2)
                    .Replace("<!--light:fader-->", "<Setter Property=\"Foreground\" Value=\"#F07821\"/>")
                    .Replace("Value=\"0.07\"/><!--light:hover-->", "Value=\"0.10\"/>");
            else
                xaml = xaml.Replace(Own, Hex).Replace(OwnLight, LightHex).Replace("#ECE8F3", "#ECE8F4").Replace("#16121D", "#16121C").Replace("#FFFFFD", "#FFFFFF");
            xaml = xaml.Replace("#F07821", TextHex).Replace("#FFFFFE", light ? "#000000" : "#FFFFFF");
            return light ? Look.Swap(xaml, Pal.Map) : xaml;
        }
    }

    // ---- v1.14: light mode - every dark colour Clips uses, and its light twin (spec palette). Theme.xaml and code both go through this.
    public static class Pal
    {
        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "#16121C", Look.LBg },      // Bg
            { "#1A1522", Look.LPanel },   // text field
            { "#1C1724", Look.LPanel },   // "clip saved" pop-up
            { "#211B2A", Look.LPanel },   // Panel
            { "#241E2E", Look.LPanel },   // menus, tooltips, drop-downs
            { "#2A2333", Look.LPanel2 },  // buttons
            { "#2E2639", Look.LPanel2 },  // PanelHi, picture placeholders
            { "#3A3248", Look.LBorder },  // Line, hover
            { "#55496A", "#B4A8CC" },     // button hover border
            { "#58566A", "#A39CB2" },     // switch off
            { "#5A5268", "#A39CB2" },     // idle dot
            { "#CDCAD6", "#FFFFFF" },     // switch knob
            { "#6ED28C", Look.LGreen },   // switch on
            { "#ECE8F4", Look.LText },    // Text
            { "#8F88A0", Look.LMuted },   // Muted
            { "#A98BE0", Look.LLilac },   // lilac headings
            { "#C8414B", "#F6D5D8" },     // pressed toggle (light red, dark text)
            { "#E05A64", Look.LRed },
            { "#E0424C", Look.LRed },     // recording dot, cut-out part
            { "#FF6B73", Look.LRed },     // stop session
            { "#FF8A90", Look.LRed },     // Recycle Bin
            { "#E0A030", Look.LAmber },   // paused dot
            { "#FFB060", Look.LAmberText },   // favourites warning (small text; v1.15: the text amber)
            { "#4C9AFF", "#2F74D8" },     // slow-motion part on the editor strip
        };
        public static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Look.Pick(hex, Map))); }
        public static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(Look.Pick(hex, Map)); }
        public static SolidColorBrush Tint(byte alpha, string hex) { var c = C(hex); return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)); }
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
