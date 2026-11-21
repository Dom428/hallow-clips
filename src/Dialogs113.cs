// Hallow Clips - v1.13: the clip editor window (replaces the plain trim window). A picture strip along the timeline,
// start / end with frame steps, Fast (keyframes, lossless) or Exact (any frame), cut out a part, slow motion for a part,
// and a blur box drawn on the picture.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HallowClips
{
    public class EditWindow : ClipDialog
    {
        public bool Ok; public readonly EditPlan Plan = new EditPlan();
        readonly ClipOps.Info info; readonly double dur, frame;
        const double PW = 548, PH = 308, TW = 548, TH = 54;
        readonly Border pic; readonly Canvas over = new Canvas { Width = PW, Height = PH, Background = Brushes.Transparent, ClipToBounds = true };
        readonly Border strip = new Border { Width = TW, Height = TH, CornerRadius = new CornerRadius(6), Background = Pal.B("#2E2639") };
        readonly Canvas marks = new Canvas { Width = TW, Height = TH, IsHitTestVisible = false, ClipToBounds = true };
        readonly Rectangle blurBox = new Rectangle { Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 }, Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        readonly TextBlock startText = new TextBlock(), endText = new TextBlock(), sum = new TextBlock(), at = new TextBlock();
        readonly Slider startS, endS, cutA, cutB, slowA, slowB;
        readonly CheckBox cutOn, slowOn, blurOn; readonly FrameworkElement cutPanel, slowPanel, blurPanel;
        readonly System.Windows.Threading.DispatcherTimer soon; double previewAt; bool busy;

        public EditWindow(string clip, string ffmpeg, ClipOps.Info info) : base("Edit clip", 600, clip, ffmpeg)
        {
            this.info = info; dur = Math.Max(0.1, info.Duration); frame = info.Fps > 1 ? 1.0 / info.Fps : 1.0 / 60;
            Plan.Start = 0; Plan.End = dur; Plan.CutFrom = dur * 0.4; Plan.CutTo = dur * 0.6; Plan.SlowFrom = dur * 0.4; Plan.SlowTo = dur * 0.6; Plan.Speed = 0.5;
            var sp = new StackPanel { Margin = new Thickness(26, 18, 26, 18) };
            sp.Children.Add(Heading("Edit clip"));
            sp.Children.Add(new TextBlock { Text = System.IO.Path.GetFileNameWithoutExtension(clip) + "   \u00B7   the original stays, a copy is saved next to it", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis });
            // the picture (with the blur box drawn on it)
            pic = Pic(PW, PH);
            var pg = new Grid { Width = PW, Height = PH }; pg.Children.Add(pic); over.Children.Add(blurBox); pg.Children.Add(over);
            at.Foreground = Brushes.White; at.FontWeight = FontWeights.SemiBold; at.Margin = new Thickness(10, 6, 0, 0); at.HorizontalAlignment = HorizontalAlignment.Left; at.VerticalAlignment = VerticalAlignment.Top; at.IsHitTestVisible = false;
            at.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.9 };
            pg.Children.Add(at); sp.Children.Add(pg);
            DrawOnPicture();
            // the timeline: picture strip, the kept part, cut / slow parts, markers
            var tl = new Grid { Width = TW, Height = TH, Margin = new Thickness(0, 10, 0, 0) }; tl.Children.Add(strip); tl.Children.Add(marks); sp.Children.Add(tl);
            startS = MakeSlider(0); endS = MakeSlider(dur);
            sp.Children.Add(HandleRow("Start", startS, startText, -1)); sp.Children.Add(HandleRow("End", endS, endText, 1));
            if (info.Marks.Count > 0)   // v1.8 markers: start a few seconds before one
            {
                var mk = new WrapPanel { Margin = new Thickness(54, 6, 0, 0) };
                mk.Children.Add(new TextBlock { Text = "Markers", Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                foreach (var m in info.Marks)
                {
                    var t = m; var b = new Button { Content = TrimWindow.Clock(m).Split('.')[0], Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3), ToolTip = "Start 5 s before this marker" };
                    b.Click += (s, e) => startS.Value = Math.Max(0, t - 5); mk.Children.Add(b);
                }
                sp.Children.Add(mk);
            }
            sp.Children.Add(Chips("Cut", new[] { Tuple.Create("fast", "Fast - at keyframes, no re-encoding"), Tuple.Create("exact", "Exact - any frame (re-encoded)") }, "fast", v => { Plan.Exact = v == "exact"; startS.Value = Plan.Start; Update(); }));
            // cut out a part in the middle
            cutA = MakeSlider(Plan.CutFrom); cutB = MakeSlider(Plan.CutTo);
            cutPanel = Range(cutA, cutB, "Cut from", "to");
            cutOn = Section("Cut out a part in the middle", "Takes out the boring bit between two moments", cutPanel, sp);
            // slow motion
            slowA = MakeSlider(Plan.SlowFrom); slowB = MakeSlider(Plan.SlowTo);
            var slowStack = new StackPanel(); slowStack.Children.Add(Range(slowA, slowB, "From", "to"));
            slowStack.Children.Add(Chips("Speed", new[] { Tuple.Create("0.5", "Slow 0.5x"), Tuple.Create("0.25", "Slower 0.25x"), Tuple.Create("2", "Fast 2x") }, "0.5", v => { Plan.Speed = double.Parse(v, CultureInfo.InvariantCulture); Update(); }));
            slowPanel = slowStack;
            slowOn = Section("Slow motion for a part", "Plays that part slower (or faster) - the sound follows", slowPanel, sp);
            // blur a box
            var bp = new StackPanel();
            bp.Children.Add(Chips("Where", new[] { Tuple.Create("tl", "Top left"), Tuple.Create("tr", "Top right"), Tuple.Create("bl", "Bottom left"), Tuple.Create("br", "Bottom right"), Tuple.Create("bc", "Bottom middle") }, "",
                v => { Plan.Blur = Corner(v); blurOn.IsChecked = true; Update(); }));
            bp.Children.Add(Note("Or drag a box on the picture. Good for names, chat or the kill feed - the blur stays for the whole clip."));
            blurPanel = bp;
            blurOn = Section("Blur a box", "Hides names, chat or anything else in one place", blurPanel, sp);
            sum.Foreground = (Brush)FindResource("Muted"); sum.TextWrapping = TextWrapping.Wrap; sum.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(sum);
            sp.Children.Add(Foot("Save edited copy", () => { if (Plan.OutLength() >= 0.3) { Ok = true; Close(); } else sum.Text = "Keep at least a moment of the clip."; }));
            Content = sp;
            soon = Soon(() => Preview(previewAt, "scale=" + (int)(PW * 2) + ":-2", pic));
            startS.ValueChanged += (s, e) =>
            {
                var v = startS.Value;
                if (Snapping) v = info.Keys.Where(x => x <= v + 0.001).DefaultIfEmpty(info.Keys[0]).Max();   // fast: the keyframe at or before
                Plan.Start = Math.Max(0, v); if (Plan.End - Plan.Start < 0.3) { Plan.End = Math.Min(dur, Plan.Start + 0.3); endS.Value = Plan.End; }
                Show(Plan.Start);
            };
            endS.ValueChanged += (s, e) => { Plan.End = Math.Min(dur, Math.Max(endS.Value, Plan.Start + 0.3)); Show(Math.Max(Plan.Start, Plan.End - frame)); };
            cutA.ValueChanged += (s, e) => { Plan.CutFrom = cutA.Value; if (cutB.Value < cutA.Value) cutB.Value = cutA.Value; Show(Plan.CutFrom); };
            cutB.ValueChanged += (s, e) => { Plan.CutTo = cutB.Value; if (cutA.Value > cutB.Value) cutA.Value = cutB.Value; Show(Plan.CutTo); };
            slowA.ValueChanged += (s, e) => { Plan.SlowFrom = slowA.Value; if (slowB.Value < slowA.Value) slowB.Value = slowA.Value; Show(Plan.SlowFrom); };
            slowB.ValueChanged += (s, e) => { Plan.SlowTo = slowB.Value; if (slowA.Value > slowB.Value) slowA.Value = slowB.Value; Show(Plan.SlowTo); };
            cutOn.Click += (s, e) => Update(); slowOn.Click += (s, e) => Update();
            blurOn.Click += (s, e) => { if (blurOn.IsChecked == true && Plan.Blur == null) Plan.Blur = Corner("bl"); Update(); };
            Update(); previewAt = 0;
            if (!Snap.On) Loaded += (s, e) => { soon.Start(); LoadStrip(); };
        }
        // for snapshots: a look with every part switched on
        public void SnapLook(double[] blur)
        {
            cutOn.IsChecked = true; slowOn.IsChecked = true; blurOn.IsChecked = true; Plan.Blur = blur;
            startS.Value = dur * 0.1; endS.Value = dur * 0.95; cutA.Value = dur * 0.3; cutB.Value = dur * 0.45; slowA.Value = dur * 0.6; slowB.Value = dur * 0.8; Update();
        }
        public static double[] Corner(string c)
        {
            switch (c)
            {
                case "tl": return new[] { 0.02, 0.03, 0.30, 0.12 };
                case "tr": return new[] { 0.68, 0.03, 0.30, 0.12 };
                case "br": return new[] { 0.68, 0.85, 0.30, 0.12 };
                case "bc": return new[] { 0.30, 0.85, 0.40, 0.12 };
                default: return new[] { 0.02, 0.72, 0.30, 0.25 };   // bottom left: chat in most games
            }
        }
        Slider MakeSlider(double v) { return new Slider { Minimum = 0, Maximum = dur, Value = v, Style = (Style)FindResource("HFader"), VerticalAlignment = VerticalAlignment.Center, SmallChange = frame, LargeChange = 1 }; }
        FrameworkElement HandleRow(string label, Slider s, TextBlock t, int which)
        {
            var d = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var l = new TextBlock { Text = label, Width = 54, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(l, Dock.Left); d.Children.Add(l);
            t.Foreground = Accent.Brush; t.FontWeight = FontWeights.SemiBold; t.Width = 58; t.TextAlignment = TextAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 8, 0);
            Func<string, int, Button> step = (txt, dir) =>
            {
                var b = new Button { Content = txt, Style = (Style)FindResource("Btn"), Width = 30, Height = 26, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), ToolTip = dir < 0 ? "One frame back (a keyframe back in Fast)" : "One frame on (the next keyframe in Fast)" };
                b.Click += (o, e) => Step(s, which, dir); return b;
            };
            var r = step("\u25B6", 1); var lft = step("\u25C0", -1);
            DockPanel.SetDock(r, Dock.Right); DockPanel.SetDock(lft, Dock.Right); d.Children.Add(r); d.Children.Add(lft);
            DockPanel.SetDock(t, Dock.Right); d.Children.Add(t);
            d.Children.Add(s); return d;
        }
        void Step(Slider s, int which, int dir)
        {
            if (which < 0 && Snapping)
            {
                var k = dir > 0 ? info.Keys.Where(x => x > Plan.Start + 0.001).DefaultIfEmpty(Plan.Start).Min() : info.Keys.Where(x => x < Plan.Start - 0.001).DefaultIfEmpty(0).Max();
                s.Value = k; return;
            }
            s.Value = Math.Max(0, Math.Min(dur, s.Value + dir * frame));
        }
        FrameworkElement Range(Slider a, Slider b, string la, string lb)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            var t1 = new TextBlock { Text = la, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center };
            var t2 = new TextBlock { Text = lb, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(a, 1); Grid.SetColumn(t2, 2); Grid.SetColumn(b, 3);
            g.Children.Add(t1); g.Children.Add(a); g.Children.Add(t2); g.Children.Add(b); return g;
        }
        CheckBox Section(string title, string tip, FrameworkElement body, StackPanel sp)
        {
            var c = new CheckBox { Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 10, 0, 0), ToolTip = tip };
            body.Margin = new Thickness(46, 0, 0, 0); body.Visibility = Visibility.Collapsed;
            sp.Children.Add(c); sp.Children.Add(body); return c;
        }
        // drag on the picture = a new blur box
        Point? dragFrom;
        void DrawOnPicture()
        {
            over.Cursor = Cursors.Cross; over.ToolTip = "Drag a box to blur it";
            over.MouseLeftButtonDown += (s, e) => { dragFrom = e.GetPosition(over); over.CaptureMouse(); };
            over.MouseMove += (s, e) =>
            {
                if (dragFrom == null) return; var p = e.GetPosition(over); var a = dragFrom.Value;
                double x = Math.Max(0, Math.Min(a.X, p.X)), y = Math.Max(0, Math.Min(a.Y, p.Y)), w = Math.Min(PW, Math.Max(a.X, p.X)) - x, h = Math.Min(PH, Math.Max(a.Y, p.Y)) - y;
                if (w < 4 || h < 4) return;
                Plan.Blur = new[] { x / PW, y / PH, w / PW, h / PH }; blurOn.IsChecked = true; Update();
            };
            over.MouseLeftButtonUp += (s, e) => { dragFrom = null; over.ReleaseMouseCapture(); };
        }
        // a plain Fast trim starts at a keyframe; anything re-encoded can start at any frame
        bool Snapping { get { return !Plan.Exact && cutOn.IsChecked != true && slowOn.IsChecked != true && blurOn.IsChecked != true; } }
        void Show(double t) { previewAt = Math.Max(0, Math.Min(dur - 0.05, t)); Update(); if (soon != null) { soon.Stop(); soon.Start(); } }
        void Update()
        {
            if (busy) return; busy = true;
            try
            {
                Plan.Start = Snapping ? Math.Max(0, info.Keys.Where(x => x <= startS.Value + 0.001).DefaultIfEmpty(info.Keys[0]).Max()) : startS.Value;   // keyframes only matter for a lossless trim
                bool cut = cutOn.IsChecked == true, slow = slowOn.IsChecked == true, blur = blurOn.IsChecked == true;
                cutPanel.Visibility = cut ? Visibility.Visible : Visibility.Collapsed; slowPanel.Visibility = slow ? Visibility.Visible : Visibility.Collapsed; blurPanel.Visibility = blur ? Visibility.Visible : Visibility.Collapsed;
                Plan.CutFrom = cut ? cutA.Value : -1; Plan.CutTo = cut ? cutB.Value : -1;
                Plan.SlowFrom = slow ? slowA.Value : -1; Plan.SlowTo = slow ? slowB.Value : -1;
                var blurBoxPlan = blur ? Plan.Blur : null;
                startText.Text = TrimWindow.Clock(Plan.Start); endText.Text = TrimWindow.Clock(Plan.End);
                at.Text = TrimWindow.Clock(previewAt);
                if (blurBoxPlan != null)
                {
                    blurBox.Visibility = Visibility.Visible; blurBox.Width = blurBoxPlan[2] * PW; blurBox.Height = blurBoxPlan[3] * PH;
                    Canvas.SetLeft(blurBox, blurBoxPlan[0] * PW); Canvas.SetTop(blurBox, blurBoxPlan[1] * PH);
                }
                else blurBox.Visibility = Visibility.Collapsed;
                var saved = Plan.Blur; if (!blur) Plan.Blur = null;
                var len = Plan.OutLength(); var re = Plan.Reencode; var what = Plan.Describe();
                Plan.Blur = saved;
                sum.Text = "New length " + TrimWindow.Clock(len) + " of " + TrimWindow.Clock(info.Duration) + ".   " +
                    (re ? "Re-encoded once on your graphics card" + (what.Length > 0 ? " (" + what + ")" : "") + " - takes a little while." : "No re-encoding: the start moves in steps of the clip's keyframes, so the copy keeps full quality and is ready at once.");
                DrawTimeline(cut, slow);
            }
            finally { busy = false; }
        }
        public EditPlan Result() { var p = Plan; if (blurOn.IsChecked != true) p.Blur = null; return p; }
        void DrawTimeline(bool cut, bool slow)
        {
            marks.Children.Clear();
            Func<double, double> X = t => Math.Max(0, Math.Min(TW, t / dur * TW));
            Action<double, double, Brush, double, double> box = (a, b, br, top, h) => { if (b <= a) return; var r = new Rectangle { Width = Math.Max(1, X(b) - X(a)), Height = h, Fill = br }; Canvas.SetLeft(r, X(a)); Canvas.SetTop(r, top); marks.Children.Add(r); };
            var shade = Pal.Tint(215, "#16121C");
            box(0, Plan.Start, shade, 0, TH); box(Plan.End, dur, shade, 0, TH);
            if (cut && Plan.HasCut)   // taken out: darkened, with a red band
            {
                double a = Math.Max(Plan.CutFrom, Plan.Start), b = Math.Min(Plan.CutTo, Plan.End);
                box(a, b, shade, 0, TH); box(a, b, Pal.B("#E0424C"), TH / 2 - 3, 6);
            }
            if (slow && Plan.HasSpeed)   // slow motion: a blue band along the top
            {
                double a = Math.Max(Plan.SlowFrom, Plan.Start), b = Math.Min(Plan.SlowTo, Plan.End);
                box(a, b, Pal.Tint(70, "#4C9AFF"), 0, TH); box(a, b, Pal.B("#4C9AFF"), 0, 7);
            }
            foreach (var t in new[] { Plan.Start, Plan.End })
            {
                var l = new Rectangle { Width = 3, Height = TH, Fill = Accent.Brush }; Canvas.SetLeft(l, Math.Min(TW - 3, X(t) - (t == Plan.End ? 3 : 0))); marks.Children.Add(l);
            }
            foreach (var m in info.Marks) { var l = new Rectangle { Width = 2, Height = 10, Fill = Brushes.White }; Canvas.SetLeft(l, X(m)); Canvas.SetTop(l, TH - 10); marks.Children.Add(l); }
            var ph = new Rectangle { Width = 1, Height = TH, Fill = Brushes.White, Opacity = 0.8 }; Canvas.SetLeft(ph, X(previewAt)); marks.Children.Add(ph);
        }
        void LoadStrip()
        {
            if (Snap.On || Ffmpeg == null) return;
            var jpg = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hallowclips-strip-" + Guid.NewGuid().ToString("N") + ".jpg");
            var inf = info; var clip = Clip; var ff = Ffmpeg;
            new Thread(() =>
            {
                var f = Editor.Strip(ff, clip, inf, 12, jpg);
                Dispatcher.BeginInvoke(new Action(() => { SetStrip(f); try { File.Delete(jpg); } catch { } }));
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }
        public void SetStrip(string jpg)
        {
            try
            {
                if (jpg == null || !File.Exists(jpg)) return;
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(jpg); bi.EndInit(); bi.Freeze();
                strip.Background = new ImageBrush(bi) { Stretch = Stretch.Fill };
            }
            catch { }
        }
        public void SetPicture(string jpg)
        {
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(jpg); bi.EndInit(); bi.Freeze();
                pic.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
            }
            catch { }
        }
    }
}
