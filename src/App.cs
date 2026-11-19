// Hallow Clips - instant replay: keeps the last moments of your game in a rolling buffer, and Ctrl+Alt+S saves them as an MP4.
// Your game sound and your mic go on separate tracks. Uses FFmpeg (downloaded once, checked against GitHub's checksum).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("Hallow Clips")]
[assembly: AssemblyProduct("Hallow Clips")]
[assembly: AssemblyDescription("Instant replay: save the last moments of your game")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace HallowClips
{
    public static class Program
    {
        public const string Version = "1.2";
        [STAThread]
        public static void Main(string[] args)
        {
            int ci = Array.IndexOf(args, "--cmd");   // Hallow family: send a command to the running copy, print the reply
            if (ci >= 0) { Environment.Exit(Family.RunCmd(ci + 1 < args.Length ? string.Join(" ", args.Skip(ci + 1)) : "")); return; }
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length)
            {
                Snap.On = true; ClipSettings.Frozen = true;
                var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa);
                new MainWindow(true).SnapAll(args[si + 1]); return;
            }
            bool created;
            var mutex = new Mutex(true, @"Local\HallowClips", out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HallowClipsShow");
            if (!created) { showEvent.Set(); return; }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(app);
            var w = new MainWindow(args.Contains("--tray"));
            new Thread(() => { while (showEvent.WaitOne()) w.Dispatcher.BeginInvoke(new Action(w.ShowFromTray)); }) { IsBackground = true }.Start();
            app.Run();
            GC.KeepAlive(mutex);
        }
        static void LoadTheme(Application app)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowClips.Theme.xaml"))
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(s));
        }
    }

    // "Clip saved" pop-up: click-through, never takes focus from the game
    public class Toast : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        readonly TextBlock text = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        readonly DispatcherTimer hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        public Toast()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false; SizeToContent = SizeToContent.WidthAndHeight; FontFamily = new FontFamily("Segoe UI");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icons.Make("circle-play", 16, new SolidColorBrush(Color.FromRgb(0xF0, 0x78, 0x20))); ic.Margin = new Thickness(0, 0, 10, 0); sp.Children.Add(ic); sp.Children.Add(text);
            Content = new Border { Background = new SolidColorBrush(Color.FromArgb(235, 0x1C, 0x17, 0x24)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x78, 0x20)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 10, 18, 10), Child = sp };
            hide.Tick += (s, e) => { hide.Stop(); Hide(); };
            SourceInitialized += (s, e) => { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000); };
        }
        public void Flash(string msg)
        {
            text.Text = msg; if (!IsVisible) Show(); UpdateLayout();
            Left = SystemParameters.PrimaryScreenWidth - ActualWidth - 20; Top = 20; hide.Stop(); hide.Start();
        }
    }

    public class MainWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        readonly ClipSettings S = ClipSettings.Load();
        readonly Recorder rec;
        readonly Tracker tracker = new Tracker();
        List<Game> games = new List<Game>();
        readonly Toast toast = new Toast();
        readonly bool startHidden; bool exiting, toldTray, saving;
        TextBlock state, ffState; Border dot; StackPanel clipsList; Button setupBtn; WF.NotifyIcon tray;

        public MainWindow(bool hidden)
        {
            startHidden = hidden; rec = new Recorder(S);
            Title = "Hallow Clips"; Width = 820; Height = 760; MinWidth = 700; MinHeight = 560;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = AppIcon(); } catch { }
            Content = Build();
            SourceInitialized += (s, e) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4); };
            Closing += (s, e) =>
            {
                if (!exiting && S.TrayOnClose && !Snap.On)
                {
                    e.Cancel = true; Hide();
                    if (!toldTray && tray != null) { toldTray = true; tray.ShowBalloonTip(4000, "Hallow Clips", "Still running - Ctrl+Alt+S saves a clip while you play. Right-click the icon to quit.", WF.ToolTipIcon.None); }
                }
                else Quit();
            };
            if (Snap.On) { UpdateState(); FillClips(); return; }
            try { games = Library.Scan(new List<Game>()); } catch { }
            SetupTray();
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            hotkeyOk = RegisterHotKey(hwnd, 1, 0x4003, 0x53);   // Ctrl+Alt+S (MOD_ALT | MOD_CONTROL | MOD_NOREPEAT)
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) => { if (msg == 0x0312 && wp.ToInt32() == 1) { SaveClip(null); handled = true; } return IntPtr.Zero; });
            var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            poll.Tick += (s, e) => { try { tracker.Poll(games.Concat(games.SelectMany(g => g.Alts)).ToList()); } catch { } Decide(); };
            poll.Start();
            var rescan = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) }; rescan.Tick += (s, e) => { try { games = Library.Scan(new List<Game>()); } catch { } }; rescan.Start();
            UpdateState(); FillClips();
            if (!hotkeyOk) Status("Ctrl+Alt+S is taken by another app - use the Save button or the tray menu, or close that app and restart Hallow Clips.");
            Family.Serve("Clips", Command);
            // What's new: an update from 1.0 (which didn't remember versions) counts as seen 1.0 when FFmpeg was already set up
            if (string.IsNullOrEmpty(S.LastSeenVersion)) { S.LastSeenVersion = S.Ffmpeg != null ? "1.0" : Program.Version; S.Save(); }
            IsVisibleChanged += (s, e) => { if (IsVisible) Dispatcher.BeginInvoke(new Action(ShowWhatsNew), DispatcherPriority.ApplicationIdle); };
            if (!startHidden) Show();
            Dispatcher.BeginInvoke(new Action(Decide), DispatcherPriority.ApplicationIdle);
        }
        bool hotkeyOk;
        void ShowWhatsNew()
        {
            if (S.LastSeenVersion == Program.Version || !IsVisible || Snap.On) return;
            var items = WhatsNew.Since(S.LastSeenVersion);
            S.LastSeenVersion = Program.Version; S.Save();
            if (items.Count > 0) new WhatsNewWindow(items) { Owner = this }.Show();
        }

        // ---- Hallow family: commands from other Hallow apps (Remote, Toolkit) and "HallowClips.exe --cmd"
        string Command(string line)
        {
            var sp = line.IndexOf(' '); var cmd = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
            if (cmd == "ping") return "ok " + Family.Json(new Dictionary<string, object> { { "app", "Clips" }, { "version", Program.Version } });
            if (cmd == "status")
            {
                string json = null; var op = Dispatcher.BeginInvoke(new Action(() => json = StatusJson()));
                return op.Wait(TimeSpan.FromSeconds(5)) == DispatcherOperationStatus.Completed && json != null ? "ok " + json : "error busy";
            }
            if (cmd == "save")
            {
                string file = null, err = null; var done = new ManualResetEvent(false);
                var op = Dispatcher.BeginInvoke(new Action(() => SaveClip((f, e) => { file = f; err = e; done.Set(); })));
                if (op.Wait(TimeSpan.FromSeconds(5)) != DispatcherOperationStatus.Completed) return "error busy";
                if (!done.WaitOne(35000)) return "error Saving is taking too long";
                return file != null ? "ok " + Family.Json(new Dictionary<string, object> { { "file", file } }) : "error " + Family.OneLine(err ?? "Couldn't save the clip");
            }
            return "error unknown command";
        }

        // ---- status.json: what other Hallow apps read (Hub's game page, Toolkit's Discord post)
        readonly Dictionary<string, Tuple<long, DateTime, double>> durations = new Dictionary<string, Tuple<long, DateTime, double>>(StringComparer.OrdinalIgnoreCase);
        List<FileInfo> RecentClips(int n)
        {
            try { if (Directory.Exists(S.ClipsFolder)) return new DirectoryInfo(S.ClipsFolder).GetFiles("*.mp4", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTime).Take(n).ToList(); } catch { }
            return new List<FileInfo>();
        }
        double DurationOf(FileInfo f)
        {
            Tuple<long, DateTime, double> c;
            if (durations.TryGetValue(f.FullName, out c) && c.Item1 == f.Length && c.Item2 == f.LastWriteTimeUtc) return c.Item3;
            var d = Math.Round(Mp4.Duration(f.FullName), 1); durations[f.FullName] = Tuple.Create(f.Length, f.LastWriteTimeUtc, d); return d;
        }
        string StatusJson()
        {
            var clips = new List<object>();
            foreach (var f in RecentClips(30))
            {
                var jpg = ThumbPath(f.FullName);
                clips.Add(new Dictionary<string, object> { { "file", f.FullName }, { "game", f.Directory.Name }, { "time", Family.Time(f.LastWriteTime) }, { "sizeBytes", f.Length },
                    { "durationSec", DurationOf(f) }, { "thumb", File.Exists(jpg) ? jpg : null }, { "favorite", S.IsFavorite(f.FullName) } });
            }
            return Family.Json(new Dictionary<string, object> { { "app", "Clips" }, { "version", Program.Version }, { "updated", Family.Time(DateTime.Now) },
                { "recording", rec.Running }, { "game", Current != null ? GameName : null }, { "seconds", S.Seconds }, { "folder", S.ClipsFolder }, { "clips", clips } });
        }
        string lastStatusKey;
        // writes status.json when something changed (force: the clip list changed)
        void WriteStatus(bool force)
        {
            if (Snap.On) return;
            var key = rec.Running + "|" + (Current != null ? GameName : "") + "|" + S.Seconds + "|" + S.ClipsFolder;
            if (!force && key == lastStatusKey) return;
            lastStatusKey = key;
            if (statusSoon == null) { statusSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; statusSoon.Tick += (s, e) => { statusSoon.Stop(); var json = StatusJson(); new Thread(() => Family.WriteStatus(json)) { IsBackground = true }.Start(); }; }
            statusSoon.Stop(); statusSoon.Start();   // several changes in a row are written once
        }
        DispatcherTimer statusSoon;

        static ImageSource AppIcon()
        {
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            return Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        Tracker.Running Current { get { return tracker.Now.Values.OrderBy(r => r.Since).FirstOrDefault(); } }
        string GameName { get { var c = Current; return c == null ? "Desktop" : (c.Game.Parent ?? c.Game).Name; } }

        // record when it should (on, FFmpeg ready, and a game running unless "always")
        void Decide()
        {
            bool want = S.On && S.Ffmpeg != null && (!S.OnlyInGame || Current != null);
            if (want && !rec.Running) { if (!rec.Start()) Status(rec.Error); }
            else if (!want && rec.Running) rec.Stop();
            UpdateState(); WriteStatus(false);
        }

        // done (optional) gets the saved file, or null and why not
        readonly List<Action<string, string>> waiting = new List<Action<string, string>>();
        void SaveClip(Action<string, string> done)
        {
            if (saving) { if (done != null) waiting.Add(done); return; }   // already saving: that clip is the answer
            if (!rec.Running) { toast.Flash(S.Ffmpeg == null ? "Hallow Clips isn't set up yet" : "Not recording right now"); if (done != null) done(null, "Nothing recorded yet"); return; }
            saving = true; if (done != null) waiting.Add(done); var name = GameName; var secs = S.Seconds;
            toast.Flash("Saving the last " + secs + " seconds...");
            new Thread(() =>
            {
                string err; var f = rec.Save(secs, name, out err);
                // Hallow Overlay shows "Clip saved" on its panel when it runs; otherwise our own pop-up does
                bool overlay = false;
                if (f != null) { var r = Family.Send("Overlay", "flash Clip saved", 3000); overlay = r != null && (r == "ok" || r.StartsWith("ok ")); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    saving = false;
                    if (f != null) { if (overlay) toast.Hide(); else toast.Flash("Clip saved - " + name + ", " + secs + " s"); Status("Saved " + Path.GetFileName(f)); FillClips(); }
                    else { toast.Flash("Couldn't save the clip"); Status(err); }
                    var w = waiting.ToList(); waiting.Clear(); foreach (var d in w) try { d(f, err); } catch { }
                }));
            }) { IsBackground = true }.Start();
        }

        // ================================================================ window
        TextBlock statusText;
        void Status(string m) { if (statusText != null) statusText.Text = m; }

        UIElement Build()
        {
            var root = new DockPanel { Margin = new Thickness(24, 14, 24, 14) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) }; DockPanel.SetDock(head, Dock.Top);
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            try { title.Children.Add(new Image { Source = AppIcon(), Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) }); } catch { }
            title.Children.Add(new TextBlock { Text = "HALLOW", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = B("#F07820"), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = " CLIPS", FontSize = 22, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(title, Dock.Left); head.Children.Add(title);
            var saveNow = new Button { Content = "Save a clip now", Style = (Style)FindResource("Accent"), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Same as Ctrl+Alt+S" };
            saveNow.Click += (s, e) => SaveClip(null); head.Children.Add(saveNow);
            root.Children.Add(head);
            // status card
            var card = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(card, Dock.Top);
            var cs = new StackPanel();
            var sr = new StackPanel { Orientation = Orientation.Horizontal };
            dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            sr.Children.Add(dot);
            state = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; sr.Children.Add(state);
            cs.Children.Add(sr);
            cs.Children.Add(new TextBlock { Text = "Press Ctrl+Alt+S in any game to save the last moments. Your game sound and your mic are on separate tracks.", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 4, 0, 0) });
            var fr = new DockPanel { Margin = new Thickness(22, 10, 0, 0) };
            setupBtn = new Button { Content = "Set up FFmpeg (171 MB)", Style = (Style)FindResource("Accent"), Margin = new Thickness(0, 0, 12, 0) };
            setupBtn.Click += (s, e) => SetupFfmpeg();
            DockPanel.SetDock(setupBtn, Dock.Left); fr.Children.Add(setupBtn);
            ffState = new TextBlock { Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; fr.Children.Add(ffState);
            cs.Children.Add(fr);
            card.Child = cs; root.Children.Add(card);
            // settings
            var set = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(set, Dock.Top);
            Func<string, IEnumerable<Tuple<string, string>>, Func<string>, Action<string>, FrameworkElement> chips = (label, opts, get, setv) =>
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var l = new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }; DockPanel.SetDock(l, Dock.Left); row.Children.Add(l);
                var wp = new WrapPanel();
                foreach (var o in opts)
                {
                    var key = o.Item1; var c = new ToggleButton { Content = o.Item2, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = get() == key, Padding = new Thickness(12, 5, 12, 5) };
                    c.Click += (s, e) => { setv(key); S.Save(); foreach (ToggleButton x in wp.Children) x.IsChecked = x == c; Restart(); };
                    wp.Children.Add(c);
                }
                row.Children.Add(wp); return row;
            };
            set.Children.Add(chips("Clip length", new[] { "15", "30", "60", "120" }.Select(x => Tuple.Create(x, x == "120" ? "2 min" : x + " s")), () => S.Seconds.ToString(), v => S.Seconds = int.Parse(v)));
            set.Children.Add(chips("Quality", new[] { Tuple.Create("medium", "Smaller files"), Tuple.Create("high", "High"), Tuple.Create("max", "Best") }, () => S.Quality, v => S.Quality = v));
            set.Children.Add(chips("Frame rate", new[] { Tuple.Create("30", "30 fps"), Tuple.Create("60", "60 fps") }, () => S.Fps.ToString(), v => S.Fps = int.Parse(v)));
            var screens = WF.Screen.AllScreens.Length;
            if (screens > 1) set.Children.Add(chips("Screen", Enumerable.Range(0, screens).Select(i => Tuple.Create(i.ToString(), "Screen " + (i + 1))), () => S.Screen.ToString(), v => S.Screen = int.Parse(v)));
            Action<string, bool, Action<bool>> sw = (text, on, setv) =>
            {
                var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, Style = (Style)FindResource("Switch") };
                c.Click += (s, e) => { setv(c.IsChecked == true); S.Save(); Restart(); }; set.Children.Add(c);
            };
            sw("Instant replay on", S.On, v => S.On = v);
            sw("Only while a game is running (otherwise it records your desktop too)", S.OnlyInGame, v => S.OnlyInGame = v);
            sw("Record my mic on its own track", S.Mic, v => S.Mic = v);
            sw("Keep running in the tray when I close this window", S.TrayOnClose, v => S.TrayOnClose = v);
            sw("Start with Windows (in the tray)", StartsWithWindows(), v => SetStartWithWindows(v));
            root.Children.Add(set);
            // clips
            var ch = new DockPanel { Margin = new Thickness(0, 6, 0, 8) }; DockPanel.SetDock(ch, Dock.Top);
            var openF = new Button { Content = "Open clips folder", Style = (Style)FindResource("Btn") };
            openF.Click += (s, e) => { Directory.CreateDirectory(S.ClipsFolder); Process.Start("explorer.exe", "\"" + S.ClipsFolder + "\""); };
            DockPanel.SetDock(openF, Dock.Right); ch.Children.Add(openF);
            ch.Children.Add(new TextBlock { Text = "YOUR CLIPS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), VerticalAlignment = VerticalAlignment.Center });
            root.Children.Add(ch);
            statusText = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(2, 8, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel.SetDock(statusText, Dock.Bottom); root.Children.Add(statusText);
            clipsList = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = clipsList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            return root;
        }

        void Restart() { if (rec.Running) rec.Stop(); Decide(); }

        void UpdateState()
        {
            var ok = S.Ffmpeg != null;
            setupBtn.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            ffState.Text = ok ? "Records with FFmpeg on your graphics card - " + S.Seconds + " s clips at " + S.Fps + " fps. Clips go to " + S.ClipsFolder + "." :
                "Hallow Clips records with FFmpeg (free). It downloads once from GitHub and is checked before use.";
            bool on = rec.Running || (Snap.On && ok);
            dot.Background = on ? B("#E0424C") : B("#5A5268");
            state.Text = !ok ? "Not set up yet" : !S.On ? "Instant replay is off" : on ? "Recording " + (Current != null ? GameName : "your screen") + " - Ctrl+Alt+S saves the last " + S.Seconds + " s"
                : S.OnlyInGame ? "Waiting for a game - it starts by itself" : "Starting...";
            if (tray != null) tray.Text = rec.Running ? "Hallow Clips - recording (Ctrl+Alt+S)" : "Hallow Clips";
        }

        // ---- FFmpeg: the LGPL build from BtbN/FFmpeg-Builds on GitHub, checked against GitHub's SHA-256 before it's used
        void SetupFfmpeg()
        {
            if (MessageBox.Show(this, "Download FFmpeg now?\n\nAbout 171 MB from github.com/BtbN/FFmpeg-Builds (the free LGPL build). It's checked against GitHub's checksum before use, and kept in Hallow Clips' own folder.",
                "Hallow Clips", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            setupBtn.IsEnabled = false; ffState.Text = "Downloading FFmpeg...";
            new Thread(() =>
            {
                string err = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var wc = new WebClient(); wc.Headers[HttpRequestHeader.UserAgent] = "HallowClips";
                    var rel = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(wc.DownloadString("https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest"));
                    var asset = ((System.Collections.ArrayList)rel["assets"]).Cast<Dictionary<string, object>>().First(a => (a["name"] as string) == "ffmpeg-master-latest-win64-lgpl.zip");
                    var digest = (asset.ContainsKey("digest") ? asset["digest"] as string : null) ?? "";
                    if (!digest.StartsWith("sha256:")) throw new Exception("GitHub didn't give a checksum, so it wasn't used.");
                    var dir = Path.Combine(ClipSettings.Dir, "ffmpeg"); var zip = Path.Combine(ClipSettings.Dir, "ffmpeg-download.zip");
                    Directory.CreateDirectory(ClipSettings.Dir);
                    wc.DownloadProgressChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => ffState.Text = "Downloading FFmpeg... " + e.ProgressPercentage + "%"));
                    var done = new ManualResetEvent(false); Exception dl = null;
                    wc.Headers.Remove(HttpRequestHeader.UserAgent); wc.Headers[HttpRequestHeader.UserAgent] = "HallowClips";
                    wc.DownloadFileCompleted += (s, e) => { dl = e.Error; done.Set(); };
                    wc.DownloadFileAsync(new Uri(asset["browser_download_url"] as string), zip); done.WaitOne();
                    if (dl != null) throw dl;
                    string hash; using (var f = File.OpenRead(zip)) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                    if (hash != digest.Substring(7).ToLowerInvariant()) { File.Delete(zip); throw new Exception("The download didn't match GitHub's checksum, so it was thrown away."); }
                    if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir);
                    using (var z = ZipFile.OpenRead(zip))
                    {
                        foreach (var e in z.Entries.Where(e => e.FullName.Contains("/bin/") && (e.Name == "ffmpeg.exe" || e.Name == "ffprobe.exe"))) e.ExtractToFile(Path.Combine(dir, e.Name), true);
                        var lic = z.Entries.FirstOrDefault(e => e.Name == "LICENSE.txt"); if (lic != null) lic.ExtractToFile(Path.Combine(dir, "FFmpeg-LICENSE.txt"), true);
                    }
                    File.Delete(zip);
                }
                catch (Exception e) { err = e.Message; }
                Dispatcher.BeginInvoke(new Action(() => { setupBtn.IsEnabled = true; if (err != null) ffState.Text = "Couldn't set up FFmpeg: " + err; UpdateState(); Decide(); }));
            }) { IsBackground = true }.Start();
        }

        // ---- the clip list: newest first, with a picture from each clip
        void FillClips()
        {
            clipsList.Children.Clear(); WriteStatus(true);
            var files = RecentClips(20);
            if (files.Count == 0) { clipsList.Children.Add(new TextBlock { Text = "No clips yet. Start a game and press Ctrl+Alt+S when something good happens.", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(2, 6, 0, 0) }); return; }
            foreach (var f in files)
            {
                var fi = f; var row = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8) };
                var dp = new DockPanel();
                var thumb = new Border { Width = 128, Height = 72, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 14, 0), Background = B("#2E2639") };
                DockPanel.SetDock(thumb, Dock.Left); dp.Children.Add(thumb); Thumb(fi.FullName, thumb);
                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Func<string, string, Button> ib = (label, tip) => new Button { Content = label, Tag = "icon", Width = 32, Height = 30, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(6, 0, 0, 0), ToolTip = tip };
                var play = new Button { Content = "Play", Style = (Style)FindResource("Btn"), Margin = new Thickness(6, 0, 0, 0) };
                play.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(fi.FullName) { UseShellExecute = true }); } catch { } };
                bool fav = S.IsFavorite(fi.FullName);
                var star = ib("Favourite", fav ? "Favourite - click to unmark" : "Mark as a favourite (never removed by the space limit)");
                star.Foreground = fav ? B("#F07820") : (Brush)FindResource("Muted");
                star.Click += (s, e) => { S.SetFavorite(fi.FullName, !S.IsFavorite(fi.FullName)); S.Save(); FillClips(); };
                var trim = ib("Trim", "Trim - keep only the best part (no re-encoding)"); trim.Click += (s, e) => TrimClip(fi.FullName);
                var discord = ib("Copy for Discord", "Copy for Discord - a smaller copy under 10 MB, ready to paste"); discord.Click += (s, e) => CopyForDiscord(fi.FullName);
                var ren = ib("Rename", "Rename"); ren.Click += (s, e) => RenameClip(fi.FullName);
                var show = ib("Show in folder", "Show in folder");
                show.Click += (s, e) => Process.Start("explorer.exe", "/select,\"" + fi.FullName + "\"");
                var del = ib("Delete", "Move to the Recycle Bin");
                del.Click += (s, e) =>
                {
                    try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(fi.FullName, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); S.SetFavorite(fi.FullName, false); S.Save(); Status(fi.Name + " is in the Recycle Bin."); FillClips(); }
                    catch (Exception ex) { Status("Couldn't delete it: " + ex.Message); }
                };
                foreach (var b in new[] { play, star, trim, discord, ren, show, del }) btns.Children.Add(b);
                if (busy.Contains(fi.FullName)) foreach (var b in new[] { trim, discord, ren, del }) b.IsEnabled = false;
                DockPanel.SetDock(btns, Dock.Right); dp.Children.Add(btns);
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var name = Path.GetFileNameWithoutExtension(fi.Name); var game = fi.Directory.Name;
                bool own = !System.Text.RegularExpressions.Regex.IsMatch(name, @"^" + System.Text.RegularExpressions.Regex.Escape(game) + @" \d{4}-\d\d-\d\d \d\d-\d\d-\d\d$");   // renamed by you
                t.Children.Add(new TextBlock { Text = own ? name : game, FontSize = 14, FontWeight = FontWeights.SemiBold, ToolTip = fi.Name });
                var len = DurationOf(fi);
                t.Children.Add(new TextBlock { Text = (own ? game + "   \u00B7   " : "") + fi.LastWriteTime.ToString("ddd d MMM, HH:mm") + "   \u00B7   " + (len > 0 ? TrimWindow.Clock(len).Split('.')[0] + "   \u00B7   " : "") + (fi.Length / 1048576.0).ToString("0.0") + " MB", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5 });
                dp.Children.Add(t); row.Child = dp; clipsList.Children.Add(row);
            }
        }

        // ---- v1.2: trim, Copy for Discord, rename (FFmpeg runs in the background; the list shows what's happening)
        readonly HashSet<string> busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Work(string clip, string doing, Func<string> job, Action<string> done)
        {
            if (busy.Contains(clip) || S.Ffmpeg == null) { if (S.Ffmpeg == null) Status("Set up FFmpeg first."); return; }
            busy.Add(clip); Status(doing); FillClips();
            new Thread(() =>
            {
                string r; try { r = job(); } catch (Exception ex) { r = "!" + ex.Message; }
                Dispatcher.BeginInvoke(new Action(() => { busy.Remove(clip); done(r); FillClips(); }));
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }
        void TrimClip(string clip)
        {
            var ff = S.Ffmpeg; if (ff == null) { Status("Set up FFmpeg first."); return; }
            if (busy.Contains(clip)) return;
            Status("Reading the clip...");
            new Thread(() =>
            {
                var info = ClipOps.Inspect(ff, clip);
                Dispatcher.BeginInvoke(new Action(() => TrimClip(clip, ff, info)));
            }) { IsBackground = true }.Start();
        }
        void TrimClip(string clip, string ff, ClipOps.Info info)
        {
            if (info.Duration <= 0) { Status("Couldn't read that clip."); return; }
            var w = new TrimWindow(clip, ff, info) { Owner = this }; w.ShowDialog();
            if (!w.Ok) { Status(""); return; }
            double a = w.Start, b = w.End;
            Work(clip, "Trimming " + Path.GetFileName(clip) + "...", () => { string err; var f = ClipOps.Trim(ff, clip, info.Codec, a, b, out err); return f ?? "!" + err; },
                r => { if (r.StartsWith("!")) Status(r.Substring(1)); else { if (S.IsFavorite(clip)) { S.SetFavorite(r, true); S.Save(); } Status("Saved the trimmed copy: " + Path.GetFileName(r) + " (the original is still there)."); } });
        }
        void CopyForDiscord(string clip)
        {
            var ff = S.Ffmpeg;
            Work(clip, "Making a Discord copy of " + Path.GetFileName(clip) + "...", () =>
            {
                var info = ClipOps.Inspect(ff, clip); string err;
                var f = ClipOps.ForDiscord(ff, clip, info, m => Dispatcher.BeginInvoke(new Action(() => Status(m))), out err);
                return f ?? "!" + err;
            }, r =>
            {
                if (r.StartsWith("!")) { Status(r.Substring(1)); return; }
                try
                {
                    Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { r });
                    var mb = (new FileInfo(r).Length / 1048576.0).ToString("0.0");
                    Status("Copied for Discord (" + mb + " MB) - paste it into a Discord message with Ctrl+V.");
                    toast.Flash("Copied for Discord - paste with Ctrl+V");
                }
                catch (Exception ex) { Status("Made " + r + " but couldn't copy it: " + ex.Message); }
            });
        }
        void RenameClip(string clip)
        {
            if (busy.Contains(clip)) return;
            var w = new RenameWindow(clip) { Owner = this }; w.ShowDialog();
            if (w.NewName == null) return;
            string err; var dest = ClipOps.Rename(clip, w.NewName, out err);
            if (dest == null) { Status(err); return; }
            if (S.IsFavorite(clip)) { S.SetFavorite(clip, false); S.SetFavorite(dest, true); S.Save(); }
            Status("Renamed to " + Path.GetFileName(dest) + "."); FillClips();
        }        public static string ThumbPath(string clip) { return Path.Combine(ClipSettings.Dir, "thumbs", Path.GetFileNameWithoutExtension(clip) + ".jpg"); }
        void Thumb(string clip, Border target)
        {
            var dir = Path.Combine(ClipSettings.Dir, "thumbs"); var jpg = ThumbPath(clip);
            Action show = () => { try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(jpg); bi.DecodePixelWidth = 256; bi.EndInit(); bi.Freeze(); target.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill }; } catch { } };
            if (File.Exists(jpg)) { show(); return; }
            var ff = S.Ffmpeg; if (ff == null || Snap.On) return;
            new Thread(() =>
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var p = Process.Start(new ProcessStartInfo(ff, "-hide_banner -loglevel error -y -ss 1 -i \"" + clip + "\" -frames:v 1 -vf scale=256:-1 \"" + jpg + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    p.WaitForExit(15000);
                    if (File.Exists(jpg)) Dispatcher.BeginInvoke(new Action(() => { show(); WriteStatus(true); }));
                }
                catch { }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static bool StartsWithWindows() { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("HallowClips") != null; }
        static void SetStartWithWindows(bool on)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) { if (on) k.SetValue("HallowClips", "\"" + Assembly.GetExecutingAssembly().Location + "\" --tray"); else k.DeleteValue("HallowClips", false); } } catch { }
        }
        void SetupTray()
        {
            tray = new WF.NotifyIcon { Text = "Hallow Clips", Visible = true };
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            var menu = new WF.ContextMenuStrip();
            var open = new WF.ToolStripMenuItem("Open Hallow Clips"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold); open.Click += (s, e) => ShowFromTray(); menu.Items.Add(open);
            var save = new WF.ToolStripMenuItem("Save a clip   (Ctrl+Alt+S)"); save.Click += (s, e) => Dispatcher.BeginInvoke(new Action(() => SaveClip(null))); menu.Items.Add(save);
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowFromTray(); };
        }
        public void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); FillClips(); UpdateState(); }
        void Quit() { exiting = true; rec.Stop(); if (tray != null) { tray.Visible = false; tray.Dispose(); } Application.Current.Shutdown(); }

        public void SnapAll(string dir)
        {
            Snap.Save(this, System.IO.Path.Combine(dir, "main.png"), 820, 760);
            Snap.Save(new WhatsNewWindow(WhatsNew.Notes.ToList()), System.IO.Path.Combine(dir, "whatsnew.png"), 560, 0);
            var clip = RecentClips(1).FirstOrDefault();
            if (clip != null)
            {
                Snap.Save(new RenameWindow(clip.FullName), System.IO.Path.Combine(dir, "rename.png"), 480, 0);
                if (S.Ffmpeg != null) Snap.Save(new TrimWindow(clip.FullName, S.Ffmpeg, ClipOps.Inspect(S.Ffmpeg, clip.FullName)), System.IO.Path.Combine(dir, "trim.png"), 580, 0);
            }
        }
    }
}
