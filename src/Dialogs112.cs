// Hallow Clips - v1.12 windows: asking before several clips go to the Recycle Bin.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowClips
{
    public class ConfirmDeleteWindow : Dialog
    {
        public bool Ok;
        public ConfirmDeleteWindow(List<FileInfo> clips, Func<FileInfo, string> name, Func<string, bool> favorite) : base("Move to the Recycle Bin", 520)
        {
            int n = clips.Count; long bytes = clips.Sum(f => { try { return f.Length; } catch { return 0L; } });
            int favs = clips.Count(f => favorite != null && favorite(f.FullName));
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(Heading("Move " + (n == 1 ? "this clip" : n + " clips") + " to the Recycle Bin?"));
            sp.Children.Add(new TextBlock { Text = ClipIndex.Size(bytes) + (favs > 0 ? "   \u00B7   " + (favs == 1 ? "1 of them is a favourite" : favs + " of them are favourites") : ""), FontWeight = FontWeights.SemiBold, Foreground = favs > 0 ? B("#FFB060") : (Brush)FindResource("Text"), Margin = new Thickness(0, 0, 0, 10) });
            var list = new StackPanel();
            const int max = 8;
            foreach (var f in clips.Take(max))
            {
                var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var when = new TextBlock { Text = f.LastWriteTime.ToString("d MMM HH:mm") + "   " + ClipIndex.Size(f.Length), Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(when, Dock.Right); d.Children.Add(when);
                var nm = new StackPanel { Orientation = Orientation.Horizontal };
                if (favorite != null && favorite(f.FullName)) { var st = Icons.Make("star", 11, Accent.Brush); st.Margin = new Thickness(0, 0, 6, 0); st.VerticalAlignment = VerticalAlignment.Center; nm.Children.Add(st); }
                nm.Children.Add(new TextBlock { Text = (f.Directory != null ? f.Directory.Name + "   \u00B7   " : "") + name(f), TextTrimming = TextTrimming.CharacterEllipsis });
                d.Children.Add(nm); list.Children.Add(d);
            }
            if (n > max) list.Children.Add(new TextBlock { Text = "and " + (n - max) + " more", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
            sp.Children.Add(new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Child = list });
            sp.Children.Add(new TextBlock { Text = "You can restore them from the Recycle Bin. Copies made from them (trimmed copies, highlight reels, Discord copies) stay where they are.", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            sp.Children.Add(Foot("Move to Recycle Bin", () => { Ok = true; Close(); }));
            Content = sp;
        }
    }
}
