// Hallow Clips - small windows for a clip (v1.2): trim (start and end, with a picture of each) and rename.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HallowClips
{
    public abstract class Dialog : Window
    {
        protected static SolidColorBrush B(string hex) { return Pal.B(hex); }   // v1.14: light mode swaps it
        protected Dialog(string title, double width)
        {
            Title = title; Width = width; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text");
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5; WindowStartupLocation = WindowStartupLocation.CenterOwner; Look.Track(this);
        }
        protected StackPanel Foot(string okText, Action ok)
        {
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (s, e) => Close();
            var b = new Button { Content = okText, Style = (Style)FindResource("Accent"), MinWidth = 110, IsDefault = true };
            b.Click += (s, e) => ok();
            foot.Children.Add(cancel); foot.Children.Add(b); return foot;
        }
        protected TextBlock Heading(string text) { return new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Accent.Brush, Margin = new Thickness(0, 0, 0, 6) }; }
    }

    // ---- Trim: pick where the clip starts (snaps to a keyframe, so nothing is re-encoded) and where it ends
    public class TrimWindow : Dialog
    {
        public double Start, End; public bool Ok;
        readonly string clip, ffmpeg; readonly ClipOps.Info info;
        readonly Border startPic = Pic(), endPic = Pic();
        readonly TextBlock startText = new TextBlock(), endText = new TextBlock(), lenText = new TextBlock();
        readonly Slider startS, endS;
        static Border Pic() { return new Border { Width = 248, Height = 140, CornerRadius = new CornerRadius(8), Background = Pal.B("#2E2639") }; }
        public static string Clock(double s) { if (s < 0) s = 0; return ((int)s / 60) + ":" + (s % 60).ToString("00.0", CultureInfo.InvariantCulture); }

        public TrimWindow(string clip, string ffmpeg, ClipOps.Info info) : base("Trim clip", 580)
        {
            this.clip = clip; this.ffmpeg = ffmpeg; this.info = info;
            Start = 0; End = info.Duration;
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading("Trim clip"));
            sp.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(clip), Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 14) });
            var pics = new Grid(); pics.ColumnDefinitions.Add(new ColumnDefinition()); pics.ColumnDefinitions.Add(new ColumnDefinition());
            Func<string, Border, TextBlock, FrameworkElement> col = (label, pic, t) =>
            {
                var c = new StackPanel();
                var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                h.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) }); t.Foreground = Accent.Brush; t.FontWeight = FontWeights.SemiBold; h.Children.Add(t);
                c.Children.Add(h); c.Children.Add(pic); return c;
            };
            var c0 = col("Starts at", startPic, startText); var c1 = col("Ends at", endPic, endText); c1.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(c1, 1); pics.Children.Add(c0); pics.Children.Add(c1); sp.Children.Add(pics);
            startS = new Slider { Minimum = 0, Maximum = Math.Max(0.1, info.Duration), Value = 0, Style = (Style)FindResource("HFader"), Margin = new Thickness(0, 16, 0, 0), ToolTip = "Start - moves in steps of the clip's keyframes, so nothing is re-encoded" };
            endS = new Slider { Minimum = 0, Maximum = Math.Max(0.1, info.Duration), Value = info.Duration, Style = (Style)FindResource("HFader"), Margin = new Thickness(0, 10, 0, 0), ToolTip = "End" };
            Func<string, Slider, FrameworkElement> row = (label, s) =>
            {
                var d = new DockPanel { Margin = s.Margin }; s.Margin = new Thickness(0);
                var l = new TextBlock { Text = label, Width = 44, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(l, Dock.Left);
                d.Children.Add(l); d.Children.Add(s); return d;
            };
            sp.Children.Add(row("Start", startS)); sp.Children.Add(row("End", endS));
            if (info.Marks.Count > 0)   // v1.8: your markers - click one to start a few seconds before it
            {
                var mk = new WrapPanel { Margin = new Thickness(44, 10, 0, 0) };
                mk.Children.Add(new TextBlock { Text = "Markers", Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                foreach (var m in info.Marks)
                {
                    var at = m; var b = new Button { Content = Clock(m).Split('.')[0], Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3), ToolTip = "Start 5 s before this marker" };
                    b.Click += (s, e) => startS.Value = Math.Max(0, at - 5); mk.Children.Add(b);
                }
                sp.Children.Add(mk);
            }
            lenText.Foreground = (Brush)FindResource("Muted"); lenText.Margin = new Thickness(0, 10, 0, 0); lenText.TextWrapping = TextWrapping.Wrap; sp.Children.Add(lenText);
            sp.Children.Add(Foot("Save trimmed copy", () => { Ok = End - Start >= 0.5; if (Ok) Close(); }));
            Content = sp;
            var soon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            soon.Tick += (s, e) => { soon.Stop(); Frame(Start, startPic); Frame(Math.Max(Start, End - 0.1), endPic); };
            startS.ValueChanged += (s, e) =>
            {
                var k = info.Keys.Where(x => x <= startS.Value + 0.001).DefaultIfEmpty(info.Keys[0]).Max();   // the keyframe at or before
                Start = Math.Max(0, k); if (End - Start < 0.5) { End = Math.Min(info.Duration, Start + 0.5); endS.Value = End; }
                Update(); soon.Stop(); soon.Start();
            };
            endS.ValueChanged += (s, e) => { End = Math.Max(endS.Value, Start + 0.5); End = Math.Min(End, info.Duration); Update(); soon.Stop(); soon.Start(); };
            Update();
            if (!Snap.On) Loaded += (s, e) => { Frame(0, startPic); Frame(Math.Max(0, info.Duration - 0.1), endPic); };
        }
        void Update()
        {
            startText.Text = Clock(Start); endText.Text = Clock(End);
            lenText.Text = "New length " + Clock(End - Start) + " of " + Clock(info.Duration) + ". The start moves in steps of the clip's keyframes, so nothing is re-encoded and the copy keeps full quality. The original clip stays.";
        }
        // a picture of the clip at `t` (FFmpeg, in the background)
        int gen;
        void Frame(double t, Border target)
        {
            if (Snap.On || ffmpeg == null) return;
            int my = Interlocked.Increment(ref gen);
            var jpg = Path.Combine(Path.GetTempPath(), "hallowclips-trim-" + Guid.NewGuid().ToString("N") + ".jpg");
            new Thread(() =>
            {
                string err; int exit;
                ClipOps.Run(ffmpeg, "-hide_banner -loglevel error -y -ss " + t.ToString("0.###", CultureInfo.InvariantCulture) + " -i \"" + clip + "\" -frames:v 1 -vf scale=496:-2 \"" + jpg + "\"", 15000, out err, out exit);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (File.Exists(jpg))
                        {
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(jpg); bi.EndInit(); bi.Freeze();
                            target.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                        }
                    }
                    catch { }
                    try { File.Delete(jpg); } catch { }
                }));
            }) { IsBackground = true }.Start();
        }
    }

    // ---- Rename
    public class RenameWindow : Dialog
    {
        public string NewName; readonly TextBox box;
        public RenameWindow(string clip) : base("Rename clip", 480)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading("Rename clip"));
            sp.Children.Add(new TextBlock { Text = "It stays in the same folder.", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 12) });
            box = new TextBox { Text = Path.GetFileNameWithoutExtension(clip), Style = (Style)FindResource("Field") };
            sp.Children.Add(box);
            sp.Children.Add(Foot("Rename", () => { NewName = box.Text; Close(); }));
            Content = sp;
            Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }
    }
}
