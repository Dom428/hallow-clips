// Hallow Clips 1.16 - teach the kill sound: the clip's game sound drawn as a strip, the part you pick (moved by a click or the
// slider), how often that sound is heard in the rest of the clip (the same detector that listens while you play), and Listen.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HallowClips
{
    public class KillSoundWindow : ClipDialog
    {
        public KillSound Result;
        readonly Highlights.Analysis a; readonly string sens; double start, len = 0.35;
        readonly Canvas wave = new Canvas { Width = W, Height = H, Background = Brushes.Transparent, ClipToBounds = true, Cursor = Cursors.Hand };
        readonly Border sel; readonly TextBlock when, found; readonly Slider slider; readonly List<UIElement> ticks = new List<UIElement>();
        const double W = 548, H = 96;
        public int Heard;   // tests / snaps: how often the picked sound is heard in the clip

        public KillSoundWindow(string clip, string ffmpeg, Highlights.Analysis a, string track, string sens) : base("Teach the kill sound", 600, clip, ffmpeg)
        {
            this.a = a; this.sens = sens ?? "normal";
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading("Teach the kill sound"));
            sp.Children.Add(new TextBlock { Text = "Pick the moment your game plays its kill sound (the hit marker or “eliminated” sound) - click it on the strip. While you play, Hallow Clips listens for it in the game's sound and adds a marker or saves a clip. It never looks inside the game.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 12) });
            // the sound, one bar per slice (its loudest moment)
            var dur = Math.Max(0.5, a.Seconds); int bars = 182; var barBrush = (Brush)FindResource("Muted");
            for (int i = 0; i < bars; i++)
            {
                int f0 = (int)(i * a.Db.Count / (double)bars), f1 = Math.Max(f0 + 1, (int)((i + 1) * a.Db.Count / (double)bars));
                double db = -120; for (int f = f0; f < Math.Min(f1, a.Db.Count); f++) db = Math.Max(db, a.Db[f]);
                var h = Math.Max(2, Math.Min(1, Math.Max(0, (db + 60) / 60)) * (H - 14));
                var r = new Rectangle { Width = 2, Height = h, Fill = barBrush, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(r, i * W / bars + 0.5); Canvas.SetTop(r, (H - h) / 2); wave.Children.Add(r);
            }
            var ac = Accent.Brush.Color;
            sel = new Border { Height = H, Background = new SolidColorBrush(Color.FromArgb(0x55, ac.R, ac.G, ac.B)), BorderBrush = Accent.Brush, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
            wave.Children.Add(sel);
            wave.MouseLeftButtonDown += (s, e) => Pick(e.GetPosition(wave).X / W * dur - len / 2);
            sp.Children.Add(new Border { Background = Pal.B("#2E2639"), CornerRadius = new CornerRadius(8), Padding = new Thickness(0), Child = wave, HorizontalAlignment = HorizontalAlignment.Left });
            slider = new Slider { Minimum = 0, Maximum = Math.Max(0.01, dur - len), Style = (Style)FindResource("HFader"), Margin = new Thickness(0, 10, 0, 0) };
            slider.ValueChanged += (s, e) => { if (Math.Abs(slider.Value - start) > 0.0005) Pick(slider.Value); };
            sp.Children.Add(slider);
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var listen = new Button { Content = "Listen", Style = (Style)FindResource("Btn"), Padding = new Thickness(12, 4, 12, 4), ToolTip = "Play the part you picked" };
            listen.Click += (s, e) => Listen(); DockPanel.SetDock(listen, Dock.Right); row.Children.Add(listen);
            when = new TextBlock { Foreground = Accent.Brush, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; row.Children.Add(when);
            sp.Children.Add(row);
            sp.Children.Add(new TextBlock { Text = "Listening to: " + track, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
            sp.Children.Add(Chips("Length", new[] { "0.2", "0.35", "0.5", "0.8" }.Select(x => Tuple.Create(x, x.TrimStart('0') + " s")), "0.35", v => { len = double.Parse(v, CultureInfo.InvariantCulture); slider.Maximum = Math.Max(0.01, dur - len); Pick(start); }));
            found = Note(""); sp.Children.Add(found);
            sp.Children.Add(Foot("Use this sound", () =>
            {
                string err; var k = Highlights.Teach(a, start, len, System.IO.Path.GetFileName(Clip), out err);
                if (k == null) { found.Text = err; found.Foreground = Pal.B("#FFB060"); return; }
                Result = k; Close();
            }));
            Content = sp;
            Pick(Highlights.Strongest(a, len));
        }

        void Pick(double t)
        {
            var dur = Math.Max(0.5, a.Seconds);
            start = Math.Max(0, Math.Min(t, dur - len));
            if (Math.Abs(slider.Value - start) > 0.0005) slider.Value = start;
            Canvas.SetLeft(sel, start / dur * W); sel.Width = Math.Max(3, len / dur * W);
            when.Text = TrimWindow.Clock(start) + " - " + TrimWindow.Clock(start + len);
            Rescan();
        }
        // the same detector as live: how often this sound is heard in the clip (a lot = not distinct enough)
        void Rescan()
        {
            foreach (var t in ticks) wave.Children.Remove(t); ticks.Clear();
            string err; var k = Highlights.Teach(a, start, len, "", out err);
            if (k == null) { Heard = 0; found.Text = err; found.Foreground = Pal.B("#FFB060"); return; }
            var hits = Highlights.Scan(a, k, false, sens); Heard = hits.Count; var dur = Math.Max(0.5, a.Seconds);
            foreach (var h in hits)
            {
                var r = new Rectangle { Width = 3, Height = 8, Fill = Accent.Brush, RadiusX = 1, RadiusY = 1, IsHitTestVisible = false };
                Canvas.SetLeft(r, h.At / dur * W); Canvas.SetTop(r, H - 9); wave.Children.Add(r); ticks.Add(r);
            }
            var times = string.Join(", ", hits.Take(8).Select(h => TrimWindow.Clock(h.At)));
            found.Foreground = (Brush)FindResource("Muted");
            if (hits.Count <= 1) found.Text = "Heard once in this clip - where you picked. A clip with two or three kills shows whether it's found again.";
            else if (hits.Count <= 8) found.Text = "Heard " + hits.Count + " times in this clip: " + times + ". Each of those would have become a highlight.";
            else { found.Text = "Heard " + hits.Count + " times - that's a lot. Pick a shorter, more distinct part, or set Sensitivity to Fewer hits."; found.Foreground = Pal.B("#FFB060"); }
        }
        void Listen()
        {
            if (Program.Quiet || Snap.On) return;   // tests: never a sound
            try { var p = new System.Media.SoundPlayer(new MemoryStream(Highlights.Wav(a.Mono, start, len))); p.Play(); } catch { }
        }
    }
}
