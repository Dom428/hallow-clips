// Hallow Clips - "What's new": shown once after an update, with the versions you skipped.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowClips
{
    public static class WhatsNew
    {
        // Newest first. One or two short lines per version.
        public static readonly Tuple<string, string[]>[] Notes = {
            Tuple.Create("1.11", new[] { "Steps aside for Discord Clips: while Discord's own Clips records the same game, Hallow Clips pauses (one recorder on the graphics card, not two) and starts again by itself when it stops", "Settings > When Discord Clips is also recording: pause Hallow Clips, or keep both; Record anyway this session is in the menu" }),
            Tuple.Create("1.10", new[] { "Webcam in a corner of your clips and sessions (Settings > Webcam, pick the corner and size)", "Keep the replay in memory instead of on disk to spare your SSD (Settings)", "Settings fold away behind the Settings button, so your clips get the room" }),
            Tuple.Create("1.9", new[] { "Own sound tracks: the game and Discord (or Spotify, your browser, TeamSpeak) each get their own track, next to everything + your mic", "Sound in exports: pick what goes into Discord copies, phone versions and highlight reels - e.g. game only, without Discord or your mic" }),
            Tuple.Create("1.8", new[] { "Updates itself: Check for updates in settings, or the Update button when a new version is out (asks first)", "Ctrl+Alt+P takes a screenshot; Ctrl+Alt+B marks a moment - markers become chapters and show up in Trim" }),
            Tuple.Create("1.7", new[] { "Highlight reel: tick clips, put them in order and join them into one video - with crossfades and a title card if you like", "Clip titles and your own thumbnail picture (the ... button)", "Export a GIF, or a 9:16 phone version for TikTok, Shorts and Reels" }),
            Tuple.Create("1.6", new[] { "Use the Hallow accent: the colour you pick in Hallow Toolkit replaces Clips' orange, right away", "If Clips ever crashes, a report is kept on this PC (never sent) and you're told next time; recording always stops with it" }),
            Tuple.Create("1.5", new[] { "One Hallow icon: when Hallow Hub runs, Clips lives in Hub's tray menu (Save clip, Record session, Replay buffer, Open clips folder) instead of its own icon", "Turn it off in settings: Use the Hallow icon" }),
            Tuple.Create("1.4", new[] { "Hallow family: save a clip from Hallow Remote on your phone, and Hallow Overlay shows Clip saved", "Your clips of a game on its Hallow Hub page, and an opt-in Discord post of new clips through Hallow Toolkit (the webhook stays on this PC)" }),
            Tuple.Create("1.3", new[] { "Record session (Ctrl+Alt+R): record everything until you stop, next to the replay", "Video format: HEVC or AV1 for smaller files, when your graphics card can", "Space limit for the clips folder (off unless you pick one): the oldest clips that aren't favourites go to the Recycle Bin" }),
            Tuple.Create("1.2", new[] { "Trim a clip (scissors) - no re-encoding, so it's instant and keeps full quality", "Copy for Discord: a copy under 10 MB, ready to paste with Ctrl+V", "Rename clips and star your favourites" }),
            Tuple.Create("1.1", new[] { "Other Hallow apps can save a clip and see your clips (Hallow Overlay shows Clip saved)", "Sound stays in step with the picture in long sessions, and recording stops cleanly if the app is closed hard" }),
        };

        static int Cmp(string a, string b) { Version x, y; return Version.TryParse(a, out x) && Version.TryParse(b, out y) ? x.CompareTo(y) : string.CompareOrdinal(a, b); }

        // The versions newer than `seen` (at most 6). Empty for a fresh install or no update.
        public static List<Tuple<string, string[]>> Since(string seen)
        {
            if (string.IsNullOrEmpty(seen)) return new List<Tuple<string, string[]>>();
            return Notes.Where(n => Cmp(n.Item1, seen) > 0 && Cmp(n.Item1, Program.Version) <= 0).Take(6).ToList();
        }
    }

    public class WhatsNewWindow : Window
    {
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        public WhatsNewWindow(List<Tuple<string, string[]>> items)
        {
            Title = "What's new in Hallow Clips"; Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text");
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(new TextBlock { Text = "What's new in Hallow Clips " + Program.Version, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Accent.Brush, Margin = new Thickness(0, 0, 0, 12) });
            foreach (var v in items)
            {
                sp.Children.Add(new TextBlock { Text = v.Item1, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), Margin = new Thickness(0, 6, 0, 3) });
                foreach (var line in v.Item2)
                {
                    var d = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var dot = new TextBlock { Text = "\u2022", Foreground = Accent.Brush, Width = 16, FontWeight = FontWeights.Bold }; DockPanel.SetDock(dot, Dock.Left);
                    d.Children.Add(dot); d.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
                    sp.Children.Add(d);
                }
            }
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var all = new Button { Content = "All release notes", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            all.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("https://github.com/Dom428/hallow-clips/releases") { UseShellExecute = true }); } catch { } };
            var ok = new Button { Content = "Got it", Style = (Style)FindResource("Accent"), MinWidth = 90 };
            ok.Click += (s, e) => Close();
            foot.Children.Add(all); foot.Children.Add(ok); sp.Children.Add(foot);
            Content = sp;
        }
    }
}
