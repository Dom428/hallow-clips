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
using System.Text.RegularExpressions;
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
[assembly: AssemblyVersion("1.11.0.0")]
[assembly: AssemblyFileVersion("1.11.0.0")]

namespace HallowClips
{
    public static class Program
    {
        public const string Version = "1.11";
        // tests: no pop-ups, no tray icon, no balloons, no update check (they go to logs in the test data folder)
        public static bool Quiet { get { return Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_QUIET") == "1"; } }
        [STAThread]
        public static void Main(string[] args)
        {
            int ci = Array.IndexOf(args, "--cmd");   // Hallow family: send a command to the running copy, print the reply
            if (ci >= 0) { Environment.Exit(Family.RunCmd(ci + 1 < args.Length ? string.Join(" ", args.Skip(ci + 1)) : "")); return; }
            // v1.6: crash reports (kept on this PC); exiting right after means no Windows error box, and the job ends FFmpeg
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Crash.Write(e.ExceptionObject as Exception, "app"); Environment.Exit(1); };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Crash.Write(e.Exception, "background task"); e.SetObserved(); };
            Accent.Hex = Accent.Pick(ClipSettings.Load().UseHallowAccent);   // v1.6: the Hallow accent, before the theme loads
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length)
            {
                Snap.On = true; ClipSettings.Frozen = true;
                var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa);
                new MainWindow(true).SnapAll(args[si + 1]); return;
            }
            bool created;
            var one = string.IsNullOrEmpty(Family.TestPipe) ? "" : "." + Family.TestPipe;   // tests: a copy next to the real one
            var mutex = new Mutex(true, @"Local\HallowClips" + one, out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HallowClipsShow" + one);
            if (!created) { showEvent.Set(); return; }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(app);
            app.DispatcherUnhandledException += (s, e) => { Crash.Write(e.Exception, "window"); e.Handled = true; Environment.Exit(1); };
            var w = new MainWindow(args.Contains("--tray"));
            new Thread(() => { while (showEvent.WaitOne()) w.Dispatcher.BeginInvoke(new Action(w.ShowFromTray)); }) { IsBackground = true }.Start();
            app.Run();
            GC.KeepAlive(mutex);
        }
        public static void LoadTheme(Application app)
        {
            string xaml; using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowClips.Theme.xaml")) using (var r = new StreamReader(s)) xaml = r.ReadToEnd();
            using (var m = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Accent.Themed(xaml))))
            {
                var d = (ResourceDictionary)XamlReader.Load(m);
                app.Resources.MergedDictionaries.Clear(); app.Resources.MergedDictionaries.Add(d);
            }
        }
    }

    // "Clip saved" pop-up: click-through, never takes focus from the game
    public class Toast : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        readonly TextBlock text = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        readonly DispatcherTimer hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        readonly FrameworkElement ic; readonly Border frame;
        public Toast()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false; SizeToContent = SizeToContent.WidthAndHeight; FontFamily = new FontFamily("Segoe UI");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            ic = Icons.Make("circle-play", 16, Accent.Brush); ic.Margin = new Thickness(0, 0, 10, 0); sp.Children.Add(ic); sp.Children.Add(text);
            Content = frame = new Border { Background = new SolidColorBrush(Color.FromArgb(235, 0x1C, 0x17, 0x24)), BorderBrush = Accent.Brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 10, 18, 10), Child = sp };
            hide.Tick += (s, e) => { hide.Stop(); Hide(); };
            SourceInitialized += (s, e) => { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000); };
        }
        public void Flash(string msg)
        {
            ((System.Windows.Shapes.Path)ic).Fill = Accent.Brush; frame.BorderBrush = Accent.Brush;   // v1.6: follows the Hallow accent
            text.Text = msg;   // v1.11: the text and Show() had gone missing in 1.6, so the pop-up never appeared
            if (Program.Quiet) { try { File.AppendAllText(Path.Combine(ClipSettings.Dir, "toast.log"), DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine); } catch { } return; }   // tests: nothing on the screen
            if (!IsVisible) Show(); UpdateLayout();
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
            startHidden = hidden; rec = new Recorder(S); ClipOps.ExportChoice = S.ExportSound ?? "all";
            Title = "Hallow Clips"; Width = 820; Height = 820; MinWidth = 700; MinHeight = 560;
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
                    if (!toldTray && tray != null) { toldTray = true; Balloon("Hallow Clips", hostedByHub ? "Still running - Ctrl+Alt+S saves a clip while you play. It's in the Hallow icon's menu." : "Still running - Ctrl+Alt+S saves a clip while you play. Right-click the icon to quit."); }
                }
                else Quit();
            };
            if (Snap.On) { discordOn = Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_SNAP_DISCORD") == "1"; UpdateState(); FillClips(); return; }   // v1.11: snaps of the paused look
            try { games = ScanGames(); } catch { }
            SetupTray();
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            hotkeyOk = RegisterHotKey(hwnd, 1, 0x4003, 0x53);   // Ctrl+Alt+S (MOD_ALT | MOD_CONTROL | MOD_NOREPEAT)
            sessionKeyOk = RegisterHotKey(hwnd, 2, 0x4003, 0x52);   // v1.3: Ctrl+Alt+R starts / stops a session recording
            shotKeyOk = RegisterHotKey(hwnd, 3, 0x4003, 0x50);      // v1.8: Ctrl+Alt+P takes a screenshot
            markKeyOk = RegisterHotKey(hwnd, 4, 0x4003, 0x42);      // v1.8: Ctrl+Alt+B marks this moment
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == 0x0312 && wp.ToInt32() == 1) { SaveClip(null); handled = true; }
                if (msg == 0x0312 && wp.ToInt32() == 2) { ToggleSession(); handled = true; }
                if (msg == 0x0312 && wp.ToInt32() == 3) { Screenshot(); handled = true; }
                if (msg == 0x0312 && wp.ToInt32() == 4) { Mark(); handled = true; }
                return IntPtr.Zero;
            });
            var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            poll.Tick += (s, e) => { try { tracker.Poll(games.Concat(games.SelectMany(g => g.Alts)).ToList()); } catch { } Decide(); };
            poll.Start();
            var rescan = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) }; rescan.Tick += (s, e) => { try { games = ScanGames(); } catch { } }; rescan.Start();
            UpdateState(); FillClips();
            if (!hotkeyOk) Status("Ctrl+Alt+S is taken by another app - use the Save button or the tray menu, or close that app and restart Hallow Clips.");
            else if (!sessionKeyOk) Status("Ctrl+Alt+R is taken by another app - use the Record session button instead.");
            else if (!shotKeyOk || !markKeyOk) Status((!shotKeyOk ? "Ctrl+Alt+P" : "Ctrl+Alt+B") + " is taken by another app - use the Hallow icon menu instead.");
            DateTime lastCheck; if (!Program.Quiet && (!DateTime.TryParse(S.LastUpdateCheck, out lastCheck) || (DateTime.Now - lastCheck).TotalHours > 20)) CheckForUpdate(false);
            FindCodecs(); FinishLeftoverSessions(); FindWebcams();
            Dispatcher.BeginInvoke(new Action(EnforceLimit), DispatcherPriority.ApplicationIdle);
            Family.Serve("Clips", Command);
            var host = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; host.Tick += (s, e) => CheckTrayHost(); host.Start(); CheckTrayHost();
            // What's new: an update from 1.0 (which didn't remember versions) counts as seen 1.0 when FFmpeg was already set up
            if (string.IsNullOrEmpty(S.LastSeenVersion)) { S.LastSeenVersion = S.Ffmpeg != null ? "1.0" : Program.Version; S.Save(); }
            IsVisibleChanged += (s, e) => { if (IsVisible) Dispatcher.BeginInvoke(new Action(ShowWhatsNew), DispatcherPriority.ApplicationIdle); };
            WatchAccent();
            IsVisibleChanged += (s, e) => { if (IsVisible) Dispatcher.BeginInvoke(new Action(ShowCrashNotice), DispatcherPriority.ApplicationIdle); };
            int crashIn; if (int.TryParse(Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_CRASH"), out crashIn))   // tests only: crash on purpose
            { var ct = new DispatcherTimer { Interval = TimeSpan.FromSeconds(crashIn) }; ct.Tick += (s, e) => { throw new InvalidOperationException("Test crash (HALLOWCLIPS_TEST_CRASH)"); }; ct.Start(); }
            // v1.11: Discord Clips - looked for every 3 s (off the UI thread) while Clips would be recording
            var dc = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; dc.Tick += (s, e) => CheckDiscord(); dc.Start();
            if (!startHidden && !Program.Quiet) Show();
            Dispatcher.BeginInvoke(new Action(Decide), DispatcherPriority.ApplicationIdle);
        }
        // tests: HALLOWCLIPS_TEST_GAMEDIR = the folder of a stand-in game (the only game known); otherwise the real library
        static List<Game> ScanGames()
        {
            var t = Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_GAMEDIR");
            if (!string.IsNullOrEmpty(t)) return new List<Game> { new Game { Id = "test:game", Name = "Test Game", Source = "custom", InstallDir = t.TrimEnd('\\') } };
            return Library.Scan(new List<Game>());
        }

        // ---- v1.11: step aside for Discord Clips (both recording the same game = two encoders on the graphics card)
        readonly DiscordClips discord = new DiscordClips();
        bool discordOn, checkingDiscord, recordAnyway; string anywayFor, lastPausedFor;
        bool WouldRecord { get { return S.On && S.Ffmpeg != null && (!S.OnlyInGame || Current != null); } }
        // "Discord Clips" while we stand aside, otherwise null (also in status.json as pausedFor)
        string PausedFor { get { return WouldRecord && discordOn && S.DiscordClips != "both" && !recordAnyway ? DiscordClips.Name : null; } }
        bool DiscordChoice { get { return WouldRecord && discordOn && S.DiscordClips != "both"; } }   // "Record anyway" makes sense
        void CheckDiscord()
        {
            if (checkingDiscord) return;
            if (!WouldRecord || S.DiscordClips == "both") { if (discordOn) { discordOn = false; Decide(); } return; }
            checkingDiscord = true;
            new Thread(() =>
            {
                bool on = false; try { discord.Check(); on = discord.Recording; } catch { }
                Dispatcher.BeginInvoke(new Action(() => { checkingDiscord = false; if (on != discordOn) { discordOn = on; Decide(); } }));
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "discord clips" }.Start();
        }
        const string PausedMsg = "Discord Clips is recording this game - Hallow Clips is paused";
        void SetRecordAnyway(bool on) { recordAnyway = on; anywayFor = on ? GameName : null; Decide(); Status(on ? "Recording next to Discord Clips until this game closes." : PausedFor != null ? PausedMsg + "." : ""); }

        // ---- v1.6: the Hallow accent - follows theme.json live (debounced), Clips' own orange when off or missing
        FileSystemWatcher accentWatch; DispatcherTimer accentSoon, accentRetry;
        void WatchAccent()
        {
            accentSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) }; accentSoon.Tick += (s, e) => { accentSoon.Stop(); ApplyAccent(); };
            accentRetry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; accentRetry.Tick += (s, e) => StartAccentWatch(); accentRetry.Start();
            StartAccentWatch();
        }
        void StartAccentWatch()
        {
            if (accentWatch != null) return;
            try
            {
                var dir = Path.GetDirectoryName(Accent.FilePath); if (!Directory.Exists(dir)) return;   // tried again every 30 s
                var w = new FileSystemWatcher(dir, Path.GetFileName(Accent.FilePath)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime };
                FileSystemEventHandler h = (s, e) => Dispatcher.BeginInvoke(new Action(() => { accentSoon.Stop(); accentSoon.Start(); }));
                w.Changed += h; w.Created += h; w.Deleted += h; w.Renamed += (s, e) => h(s, null);
                w.EnableRaisingEvents = true; accentWatch = w; accentRetry.Stop();
                ApplyAccent();   // it may have changed while nobody watched
            }
            catch { }
        }
        void ApplyAccent()
        {
            try
            {
                var hex = Accent.Pick(S.UseHallowAccent); if (hex == Accent.Hex) return;
                Accent.Hex = hex; Program.LoadTheme(Application.Current);
                var st = statusText != null ? statusText.Text : ""; Content = Build(); UpdateState(); FillClips(); SessionState(); Status(st);
            }
            catch { }
        }
        // ---- v1.6: after a crash, a notice once per report (when the window is open)
        void ShowCrashNotice()
        {
            if (!IsVisible || Snap.On) return;
            var f = Crash.Files().FirstOrDefault(); if (f == null || f.Name == S.LastCrashShown) return;
            S.LastCrashShown = f.Name; S.Save();
            if (f.LastWriteTime < DateTime.Now.AddDays(-30)) return;
            new CrashNotice(f.FullName) { Owner = this }.Show();
        }
        bool hotkeyOk, sessionKeyOk;
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
            // v1.5: Hallow Hub's one tray icon - open, our menu items, run one, quit
            if (cmd == "open") return OnUi(() => ShowFromTray()) ? "ok" : "error busy";
            if (cmd == "menu")
            {
                List<object> items = null;
                if (!OnUi(() => items = MenuItems())) return "error busy";
                return "ok " + Family.Json(new Dictionary<string, object> { { "app", "Clips" }, { "items", items } });
            }
            if (cmd == "invoke")
            {
                var id = sp < 0 ? "" : line.Substring(sp + 1).Trim(); string err = null;
                if (!OnUi(() => err = Invoke(id))) return "error busy";
                return err == null ? "ok" : "error " + err;
            }
            if (cmd == "quit") { new Timer(_ => Dispatcher.BeginInvoke(new Action(Quit)), null, 300, Timeout.Infinite); return "ok"; }   // reply first, then exit
            return "error unknown command";
        }
        bool OnUi(Action a) { var op = Dispatcher.BeginInvoke(a); return op.Wait(TimeSpan.FromSeconds(5)) == DispatcherOperationStatus.Completed; }
        List<object> MenuItems()
        {
            Func<string, string, object, Dictionary<string, object>> it = (id, label, chk) => { var d = new Dictionary<string, object> { { "id", id }, { "label", label } }; if (chk != null) d["checked"] = chk; return d; };
            var save = it("save", "Save clip", null); if (!rec.Running) save["enabled"] = false;
            var record = it("record", "Record session", session != null); if (finishing || S.Ffmpeg == null) record["enabled"] = false;
            var mark = it("mark", "Add marker", null); if (!rec.Running && session == null) mark["enabled"] = false;
            var list = new List<object> { save, record, it("screenshot", "Take screenshot", null), mark, new Dictionary<string, object> { { "id", "-" } }, it("replay", "Replay buffer", S.On), it("folder", "Open clips folder", null) };
            if (DiscordChoice) list.Insert(5, it("anyway", "Record anyway this session", recordAnyway));   // v1.11: only while Discord Clips records this game
            return list;
        }
        string Invoke(string id)
        {
            if (id == "save") { SaveClip(null); return null; }
            if (id == "record") { ToggleSession(); return null; }
            if (id == "replay") { S.On = !S.On; S.Save(); if (replaySwitch != null) replaySwitch.IsChecked = S.On; Restart(); return null; }
            if (id == "screenshot") { Screenshot(); return null; }
            if (id == "mark") { Mark(); return null; }
            if (id == "anyway") { if (!DiscordChoice) return "Discord Clips isn't recording"; SetRecordAnyway(!recordAnyway); return null; }
            if (id == "folder") { try { Directory.CreateDirectory(S.ClipsFolder); Process.Start("explorer.exe", "\"" + S.ClipsFolder + "\""); return null; } catch (Exception e) { return Family.OneLine(e.Message); } }
            return "unknown item";
        }
        CheckBox replaySwitch;

        // ---- v1.5: "Use the Hallow icon" - while Hallow Hub hosts the family icon, ours is hidden (never both gone)
        bool hostedByHub, checkingHost;
        static string HubPipe { get { var t = Environment.GetEnvironmentVariable("HALLOWCLIPS_TEST_HUB"); return string.IsNullOrEmpty(t) ? "Hub" : t; } }   // tests use a stand-in Hub
        void CheckTrayHost()
        {
            if (tray == null || checkingHost) return;
            if (!S.UseHallowIcon) { SetHosted(false); return; }
            checkingHost = true;
            new Thread(() =>
            {
                var r = Family.Send(HubPipe, "tray-host", 1500);
                bool on = r != null && r.StartsWith("ok ") && System.Text.RegularExpressions.Regex.IsMatch(r, "\"on\"\\s*:\\s*true");
                Dispatcher.BeginInvoke(new Action(() => { checkingHost = false; SetHosted(on && S.UseHallowIcon); }));
            }) { IsBackground = true }.Start();
        }
        void SetHosted(bool on) { hostedByHub = on; if (tray != null && !exiting) tray.Visible = !on && !Program.Quiet; WriteStatus(false); }
        // a tray balloon - through Hub's icon while ours is hidden (ours shows briefly if Hub doesn't answer)
        void Balloon(string title, string text)
        {
            if (tray == null) return;
            if (Program.Quiet && !hostedByHub) { try { File.AppendAllText(Path.Combine(ClipSettings.Dir, "balloon.log"), DateTime.Now.ToString("HH:mm:ss") + " " + title + " | " + text + Environment.NewLine); } catch { } return; }   // tests
            if (!hostedByHub) { tray.ShowBalloonTip(4000, title, text, WF.ToolTipIcon.None); return; }
            new Thread(() =>
            {
                var r = Family.Send(HubPipe, "notify Clips\t" + title + "\t" + text, 3000);
                if ((r == null || !(r == "ok" || r.StartsWith("ok "))) && !Program.Quiet) Dispatcher.BeginInvoke(new Action(() => { tray.Visible = true; tray.ShowBalloonTip(4000, title, text, WF.ToolTipIcon.None); }));   // the next check hides it again
            }) { IsBackground = true }.Start();
        }

        // ---- status.json: what other Hallow apps read (Hub's game page, Toolkit's Discord post)
        readonly Dictionary<string, Tuple<long, DateTime, double>> durations = new Dictionary<string, Tuple<long, DateTime, double>>(StringComparer.OrdinalIgnoreCase);
        List<FileInfo> RecentClips(int n)
        {
            try { if (Directory.Exists(S.ClipsFolder)) return new DirectoryInfo(S.ClipsFolder).GetFiles("*.mp4", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTime).Take(n).ToList(); } catch { }
            return new List<FileInfo>();
        }
        readonly Dictionary<string, Tuple<long, DateTime, string>> titles = new Dictionary<string, Tuple<long, DateTime, string>>(StringComparer.OrdinalIgnoreCase);
        string TitleOf(FileInfo f)
        {
            Tuple<long, DateTime, string> c;
            if (titles.TryGetValue(f.FullName, out c) && c.Item1 == f.Length && c.Item2 == f.LastWriteTimeUtc) return c.Item3;
            var s = Mp4.Title(f.FullName); titles[f.FullName] = Tuple.Create(f.Length, f.LastWriteTimeUtc, s); return s;
        }
        double DurationOf(FileInfo f)
        {
            Tuple<long, DateTime, double> c;
            if (durations.TryGetValue(f.FullName, out c) && c.Item1 == f.Length && c.Item2 == f.LastWriteTimeUtc) return c.Item3;
            var d = Math.Round(Mp4.Duration(f.FullName), 1); durations[f.FullName] = Tuple.Create(f.Length, f.LastWriteTimeUtc, d); return d;
        }
        string StatusJson()
        {
            // v1.4: a clip's folder is its game's name made safe for Windows ("Warcraft III: Reforged" -> "Warcraft III_ Reforged");
            // other apps get the real name back, and the install folder so they can match a game whose name differs
            var byFolder = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in games) { var k = Recorder.Safe(g.Name); if (!byFolder.ContainsKey(k)) byFolder[k] = g; }
            var clips = new List<object>();
            foreach (var f in RecentClips(30))
            {
                var jpg = ThumbPath(f.FullName); Game g; byFolder.TryGetValue(f.Directory.Name, out g);
                clips.Add(new Dictionary<string, object> { { "file", f.FullName }, { "game", g != null ? g.Name : f.Directory.Name }, { "installDir", g != null ? g.InstallDir : null }, { "time", Family.Time(f.LastWriteTime) }, { "sizeBytes", f.Length },
                    { "durationSec", DurationOf(f) }, { "title", TitleOf(f) }, { "thumb", File.Exists(jpg) ? jpg : null }, { "favorite", S.IsFavorite(f.FullName) } });
            }
            var cur = Current; var cg = cur == null ? null : cur.Game.Parent ?? cur.Game;
            return Family.Json(new Dictionary<string, object> { { "app", "Clips" }, { "version", Program.Version }, { "updated", Family.Time(DateTime.Now) },
                { "recording", rec.Running }, { "pausedFor", PausedFor }, { "game", cg != null ? cg.Name : null }, { "installDir", cg != null ? cg.InstallDir : null }, { "seconds", S.Seconds }, { "folder", S.ClipsFolder }, { "trayHosted", hostedByHub }, { "lastCrash", Crash.Last().HasValue ? Family.Time(Crash.Last().Value) : null }, { "clips", clips } });
        }
        string lastStatusKey;
        // writes status.json when something changed (force: the clip list changed)
        void WriteStatus(bool force)
        {
            if (Snap.On) return;
            var key = rec.Running + "|" + PausedFor + "|" + (Current != null ? GameName : "") + "|" + S.Seconds + "|" + S.ClipsFolder + "|" + hostedByHub;
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
            if (recordAnyway && anywayFor != GameName) recordAnyway = false;   // v1.11: "this session" = until the game closes or changes
            var paused = PausedFor;
            bool want = WouldRecord && paused == null;   // v1.11: not while Discord Clips records the same game (unless you said keep both)
            var gp = S.PerAppSound && Current != null ? Current.Pid : 0;
            if (want && rec.Running && S.PerAppSound && gp != rec.GamePid && ProcessAudio.Supported) rec.Stop();   // v1.9: a game started or ended - restart so it gets its own track
            if (want && !rec.Running && rec.Crashed && (DateTime.Now - rec.Started).TotalSeconds < 20)
            {
                // v1.10: FFmpeg stopped right after starting - try again without the webcam, then without the memory buffer
                if (rec.WebcamUsed && !rec.NoWebcam) { rec.NoWebcam = true; Status("The webcam couldn't be opened (in use by another app or unplugged) - recording without it."); }
                else if (S.RamBuffer && !rec.NoRam) { rec.NoRam = true; Status("The replay couldn't be kept in memory - using the disk instead."); }
            }
            if (want && !rec.Running) { rec.GamePid = gp; if (!rec.Start()) Status(rec.Error); }
            else if (!want && rec.Running) rec.Stop();   // also when Discord Clips starts: FFmpeg is told to stop cleanly (q), killed after 3 s
            if (paused != lastPausedFor)
            {
                if (paused != null) Status(PausedMsg + " (Record anyway is in the menu).");
                else if (lastPausedFor != null && want) Status("Discord Clips stopped - Hallow Clips is recording again.");
                lastPausedFor = paused;
            }
            UpdateState(); WriteStatus(false);
        }

        // done (optional) gets the saved file, or null and why not
        readonly List<Action<string, string>> waiting = new List<Action<string, string>>();
        void SaveClip(Action<string, string> done)
        {
            if (saving) { if (done != null) waiting.Add(done); return; }   // already saving: that clip is the answer
            if (!rec.Running && PausedFor != null)
            {
                // v1.11: standing aside for Discord Clips - say so (Discord's own hotkey saves its clip)
                toast.Flash(PausedMsg); if (hostedByHub) Balloon("Hallow Clips", PausedMsg + ". Use Discord's clip hotkey, or Record anyway in the menu.");
                Status(PausedMsg + "."); if (done != null) done(null, PausedMsg); return;
            }
            if (!rec.Running) { toast.Flash(S.Ffmpeg == null ? "Hallow Clips isn't set up yet" : "Not recording right now"); if (done != null) done(null, "Nothing recorded yet"); return; }
            saving = true; if (done != null) waiting.Add(done); var name = GameName; var secs = S.Seconds;
            toast.Flash("Saving the last " + secs + " seconds...");
            new Thread(() =>
            {
                var mk = marks.ToList(); string err; var f = rec.Save(secs, name, mk, out err);
                // Hallow Overlay shows "Clip saved" on its panel when it runs; otherwise our own pop-up does
                bool overlay = false;
                if (f != null && !Program.Quiet) { var r = Family.Send("Overlay", "flash Clip saved", 3000); overlay = r != null && (r == "ok" || r.StartsWith("ok ")); }
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
            title.Children.Add(new TextBlock { Text = "HALLOW", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Accent.Brush, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = " CLIPS", FontSize = 22, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(title, Dock.Left); head.Children.Add(title);
            var saveNow = new Button { Content = "Save a clip now", Style = (Style)FindResource("Accent"), ToolTip = "Same as Ctrl+Alt+S" };
            saveNow.Click += (s, e) => SaveClip(null);
            sessionBtn = new Button { Content = "Record session", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Record everything from now until you stop (Ctrl+Alt+R)" };
            sessionBtn.Click += (s, e) => ToggleSession();
            var hb = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            updateBtn = new Button { Content = "Update", Style = (Style)FindResource("Accent"), Margin = new Thickness(0, 0, 8, 0), Visibility = pending != null ? Visibility.Visible : Visibility.Collapsed };
            if (pending != null) updateBtn.Content = "Update to v" + pending.Version.ToString(2);
            updateBtn.Click += (s, e) => InstallUpdate();
            // v1.10: the settings fold away (there are many now) - the clips get the room
            var gear = new Button { Content = "Settings", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Show or hide the settings" };
            gear.Click += (s, e) => { showSettings = !showSettings; if (settingsPanel != null) settingsPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed; };
            hb.Children.Add(updateBtn); hb.Children.Add(gear); hb.Children.Add(sessionBtn); hb.Children.Add(saveNow); head.Children.Add(hb);
            root.Children.Add(head);
            // status card
            var card = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(card, Dock.Top);
            var cs = new StackPanel();
            var sr = new StackPanel { Orientation = Orientation.Horizontal };
            dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            sr.Children.Add(dot);
            state = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 }; sr.Children.Add(state);
            // v1.11: while Discord Clips records this game
            anywayBtn = new Button { Content = "Record anyway", Style = (Style)FindResource("Btn"), Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Record with Hallow Clips too, next to Discord Clips, until this game closes" };
            anywayBtn.Click += (s, e) => { if (DiscordChoice) SetRecordAnyway(!recordAnyway); }; sr.Children.Add(anywayBtn);
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
            settingsPanel = set; if (S.Ffmpeg == null) showSettings = true; set.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
            Func<string, IEnumerable<Tuple<string, string>>, Func<string>, Action<string>, FrameworkElement> chips = null;
            Func<string, IEnumerable<Tuple<string, string>>, Func<string>, Action<string>, bool, FrameworkElement> chipRow = (label, opts, get, setv, restart) =>
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var l = new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) }; DockPanel.SetDock(l, Dock.Left); row.Children.Add(l);
                var wp = new WrapPanel();
                foreach (var o in opts)
                {
                    var key = o.Item1; var c = new ToggleButton { Content = o.Item2, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = get() == key, Padding = new Thickness(12, 5, 12, 5) };
                    c.Click += (s, e) => { setv(key); S.Save(); foreach (ToggleButton x in wp.Children) x.IsChecked = x == c; if (restart) Restart(); };
                    wp.Children.Add(c);
                }
                row.Children.Add(wp); return row;
            };
            chips = (label, opts, get, setv) => chipRow(label, opts, get, setv, true);
            set.Children.Add(chips("Clip length", new[] { "15", "30", "60", "120" }.Select(x => Tuple.Create(x, x == "120" ? "2 min" : x + " s")), () => S.Seconds.ToString(), v => S.Seconds = int.Parse(v)));
            set.Children.Add(chips("Quality", new[] { Tuple.Create("medium", "Smaller files"), Tuple.Create("high", "High"), Tuple.Create("max", "Best") }, () => S.Quality, v => S.Quality = v));
            set.Children.Add(chips("Frame rate", new[] { Tuple.Create("30", "30 fps"), Tuple.Create("60", "60 fps") }, () => S.Fps.ToString(), v => S.Fps = int.Parse(v)));
            var screens = WF.Screen.AllScreens.Length;
            if (screens > 1) set.Children.Add(chips("Screen", Enumerable.Range(0, screens).Select(i => Tuple.Create(i.ToString(), "Screen " + (i + 1))), () => S.Screen.ToString(), v => S.Screen = int.Parse(v)));
            // v1.3: video format - only what this PC can record (found with a one-frame test encode, see FindCodecs)
            codecRow = new ContentControl(); set.Children.Add(codecRow);
            FillCodecRow = () =>
            {
                var have = new List<string> { "h264" }; if (S.Codecs != null) have.AddRange(S.Codecs.Where(c => c == "hevc" || c == "av1"));
                if (have.Count < 2) { codecRow.Content = null; return; }
                var names = new Dictionary<string, string> { { "h264", "H.264 (plays everywhere)" }, { "hevc", "HEVC (smaller)" }, { "av1", "AV1 (smallest)" } };
                codecRow.Content = chips("Video format", have.Select(c => Tuple.Create(c, names[c])), () => have.Contains(S.Codec) ? S.Codec : "h264", v => S.Codec = v);
                codecRow.ToolTip = "HEVC and AV1 make smaller files at the same quality. Windows needs the HEVC / AV1 Video Extension to play them in its own player; Discord copies are always H.264.";
            };
            FillCodecRow();
            // v1.3: space limit for the clips folder (off by default)
            var limit = chipRow("Space limit", new[] { Tuple.Create("0", "Off"), Tuple.Create("10", "10 GB"), Tuple.Create("25", "25 GB"), Tuple.Create("50", "50 GB"), Tuple.Create("100", "100 GB") },
                () => S.SpaceLimitGB.ToString(), v => { S.SpaceLimitGB = int.Parse(v); Dispatcher.BeginInvoke(new Action(EnforceLimit)); }, false);
            ((FrameworkElement)limit).ToolTip = "When the clips folder grows past this, the oldest clips that aren't favourites go to the Recycle Bin.";
            set.Children.Add(limit);
            // v1.9: separate sound per app, and which tracks go into exports
            var apps = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var al = new TextBlock { Text = "Own sound tracks", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }; DockPanel.SetDock(al, Dock.Left); apps.Children.Add(al);
            var awp = new WrapPanel();
            var groups = new[] { Tuple.Create("Game", new string[0]), Tuple.Create("Discord", new[] { "Discord" }), Tuple.Create("Spotify", new[] { "Spotify" }), Tuple.Create("Browser", new[] { "chrome", "msedge", "firefox" }), Tuple.Create("TeamSpeak", new[] { "ts3client_win64", "TeamSpeak" }) };
            bool supported = ProcessAudio.Supported;
            foreach (var g in groups)
            {
                var grp = g; bool isGame = g.Item2.Length == 0;
                var on = isGame ? S.PerAppSound && S.GameTrack : S.PerAppSound && g.Item2.Any(x => (S.AppTracks ?? new List<string>()).Contains(x, StringComparer.OrdinalIgnoreCase));
                var c = new ToggleButton { Content = g.Item1, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = on && supported, Padding = new Thickness(12, 5, 12, 5), IsEnabled = supported };
                c.Click += (s, e) =>
                {
                    if (S.AppTracks == null) S.AppTracks = new List<string>();
                    if (!S.PerAppSound) { S.GameTrack = false; S.AppTracks.Clear(); }   // off before: start from nothing ticked
                    if (isGame) S.GameTrack = c.IsChecked == true;
                    else { S.AppTracks.RemoveAll(x => grp.Item2.Contains(x, StringComparer.OrdinalIgnoreCase)); if (c.IsChecked == true) S.AppTracks.AddRange(grp.Item2); }
                    S.PerAppSound = S.GameTrack || S.AppTracks.Count > 0;
                    S.Save(); foreach (ToggleButton x in awp.Children) { var nm = x.Content as string; var gg = groups.First(q => q.Item1 == nm); x.IsChecked = S.PerAppSound && (gg.Item2.Length == 0 ? S.GameTrack : gg.Item2.Any(y => S.AppTracks.Contains(y, StringComparer.OrdinalIgnoreCase))); }
                    Restart();
                };
                awp.Children.Add(c);
            }
            apps.Children.Add(awp);
            apps.ToolTip = supported ? "Each app you pick gets its own sound track next to everything + your mic (the game is the one that's running). An app started after recording began gets its track at the next start." : "Needs Windows 10 version 2004 or newer - your clips keep everything + mic.";
            set.Children.Add(apps);
            // v1.10: webcam in a corner (cameras are listed in the background; listing doesn't switch one on)
            webcamRow = new ContentControl(); set.Children.Add(webcamRow);
            FillWebcamRow = () =>
            {
                var cams = webcams ?? new List<string>();
                if (cams.Count == 0 && string.IsNullOrEmpty(S.Webcam)) { webcamRow.Content = null; return; }
                var all = new List<string>(cams); if (!string.IsNullOrEmpty(S.Webcam) && !all.Contains(S.Webcam)) all.Add(S.Webcam);
                var sp2 = new StackPanel();
                sp2.Children.Add(chips("Webcam", new[] { Tuple.Create("", "Off") }.Concat(all.Select(c => Tuple.Create(c, c.Length > 28 ? c.Substring(0, 26) + "..." : c))), () => S.Webcam ?? "", v => { S.Webcam = v; Dispatcher.BeginInvoke(new Action(FillWebcamRow)); }));
                if (!string.IsNullOrEmpty(S.Webcam))
                {
                    sp2.Children.Add(chips("Webcam corner", new[] { Tuple.Create("tl", "Top left"), Tuple.Create("tr", "Top right"), Tuple.Create("bl", "Bottom left"), Tuple.Create("br", "Bottom right") }, () => S.WebcamCorner ?? "br", v => S.WebcamCorner = v));
                    sp2.Children.Add(chips("Webcam size", new[] { Tuple.Create("small", "Small"), Tuple.Create("medium", "Medium"), Tuple.Create("large", "Large") }, () => S.WebcamSize ?? "medium", v => S.WebcamSize = v));
                }
                webcamRow.Content = sp2;
                webcamRow.ToolTip = "Your webcam in a corner of clips and sessions. A session started while the replay uses the camera records without it (a camera can only be opened once).";
            };
            FillWebcamRow();
            set.Children.Add(chipRow("Sound in exports", new[] { Tuple.Create("all", "Everything + mic"), Tuple.Create("nomic", "Everything, no mic"), Tuple.Create("game", "Game only"), Tuple.Create("gamemic", "Game + mic") },
                () => S.ExportSound ?? "all", v => { S.ExportSound = v; ClipOps.ExportChoice = v; }, false));
            // v1.11: Discord Clips records the same game with its own encoder - by default Clips steps aside
            var dcRow = chipRow("When Discord Clips is also recording", new[] { Tuple.Create("pause", "Let Discord record - pause Hallow Clips"), Tuple.Create("both", "Keep both recording") },
                () => S.DiscordClips == "both" ? "both" : "pause", v => { S.DiscordClips = v; Dispatcher.BeginInvoke(new Action(() => { CheckDiscord(); Decide(); })); }, false);
            ((FrameworkElement)dcRow).ToolTip = "Discord's own Clips records the same game on the graphics card, so two recorders would double the load. Paused, Hallow Clips starts again by itself a few seconds after Discord Clips stops; Record anyway (menu or above) keeps both for this game.";
            set.Children.Add(dcRow);
            Func<string, bool, Action<bool>, CheckBox> sw = (text, on, setv) =>
            {
                var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, Style = (Style)FindResource("Switch") };
                c.Click += (s, e) => { setv(c.IsChecked == true); S.Save(); Restart(); }; set.Children.Add(c); return c;
            };
            replaySwitch = sw("Instant replay on", S.On, v => S.On = v);
            sw("Only while a game is running (otherwise it records your desktop too)", S.OnlyInGame, v => S.OnlyInGame = v);
            sw("Record my mic on its own track", S.Mic, v => S.Mic = v);
            sw("Keep the replay in memory instead of on disk (spares your SSD; a 2 min buffer at Best can use about 1 GB of RAM)", S.RamBuffer, v => S.RamBuffer = v);
            sw("Keep running in the tray when I close this window", S.TrayOnClose, v => S.TrayOnClose = v);
            sw("Start with Windows (in the tray)", StartsWithWindows(), v => SetStartWithWindows(v));
            // v1.5: one tray icon for the whole Hallow family - Hallow Hub hosts it when it runs
            var hi = new CheckBox { Content = new TextBlock { Text = "Use the Hallow icon (when Hallow Hub runs, Clips is in its tray menu instead of having its own icon)", TextWrapping = TextWrapping.Wrap }, IsChecked = S.UseHallowIcon, Style = (Style)FindResource("Switch") };
            hi.Click += (s, e) => { S.UseHallowIcon = hi.IsChecked == true; S.Save(); CheckTrayHost(); }; set.Children.Add(hi);
            var ha = new CheckBox { Content = new TextBlock { Text = "Use the Hallow accent (the colour you pick in Hallow Toolkit)", TextWrapping = TextWrapping.Wrap }, IsChecked = S.UseHallowAccent, Style = (Style)FindResource("Switch") };
            ha.Click += (s, e) => { S.UseHallowAccent = ha.IsChecked == true; S.Save(); Dispatcher.BeginInvoke(new Action(ApplyAccent)); }; set.Children.Add(ha);
            // v1.8: hotkeys and updates
            var keys = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var upd = new Button { Content = "Check for updates", Style = (Style)FindResource("Btn") }; upd.Click += (s, e) => CheckForUpdate(true);
            DockPanel.SetDock(upd, Dock.Right); keys.Children.Add(upd);
            keys.Children.Add(new TextBlock { Text = "Hallow Clips " + Program.Version + "   \u00B7   Ctrl+Alt+S clip   \u00B7   Ctrl+Alt+R session   \u00B7   Ctrl+Alt+P screenshot   \u00B7   Ctrl+Alt+B marker", Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center });
            set.Children.Add(keys);
            root.Children.Add(set);
            // clips
            var ch = new DockPanel { Margin = new Thickness(0, 6, 0, 8) }; DockPanel.SetDock(ch, Dock.Top);
            var openF = new Button { Content = "Open clips folder", Style = (Style)FindResource("Btn") };
            openF.Click += (s, e) => { Directory.CreateDirectory(S.ClipsFolder); Process.Start("explorer.exe", "\"" + S.ClipsFolder + "\""); };
            DockPanel.SetDock(openF, Dock.Right); ch.Children.Add(openF);
            var reel = new Button { Content = "Highlight reel", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Join several clips into one video" };
            reel.Click += (s, e) => MakeReel(); DockPanel.SetDock(reel, Dock.Right); ch.Children.Add(reel);
            ch.Children.Add(new TextBlock { Text = "YOUR CLIPS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), VerticalAlignment = VerticalAlignment.Center });
            root.Children.Add(ch);
            statusText = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(2, 8, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel.SetDock(statusText, Dock.Bottom); root.Children.Add(statusText);
            clipsList = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = clipsList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            return root;
        }

        StackPanel settingsPanel; bool showSettings; Button anywayBtn;
        void Restart() { if (rec.Running) rec.Stop(); rec.NoWebcam = false; rec.NoRam = false; Decide(); }

        void UpdateState()
        {
            var ok = S.Ffmpeg != null;
            setupBtn.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            ffState.Text = ok ? "Records with FFmpeg on your graphics card - " + S.Seconds + " s clips at " + S.Fps + " fps. Clips go to " + S.ClipsFolder + "." :
                "Hallow Clips records with FFmpeg (free). It downloads once from GitHub and is checked before use.";
            bool on = rec.Running || (Snap.On && ok);
            dot.Background = on ? B("#E0424C") : B("#5A5268");
            var paused = PausedFor;   // v1.11
            if (paused != null) dot.Background = B("#E0A030");
            if (trayAnyway != null) { trayAnyway.Visible = DiscordChoice; trayAnyway.Checked = recordAnyway; }
            if (anywayBtn != null) { anywayBtn.Visibility = DiscordChoice ? Visibility.Visible : Visibility.Collapsed; anywayBtn.Content = recordAnyway ? "Pause for Discord" : "Record anyway"; }
            state.Text = !ok ? "Not set up yet" : !S.On ? "Instant replay is off" : paused != null ? "Paused - Discord Clips is recording " + (Current != null ? GameName : "your screen") + ", so only one recorder runs" : on ? "Recording " + (Current != null ? GameName : "your screen") + " - Ctrl+Alt+S saves the last " + S.Seconds + " s"
                : S.OnlyInGame ? "Waiting for a game - it starts by itself" : "Starting...";
            if (tray != null) tray.Text = rec.Running ? "Hallow Clips - recording (Ctrl+Alt+S)" : paused != null ? "Hallow Clips - paused for Discord Clips" : "Hallow Clips";
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
                star.Foreground = fav ? Accent.Brush : (Brush)FindResource("Muted");
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
                // v1.7: title, thumbnail, GIF and phone exports
                var more = ib("More", "Title, thumbnail, GIF, phone version, show in folder");
                var cm = new ContextMenu();
                Action<string, Action> mi = (label, act) => { var m = new MenuItem { Header = label }; m.Click += (s, e) => act(); cm.Items.Add(m); };
                mi("Set title...", () => TitleClip(fi.FullName)); mi("Pick thumbnail...", () => PickThumb(fi.FullName));
                mi("Export GIF...", () => GifClip(fi.FullName)); mi("Phone version (9:16)...", () => PhoneClip(fi.FullName)); mi("Show in folder", () => { try { Process.Start("explorer.exe", "/select,\"" + fi.FullName + "\""); } catch { } });
                more.ContextMenu = cm; more.Click += (s, e) => { cm.PlacementTarget = more; cm.Placement = PlacementMode.Bottom; cm.IsOpen = true; };
                foreach (var b in new[] { play, star, trim, discord, ren, more, del }) btns.Children.Add(b);
                if (busy.Contains(fi.FullName)) foreach (var b in new[] { trim, discord, ren, more, del }) b.IsEnabled = false;
                DockPanel.SetDock(btns, Dock.Right); dp.Children.Add(btns);
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var name = Path.GetFileNameWithoutExtension(fi.Name); var game = fi.Directory.Name;
                bool own = !System.Text.RegularExpressions.Regex.IsMatch(name, @"^" + System.Text.RegularExpressions.Regex.Escape(game) + @" \d{4}-\d\d-\d\d \d\d-\d\d-\d\d$");   // renamed by you
                var title = TitleOf(fi);   // v1.7: your title (stored in the MP4) comes first
                t.Children.Add(new TextBlock { Text = title ?? (own ? name : game), FontSize = 14, FontWeight = FontWeights.SemiBold, ToolTip = fi.Name });
                if (title != null) own = true;
                var len = DurationOf(fi);
                t.Children.Add(new TextBlock { Text = (own ? game + "   \u00B7   " : "") + fi.LastWriteTime.ToString("ddd d MMM, HH:mm") + "   \u00B7   " + (len > 0 ? TrimWindow.Clock(len).Split('.')[0] + "   \u00B7   " : "") + (fi.Length / 1048576.0).ToString("0.0") + " MB", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5 });
                dp.Children.Add(t); row.Child = dp; clipsList.Children.Add(row);
            }
        }

        // ---- v1.3: record a whole session (start / stop) - its own recorder next to the replay buffer, straight to a file
        Recorder session; Button sessionBtn; WF.ToolStripMenuItem traySession, trayAnyway; DispatcherTimer sessionTick; bool finishing;
        void ToggleSession()
        {
            if (finishing) return;
            if (session != null) { StopSession(); return; }
            if (S.Ffmpeg == null) { toast.Flash("Hallow Clips isn't set up yet"); return; }
            var r = new Recorder(S); var name = GameName; r.SessionFile = r.SessionName(name); r.NoWebcam = rec.Running && rec.WebcamUsed;   // the camera is busy in the replay
            r.GamePid = S.PerAppSound && Current != null ? Current.Pid : 0;
            if (!r.Start()) { toast.Flash("Couldn't start recording"); Status(r.Error); return; }
            session = r;
            toast.Flash("Recording the whole session - Ctrl+Alt+R stops");
            Status("Recording a session to " + Path.GetFileName(r.SessionFile) + ".");
            if (sessionTick == null) { sessionTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; sessionTick.Tick += (s, e) => SessionState(); }
            sessionTick.Start(); SessionState();
        }
        void SessionState()
        {
            if (session != null && !session.Running && !finishing) { Status("The session recording stopped by itself (" + Family.OneLine(session.LastLog.Split('\n').LastOrDefault(l => l.Trim().Length > 0)) + ")."); StopSession(); return; }
            var len = session == null ? "" : TimeSpan.FromSeconds((int)(DateTime.Now - session.Started).TotalSeconds).ToString(@"h\:mm\:ss");
            sessionBtn.Content = finishing ? "Finishing..." : session != null ? "Stop recording " + len : "Record session";
            sessionBtn.IsEnabled = !finishing;
            sessionBtn.Foreground = session != null ? B("#FF6B73") : (Brush)FindResource("Text");
            if (traySession != null) traySession.Text = session != null ? "Stop recording (" + len + ")   (Ctrl+Alt+R)" : "Record session   (Ctrl+Alt+R)";
        }
        void StopSession()
        {
            var r = session; session = null; if (r == null) return;
            if (sessionTick != null) sessionTick.Stop();
            finishing = true; SessionState(); var ff = S.Ffmpeg; var mins = (DateTime.Now - r.Started).TotalMinutes;
            new Thread(() =>
            {
                r.Stop();
                var mk = marks.ToList(); string err; var f = Recorder.FinishSession(ff, Path.ChangeExtension(r.SessionFile, ".mkv"), mk, r.Started, out err);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    finishing = false; SessionState();
                    if (f != null) { toast.Flash("Session saved - " + (mins < 1 ? (int)(mins * 60) + " s" : (int)mins + " min")); Status("Saved " + Path.GetFileName(f)); }
                    else { toast.Flash("Session recording kept as .mkv"); Status(err); }
                    FillClips(); EnforceLimit();
                }));
            }) { IsBackground = true }.Start();
        }
        // a session left as .mkv (the app was closed while recording) becomes an MP4 now
        void FinishLeftoverSessions()
        {
            var ff = S.Ffmpeg; var folder = S.ClipsFolder; if (ff == null || !Directory.Exists(folder)) return;
            new Thread(() =>
            {
                bool any = false;
                try
                {
                    foreach (var mkv in Directory.GetFiles(folder, "* session.mkv", SearchOption.AllDirectories))
                    {
                        if (File.Exists(Path.ChangeExtension(mkv, ".mp4"))) continue;
                        try { using (File.Open(mkv, FileMode.Open, FileAccess.Read, FileShare.None)) { } } catch { continue; }   // still being written
                        string err; if (Recorder.FinishSession(ff, mkv, out err) != null) any = true;
                    }
                }
                catch { }
                if (any) Dispatcher.BeginInvoke(new Action(FillClips));
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }

        // ---- v1.3: which video formats this PC can record (one-frame test encodes; remembered until FFmpeg or the graphics card changes)
        ContentControl codecRow; Action FillCodecRow;
        void FindCodecs()
        {
            var ff = S.Ffmpeg; if (ff == null) return;
            new Thread(() =>
            {
                string key; try { key = ff + "|" + new FileInfo(ff).Length + "|" + Recorder.Encoder(ff); } catch { return; }
                if (key == S.CodecsFor && S.Codecs != null) return;
                var ok = new List<string>(); foreach (var c in new[] { "hevc", "av1" }) if (Recorder.CanEncode(ff, c)) ok.Add(c);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    S.Codecs = ok; S.CodecsFor = key;
                    if (S.Codec != "h264" && !ok.Contains(S.Codec)) { S.Codec = "h264"; Restart(); }   // e.g. a new graphics card
                    S.Save(); FillCodecRow();
                }));
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }

        // ---- v1.3: space limit - over the limit, the oldest clips that aren't favourites go to the Recycle Bin
        bool enforcing;
        void EnforceLimit()
        {
            if (S.SpaceLimitGB <= 0 || enforcing || Snap.On) return;
            enforcing = true; long limit = S.SpaceLimitGB * 1000L * 1000 * 1000; var folder = S.ClipsFolder;
            var keep = new HashSet<string>(S.Favorites ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var recording = session != null ? Path.ChangeExtension(session.SessionFile, ".mkv") : null;
            new Thread(() =>
            {
                int n = 0; long freed = 0; string err = null;
                try
                {
                    if (Directory.Exists(folder))
                    {
                        var files = new DirectoryInfo(folder).GetFiles("*.*", SearchOption.AllDirectories).Where(f => f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)).ToList();
                        long total = files.Sum(f => f.Length);
                        foreach (var f in files.OrderBy(f => f.LastWriteTime))
                        {
                            if (total <= limit) break;
                            if (keep.Contains(f.FullName) || string.Equals(f.FullName, recording, StringComparison.OrdinalIgnoreCase) || f.LastWriteTime > DateTime.Now.AddMinutes(-2)) continue;   // favourites, the recording, brand-new files
                            try
                            {
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f.FullName, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                                total -= f.Length; freed += f.Length; n++;
                                try { File.Delete(ThumbPath(f.FullName)); } catch { }
                            }
                            catch (Exception ex) { err = ex.Message; }
                        }
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    enforcing = false;
                    if (n > 0) { Status("Space limit " + S.SpaceLimitGB + " GB: moved " + n + " old clip" + (n == 1 ? "" : "s") + " (" + (freed / 1e9).ToString("0.0") + " GB) to the Recycle Bin."); FillClips(); }
                    else if (err != null) Status("Space limit: " + err);
                }));
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
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
        // ---- v1.10: webcams
        List<string> webcams; ContentControl webcamRow; Action FillWebcamRow;
        void FindWebcams()
        {
            var ff = S.Ffmpeg; if (ff == null) return;
            new Thread(() => { var l = Recorder.Webcams(ff); Dispatcher.BeginInvoke(new Action(() => { webcams = l; FillWebcamRow(); })); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }

        // ---- v1.8: updates from GitHub (asks first; FFmpeg is never part of an update)
        Button updateBtn; GhRelease pending;
        void CheckForUpdate(bool manual)
        {
            if (Snap.On) return;
            if (manual) Status("Checking GitHub for a newer Hallow Clips...");
            new Thread(() =>
            {
                var r = Updater.Latest();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    S.LastUpdateCheck = DateTime.Now.ToString("o"); S.Save();
                    if (r == null) { if (manual) Status("Couldn't reach GitHub - try again later."); return; }
                    if (!Updater.IsNewer(r)) { if (manual) Status("You have the newest version (v" + Program.Version + ")."); return; }
                    pending = r; updateBtn.Content = "Update to v" + r.Version.ToString(2); updateBtn.Visibility = Visibility.Visible;
                    updateBtn.ToolTip = (r.Notes ?? "").Length > 600 ? r.Notes.Substring(0, 600) + "..." : r.Notes;
                    Status("Hallow Clips v" + r.Version.ToString(2) + " is out - click Update at the top (your clips and settings are kept).");
                }));
            }) { IsBackground = true }.Start();
        }
        void InstallUpdate()
        {
            if (pending == null) return;
            if (session != null || finishing) { Status("Stop the session recording first, then update."); return; }
            if (MessageBox.Show(this, "Install Hallow Clips v" + pending.Version.ToString(2) + " now?\n\nIt downloads from GitHub, checks the file, and reopens by itself. Your clips, settings and FFmpeg are kept.", "Update Hallow Clips", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            updateBtn.IsEnabled = false; updateBtn.Content = "Updating..."; var r = pending; S.Save();
            new Thread(() =>
            {
                var err = Updater.Install(r);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (err != null) { Status(err); updateBtn.IsEnabled = true; updateBtn.Content = "Update to v" + r.Version.ToString(2); }
                    else { rec.Stop(); Status("Installing the update - Hallow Clips reopens in a few seconds."); }   // the installer closes this copy
                }));
            }) { IsBackground = true }.Start();
        }

        // ---- v1.8: screenshot (Ctrl+Alt+P) and markers (Ctrl+Alt+B)
        bool shotKeyOk, markKeyOk, shooting;
        readonly List<DateTime> marks = new List<DateTime>();
        // a short message: on Hallow Overlay's panel when it runs, otherwise our own pop-up
        void Announce(string text)
        {
            new Thread(() =>
            {
                var r = Program.Quiet ? null : Family.Send("Overlay", "flash " + text, 2000);
                if (r == null || !(r == "ok" || r.StartsWith("ok "))) Dispatcher.BeginInvoke(new Action(() => toast.Flash(text)));
            }) { IsBackground = true }.Start();
        }
        void Screenshot()
        {
            var ff = S.Ffmpeg; if (ff == null) { toast.Flash("Hallow Clips isn't set up yet"); return; }
            if (shooting) return; shooting = true;
            var g = Recorder.Safe(GameName); var dir = Path.Combine(S.ClipsFolder, g, "Screenshots"); var screen = S.Screen;
            new Thread(() =>
            {
                string file = null, err = "";
                try
                {
                    Directory.CreateDirectory(dir);
                    file = ClipOps.Free(dir, g + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"), ".png"); int exit;
                    ClipOps.Run(ff, "-hide_banner -loglevel error -y -f lavfi -i ddagrab=output_idx=" + screen + ":framerate=30:draw_mouse=0 -frames:v 1 -vf hwdownload,format=bgra -update 1 \"" + file + "\"", 15000, out err, out exit);
                    if (!File.Exists(file) || new FileInfo(file).Length == 0) { try { File.Delete(file); } catch { } file = null; }
                }
                catch (Exception ex) { err = ex.Message; file = null; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    shooting = false;
                    if (file != null) { Announce("Screenshot saved"); Status("Screenshot saved: " + file); }
                    else { toast.Flash("Couldn't take a screenshot"); Status("Screenshot failed: " + Family.OneLine(err)); }
                }));
            }) { IsBackground = true }.Start();
        }
        void Mark()
        {
            if (!rec.Running && session == null) { toast.Flash(PausedFor != null ? PausedMsg : "Not recording right now"); return; }
            marks.Add(DateTime.Now); marks.RemoveAll(m => m < DateTime.Now.AddHours(-12));
            Announce("Marker added");
        }

        // ---- v1.7: title, thumbnail, GIF, phone, highlight reel
        void TitleClip(string clip)
        {
            if (busy.Contains(clip) || S.Ffmpeg == null) return;
            var w = new PromptWindow("Clip title", "Shown in the list and kept inside the MP4 (players show it too). Leave it empty to remove it.", Mp4.Title(clip), "Save title") { Owner = this }; w.ShowDialog();
            if (w.Value == null) return; var ff = S.Ffmpeg; var v = w.Value.Trim();
            Work(clip, "Saving the title...", () => { string err; return Exports.SetTitle(ff, clip, v, out err) ? "" : "!" + err; }, r => Status(r.StartsWith("!") ? r.Substring(1) : v.Length > 0 ? "Title saved." : "Title removed."));
        }
        void PickThumb(string clip)
        {
            var ff = S.Ffmpeg; if (ff == null || busy.Contains(clip)) return;
            var len = Mp4.Duration(clip); if (len <= 0) { Status("Couldn't read that clip."); return; }
            var w = new ThumbWindow(clip, ff, len) { Owner = this }; w.ShowDialog(); if (w.At < 0) return; var at = w.At;
            Work(clip, "Making the thumbnail...", () => Exports.ThumbAt(ff, clip, at, ThumbPath(clip)) ? "" : "!Couldn't make the thumbnail.", r => Status(r.StartsWith("!") ? r.Substring(1) : "Thumbnail changed."));
        }
        void GifClip(string clip)
        {
            var ff = S.Ffmpeg; if (ff == null || busy.Contains(clip)) return;
            var len = Mp4.Duration(clip); if (len <= 0) { Status("Couldn't read that clip."); return; }
            var w = new GifWindow(clip, ff, len) { Owner = this }; w.ShowDialog(); if (!w.Ok) return;
            double a = w.Start, l = w.Length; int wd = w.Width0;
            Work(clip, "Making the GIF...", () => { string err; var f = Exports.Gif(ff, clip, a, l, wd, out err); return f ?? "!" + err; },
                r => { if (r.StartsWith("!")) Status(r.Substring(1)); else { Status("Saved " + Path.GetFileName(r) + " (" + (new FileInfo(r).Length / 1048576.0).ToString("0.0") + " MB) next to the clip."); try { Process.Start("explorer.exe", "/select,\"" + r + "\""); } catch { } } });
        }
        void PhoneClip(string clip)
        {
            var ff = S.Ffmpeg; if (ff == null || busy.Contains(clip)) return;
            var len = Mp4.Duration(clip); if (len <= 0) { Status("Couldn't read that clip."); return; }
            var w = new PhoneWindow(clip, ff, len) { Owner = this }; w.ShowDialog(); if (!w.Ok) return; var pos = w.Pos;
            Work(clip, "Making the phone version...", () =>
            {
                var info = ClipOps.Inspect(ff, clip); string err;
                var f = Exports.Phone(ff, clip, info, pos, m => Dispatcher.BeginInvoke(new Action(() => Status(m))), out err); return f ?? "!" + err;
            }, r => Status(r.StartsWith("!") ? r.Substring(1) : "Saved " + Path.GetFileName(r) + " (" + (new FileInfo(r).Length / 1048576.0).ToString("0.0") + " MB)."));
        }
        bool reeling;
        void MakeReel()
        {
            var ff = S.Ffmpeg; if (ff == null) { Status("Set up FFmpeg first."); return; }
            if (reeling) { Status("A highlight reel is already being made."); return; }
            var all = RecentClips(40); if (all.Count == 0) { Status("No clips yet."); return; }
            var w = new ReelWindow(all, ReelName, DurationOf) { Owner = this }; w.ShowDialog(); if (!w.Ok) return;
            var clips = w.Picked; var fade = w.Crossfade; var title = w.TitleText; var folder = S.ClipsFolder; var q = S.Quality == "max" ? 18 : S.Quality == "medium" ? 28 : 23;
            reeling = true; Status("Making the highlight reel...");
            new Thread(() =>
            {
                string err; string f = null;
                try { var infos = clips.Select(c => ClipOps.Inspect(ff, c)).ToList(); f = Exports.Reel(ff, folder, clips, infos, fade, title, q, m => Dispatcher.BeginInvoke(new Action(() => Status(m))), out err); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    reeling = false;
                    if (f == null) { Status(err); return; }
                    Status("Saved " + Path.GetFileName(f) + " in the Highlights folder."); toast.Flash("Highlight reel ready"); FillClips(); EnforceLimit();
                }));
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }
        string ReelName(FileInfo f) { var t = TitleOf(f); return t ?? f.Directory.Name + (Regex.IsMatch(Path.GetFileNameWithoutExtension(f.Name), @"\d{4}-\d\d-\d\d \d\d-\d\d-\d\d") ? "" : " - " + Path.GetFileNameWithoutExtension(f.Name)); }

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
            Action show = () => { try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache; bi.UriSource = new Uri(jpg); bi.DecodePixelWidth = 256; bi.EndInit(); bi.Freeze(); target.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill }; } catch { } };
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
            tray = new WF.NotifyIcon { Text = "Hallow Clips", Visible = !Program.Quiet };
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            var menu = new WF.ContextMenuStrip();
            var open = new WF.ToolStripMenuItem("Open Hallow Clips"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold); open.Click += (s, e) => ShowFromTray(); menu.Items.Add(open);
            var save = new WF.ToolStripMenuItem("Save a clip   (Ctrl+Alt+S)"); save.Click += (s, e) => Dispatcher.BeginInvoke(new Action(() => SaveClip(null))); menu.Items.Add(save);
            traySession = new WF.ToolStripMenuItem("Record session   (Ctrl+Alt+R)"); traySession.Click += (s, e) => Dispatcher.BeginInvoke(new Action(ToggleSession)); menu.Items.Add(traySession);
            trayAnyway = new WF.ToolStripMenuItem("Record anyway this session") { Visible = false, ToolTipText = "Discord Clips is recording this game - record with Hallow Clips too, until the game closes" };   // v1.11
            trayAnyway.Click += (s, e) => Dispatcher.BeginInvoke(new Action(() => { if (DiscordChoice) SetRecordAnyway(!recordAnyway); })); menu.Items.Add(trayAnyway);
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowFromTray(); };
        }
        public void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); FillClips(); UpdateState(); }
        void Quit()
        {
            exiting = true; rec.Stop();
            if (session != null) session.Stop();   // a session in progress is kept as .mkv and becomes an MP4 on the next start
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Application.Current.Shutdown();
        }

        public void SnapAll(string dir)
        {
            showSettings = true; settingsPanel.Visibility = Visibility.Visible;
            Snap.Save(this, System.IO.Path.Combine(dir, "main.png"), 820, 1080);
            showSettings = false; settingsPanel.Visibility = Visibility.Collapsed;
            Snap.Save(this, System.IO.Path.Combine(dir, "main-folded.png"), 820, 900);
            Snap.Save(new WhatsNewWindow(WhatsNew.Notes.ToList()), System.IO.Path.Combine(dir, "whatsnew.png"), 560, 0);
            Snap.Save(new CrashNotice(System.IO.Path.Combine(Crash.Dir, "2026-11-19 20-00-00.txt")), System.IO.Path.Combine(dir, "crash.png"), 520, 0);
            var clip = RecentClips(1).FirstOrDefault();
            if (clip != null)
            {
                Snap.Save(new RenameWindow(clip.FullName), System.IO.Path.Combine(dir, "rename.png"), 480, 0);
                if (S.Ffmpeg != null) Snap.Save(new TrimWindow(clip.FullName, S.Ffmpeg, ClipOps.Inspect(S.Ffmpeg, clip.FullName)), System.IO.Path.Combine(dir, "trim.png"), 580, 0);
                Snap.Save(new PromptWindow("Clip title", "Shown in the list and kept inside the MP4 (players show it too). Leave it empty to remove it.", "Big play", "Save title"), System.IO.Path.Combine(dir, "title.png"), 480, 0);
                Snap.Save(new ThumbWindow(clip.FullName, S.Ffmpeg, 8), System.IO.Path.Combine(dir, "thumb.png"), 560, 0);
                Snap.Save(new GifWindow(clip.FullName, S.Ffmpeg, 8), System.IO.Path.Combine(dir, "gif.png"), 560, 0);
                Snap.Save(new PhoneWindow(clip.FullName, S.Ffmpeg, 8), System.IO.Path.Combine(dir, "phone.png"), 560, 0);
                var rw = new ReelWindow(RecentClips(40), ReelName, DurationOf); if (RecentClips(40).Count > 1) { rw.Check(0); rw.Check(1); }
                Snap.Save(rw, System.IO.Path.Combine(dir, "reel.png"), 620, 0);
            }
        }
    }
}
