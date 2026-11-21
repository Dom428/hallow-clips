// Hallow Clips - v1.7 windows: clip title, thumbnail picker, GIF, phone version (9:16) and the highlight reel.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HallowClips
{
    // shared bits: a picture of the clip at a moment (FFmpeg in the background), chips, labelled sliders
    public abstract class ClipDialog : Dialog
    {
        protected readonly string Clip, Ffmpeg;
        protected ClipDialog(string title, double width, string clip, string ffmpeg) : base(title, width) { Clip = clip; Ffmpeg = ffmpeg; }
        int gen;
        protected void Preview(double t, string vf, Border target)
        {
            if (Snap.On || Ffmpeg == null) return;
            int my = Interlocked.Increment(ref gen);
            var jpg = Path.Combine(Path.GetTempPath(), "hallowclips-preview-" + Guid.NewGuid().ToString("N") + ".jpg");
            new Thread(() =>
            {
                string err; int exit;
                ClipOps.Run(Ffmpeg, "-hide_banner -loglevel error -y -ss " + t.ToString("0.###", CultureInfo.InvariantCulture) + " -i \"" + Clip + "\" -frames:v 1 -vf \"" + vf + "\" \"" + jpg + "\"", 15000, out err, out exit);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (my == gen && File.Exists(jpg))
                        {
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache; bi.UriSource = new Uri(jpg); bi.EndInit(); bi.Freeze();
                            target.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                        }
                    }
                    catch { }
                    try { File.Delete(jpg); } catch { }
                }));
            }) { IsBackground = true }.Start();
        }
        protected Border Pic(double w, double h) { return new Border { Width = w, Height = h, CornerRadius = new CornerRadius(8), Background = Pal.B("#2E2639") }; }
        protected FrameworkElement Chips(string label, IEnumerable<Tuple<string, string>> opts, string selected, Action<string> pick)
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var l = new TextBlock { Text = label, Width = 70, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(l, Dock.Left); row.Children.Add(l);
            var wp = new WrapPanel();
            foreach (var o in opts)
            {
                var key = o.Item1; var c = new ToggleButton { Content = o.Item2, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = key == selected, Padding = new Thickness(12, 5, 12, 5) };
                c.Click += (s, e) => { foreach (ToggleButton x in wp.Children) x.IsChecked = x == c; pick(key); };
                wp.Children.Add(c);
            }
            row.Children.Add(wp); return row;
        }
        protected FrameworkElement Labelled(string label, FrameworkElement e)
        {
            var d = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var l = new TextBlock { Text = label, Width = 70, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(l, Dock.Left);
            d.Children.Add(l); d.Children.Add(e); return d;
        }
        protected TextBlock Note(string text) { return new TextBlock { Text = text, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }; }
        protected DispatcherTimer Soon(Action a) { var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; t.Tick += (s, e) => { t.Stop(); a(); }; return t; }
    }

    // ---- a one-line answer (clip title)
    public class PromptWindow : Dialog
    {
        public string Value; readonly TextBox box;
        public PromptWindow(string heading, string hint, string value, string okText) : base(heading, 480)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading(heading));
            sp.Children.Add(new TextBlock { Text = hint, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            box = new TextBox { Text = value ?? "", Style = (Style)FindResource("Field") }; sp.Children.Add(box);
            sp.Children.Add(Foot(okText, () => { Value = box.Text; Close(); }));
            Content = sp; Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }
    }

    // ---- the list picture: pick the moment
    public class ThumbWindow : ClipDialog
    {
        public double At = -1;
        public ThumbWindow(string clip, string ffmpeg, double duration) : base("Pick thumbnail", 560, clip, ffmpeg)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading("Pick thumbnail"));
            var pic = Pic(508, 286); sp.Children.Add(pic);
            var time = new TextBlock { Foreground = Accent.Brush, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Width = 62, TextAlignment = TextAlignment.Right };
            var s = new Slider { Minimum = 0, Maximum = Math.Max(0.1, duration - 0.1), Value = Math.Min(1, duration / 2), Style = (Style)FindResource("HFader"), VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(time, Dock.Right); row.Children.Add(time); row.Children.Add(s); sp.Children.Add(row);
            var soon = Soon(() => Preview(s.Value, "scale=512:-2", pic));
            Action upd = () => { time.Text = TrimWindow.Clock(s.Value); soon.Stop(); soon.Start(); };
            s.ValueChanged += (o, e) => upd(); upd();
            sp.Children.Add(Foot("Use this picture", () => { At = s.Value; Close(); }));
            Content = sp;
        }
    }

    // ---- GIF: start, length (capped), width
    public class GifWindow : ClipDialog
    {
        public bool Ok; public double Start, Length = 5; public int Width0 = 480;
        public GifWindow(string clip, string ffmpeg, double duration) : base("Export GIF", 560, clip, ffmpeg)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading("Export GIF"));
            var pic = Pic(508, 286); sp.Children.Add(pic);
            var time = new TextBlock { Foreground = Accent.Brush, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Width = 62, TextAlignment = TextAlignment.Right };
            var s = new Slider { Minimum = 0, Maximum = Math.Max(0, duration - 0.5), Value = 0, Style = (Style)FindResource("HFader"), VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel(); DockPanel.SetDock(time, Dock.Right); row.Children.Add(time); row.Children.Add(s); sp.Children.Add(Labelled("Starts at", row));
            var note = Note("");
            Action info = () => { var len = Math.Min(Length, duration - Start); note.Text = "A " + len.ToString("0.#", CultureInfo.InvariantCulture) + " s GIF, " + Width0 + " px wide at 15 fps, saved next to the clip. GIFs have no sound and get big quickly - " + Exports.GifMaxSeconds + " s at most."; };
            sp.Children.Add(Chips("Length", new[] { "3", "5", "10", "15" }.Select(x => Tuple.Create(x, x + " s")), "5", v => { Length = int.Parse(v); info(); }));
            sp.Children.Add(Chips("Width", new[] { "320", "480", "640" }.Select(x => Tuple.Create(x, x + " px")), "480", v => { Width0 = int.Parse(v); info(); }));
            sp.Children.Add(note);
            var soon = Soon(() => Preview(Start, "scale=512:-2", pic));
            s.ValueChanged += (o, e) => { Start = s.Value; time.Text = TrimWindow.Clock(Start); info(); soon.Stop(); soon.Start(); };
            time.Text = TrimWindow.Clock(0); info(); soon.Start();
            sp.Children.Add(Foot("Make GIF", () => { Ok = true; Close(); }));
            Content = sp;
        }
    }

    // ---- phone: which part of the picture the 9:16 frame keeps
    public class PhoneWindow : ClipDialog
    {
        public bool Ok; public double Pos = 0.5;
        public PhoneWindow(string clip, string ffmpeg, double duration) : base("Phone version", 560, clip, ffmpeg)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading("Phone version (9:16)"));
            var grid = new DockPanel();
            var pic = Pic(162, 288); pic.Margin = new Thickness(0, 0, 20, 0); DockPanel.SetDock(pic, Dock.Left); grid.Children.Add(pic);
            var right = new StackPanel();
            right.Children.Add(new TextBlock { Text = "A tall copy for TikTok, Shorts and Reels: the 9:16 part of the picture you pick, under 50 MB, game sound and mic in one track. Saved next to the clip.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted") });
            var s = new Slider { Minimum = 0, Maximum = 1, Value = 0.5, Style = (Style)FindResource("HFader"), Margin = new Thickness(0, 16, 0, 0) };
            WrapPanel chipsPanel = null;
            var chips = Chips("Keep", new[] { Tuple.Create("0", "Left"), Tuple.Create("0.5", "Centre"), Tuple.Create("1", "Right") }, "0.5", v => s.Value = double.Parse(v, CultureInfo.InvariantCulture));
            chipsPanel = (WrapPanel)((DockPanel)chips).Children[1];
            right.Children.Add(chips); right.Children.Add(s);
            grid.Children.Add(right); sp.Children.Add(grid);
            var mid = Math.Max(0, duration / 2);
            var soon = Soon(() => Preview(mid, Exports.PhoneFilter(Pos) + ",scale=324:-2", pic));
            s.ValueChanged += (o, e) =>
            {
                Pos = s.Value;
                foreach (ToggleButton x in chipsPanel.Children) { var k = x.Content as string; x.IsChecked = (k == "Left" && Pos < 0.01) || (k == "Centre" && Math.Abs(Pos - 0.5) < 0.01) || (k == "Right" && Pos > 0.99); }
                soon.Stop(); soon.Start();
            };
            soon.Start();
            sp.Children.Add(Foot("Make phone version", () => { Ok = true; Close(); }));
            Content = sp;
        }
    }

    // ---- highlight reel: tick clips, put them in order, optional crossfades and a title card
    public class ReelWindow : Dialog
    {
        public bool Ok; public List<string> Picked = new List<string>(); public bool Crossfade; public string TitleText;
        readonly List<Tuple<FileInfo, CheckBox>> rows = new List<Tuple<FileInfo, CheckBox>>();
        readonly StackPanel list = new StackPanel(); readonly TextBlock sum = new TextBlock();
        readonly Func<FileInfo, string> name; readonly Func<FileInfo, double> length;
        public ReelWindow(List<FileInfo> clips, Func<FileInfo, string> name, Func<FileInfo, double> length) : base("Highlight reel", 620)
        {
            this.name = name; this.length = length;
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading("Highlight reel"));
            sp.Children.Add(new TextBlock { Text = "Tick the clips to join and put them in order with the arrows. Saved in the Highlights folder.", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            foreach (var f in clips.OrderBy(f => f.LastWriteTime)) rows.Add(Tuple.Create(f, new CheckBox { IsChecked = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }));
            foreach (var r in rows) r.Item2.Click += (s, e) => Sum();
            Fill();
            sp.Children.Add(new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), Child = new ScrollViewer { Content = list, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            var fade = new CheckBox { Content = new TextBlock { Text = "Crossfade between clips (half a second - the reel is re-encoded)", TextWrapping = TextWrapping.Wrap }, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 12, 0, 0) };
            fade.Click += (s, e) => { Crossfade = fade.IsChecked == true; Sum(); };
            sp.Children.Add(fade);
            var tb = new TextBox { Style = (Style)FindResource("Field") };
            tb.TextChanged += (s, e) => { TitleText = tb.Text; Sum(); };
            var tr = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; var tl = new TextBlock { Text = "Title card", Width = 80, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Muted") }; DockPanel.SetDock(tl, Dock.Left);
            tr.Children.Add(tl); tr.Children.Add(tb); sp.Children.Add(tr);
            sum.Foreground = (Brush)FindResource("Muted"); sum.TextWrapping = TextWrapping.Wrap; sum.Margin = new Thickness(0, 10, 0, 0); sp.Children.Add(sum);
            sp.Children.Add(Foot("Make highlight reel", () =>
            {
                Picked = rows.Where(r => r.Item2.IsChecked == true).Select(r => r.Item1.FullName).ToList();
                if (Picked.Count < 2 && string.IsNullOrWhiteSpace(TitleText)) { sum.Text = "Tick at least two clips."; return; }
                if (Picked.Count == 0) { sum.Text = "Tick at least one clip."; return; }
                Ok = true; Close();
            }));
            Content = sp; Sum();
        }
        public void Check(int i) { rows[i].Item2.IsChecked = true; Sum(); }   // for snapshots
        void Fill()
        {
            list.Children.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                int idx = i; var r = rows[i];
                var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var up = new Button { Content = "Up", Tag = "icon", Width = 28, Height = 26, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Earlier", IsEnabled = i > 0 };
                var down = new Button { Content = "Down", Tag = "icon", Width = 28, Height = 26, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Later", IsEnabled = i < rows.Count - 1 };
                up.Click += (s, e) => Move(idx, -1); down.Click += (s, e) => Move(idx, 1);
                DockPanel.SetDock(down, Dock.Right); DockPanel.SetDock(up, Dock.Right); d.Children.Add(down); d.Children.Add(up);
                if (r.Item2.Parent != null) ((Panel)r.Item2.Parent).Children.Remove(r.Item2);
                DockPanel.SetDock(r.Item2, Dock.Left); d.Children.Add(r.Item2);
                var len = length(r.Item1);
                var lt = new TextBlock { Text = (len > 0 ? TrimWindow.Clock(len).Split('.')[0] + "   " : "") + r.Item1.LastWriteTime.ToString("d MMM HH:mm"), Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(lt, Dock.Right); d.Children.Add(lt);
                d.Children.Add(new TextBlock { Text = name(r.Item1), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
                list.Children.Add(d);
            }
        }
        void Move(int i, int by) { int j = i + by; if (j < 0 || j >= rows.Count) return; var t = rows[i]; rows[i] = rows[j]; rows[j] = t; Fill(); Sum(); }
        void Sum()
        {
            var p = rows.Where(r => r.Item2.IsChecked == true).ToList();
            double total = p.Sum(r => Math.Max(0, length(r.Item1))) + (string.IsNullOrWhiteSpace(TitleText) ? 0 : Exports.TitleLength) - (Crossfade ? Exports.Fade * Math.Max(0, p.Count - (string.IsNullOrWhiteSpace(TitleText) ? 1 : 0)) : 0);
            sum.Text = p.Count == 0 ? "No clips ticked yet." : p.Count + " clip" + (p.Count == 1 ? "" : "s") + ", about " + TrimWindow.Clock(total).Split('.')[0] + ". " +
                (Crossfade || !string.IsNullOrWhiteSpace(TitleText) ? "Re-encoded once on your graphics card." : "Joined without re-encoding when the clips share a format (otherwise re-encoded once).");
        }
    }
}
