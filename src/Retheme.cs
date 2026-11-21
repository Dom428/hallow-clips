// Hallow Clips v1.15 - windows that are already open (editor, trim, dialogs, What's new) follow a light / dark or accent change live:
// their styles are swapped for the new theme's ones (slider knobs, buttons, chips, switches) and the colours set from code are mapped
// from the old look to the new one. The main window rebuilds itself instead.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HallowClips
{
    public static class Retheme
    {
        // one look: light or dark, and the accent as text and as a fill
        public struct State { public bool Light; public string Text, Fill; }
        public static State Now() { return new State { Light = Look.IsLight, Text = Accent.TextHex.ToUpperInvariant(), Fill = Accent.FillHex.ToUpperInvariant() }; }

        // light colours that several dark ones share go back to the one code uses
        static readonly Dictionary<string, string> Prefer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { Look.LPanel, "#211B2A" }, { Look.LPanel2, "#2E2639" }, { "#A39CB2", "#5A5268" }, { Look.LRed, "#E0424C" }, { Look.LAmber, "#E0A030" },
        };
        // old colour (#RRGGBB) -> new colour, for everything Clips sets from code
        public static Dictionary<string, string> ColorMap(State from, State to)
        {
            var toDark = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // a colour of the old look -> its dark twin
            if (from.Light)
            {
                foreach (var kv in Pal.Map) if (!toDark.ContainsKey(kv.Value)) toDark[kv.Value] = kv.Key;
                foreach (var kv in Prefer) toDark[kv.Key] = kv.Value;
            }
            else foreach (var k in Pal.Map.Keys) toDark[k] = k;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in toDark)
            {
                string v; var dst = to.Light && Pal.Map.TryGetValue(kv.Value, out v) ? v : kv.Value;
                if (!string.Equals(kv.Key, dst, StringComparison.OrdinalIgnoreCase)) map[kv.Key] = dst;
            }
            if (from.Fill != to.Fill) map[from.Fill] = to.Fill;
            if (from.Text != to.Text) map[from.Text] = to.Text;   // the accent as text wins where it equals the fill (dark)
            return map;
        }

        // the style objects of a theme dictionary -> their keys
        public static Dictionary<Style, object> StyleKeys(ResourceDictionary d)
        {
            var r = new Dictionary<Style, object>();
            if (d != null) foreach (var k in d.Keys) { var s = d[k] as Style; if (s != null && !r.ContainsKey(s)) r[s] = k; }
            return r;
        }

        // re-themes one open window in place
        public static void Apply(Window w, Dictionary<Style, object> oldStyles, ResourceDictionary now, Dictionary<string, string> map)
        {
            var seen = new HashSet<DependencyObject>();
            Walk(w, seen, oldStyles, now, map);
        }
        static void Walk(DependencyObject o, HashSet<DependencyObject> seen, Dictionary<Style, object> oldStyles, ResourceDictionary now, Dictionary<string, string> map)
        {
            if (o == null || !seen.Add(o)) return;
            var fe = o as FrameworkElement;
            if (fe != null)
            {
                object key;
                if (fe.Style != null && oldStyles.TryGetValue(fe.Style, out key) && now.Contains(key)) { var ns = now[key] as Style; if (ns != null) fe.Style = ns; }
                if (fe.ContextMenu != null) Walk(fe.ContextMenu, seen, oldStyles, now, map);
            }
            foreach (var dp in Props(o)) Remap(o, dp, map);
            foreach (var c in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>().ToList()) Walk(c, seen, oldStyles, now, map);
            if (o is Visual || o is System.Windows.Media.Media3D.Visual3D)
            {
                int n; try { n = VisualTreeHelper.GetChildrenCount(o); } catch { n = 0; }
                for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(o, i), seen, oldStyles, now, map);
            }
        }
        static IEnumerable<DependencyProperty> Props(DependencyObject o)
        {
            if (o is Control) { yield return Control.BackgroundProperty; yield return Control.ForegroundProperty; yield return Control.BorderBrushProperty; }
            if (o is Border) { yield return Border.BackgroundProperty; yield return Border.BorderBrushProperty; }
            if (o is Panel) yield return Panel.BackgroundProperty;
            if (o is TextBlock) { yield return TextBlock.ForegroundProperty; yield return TextBlock.BackgroundProperty; }
            if (o is Shape) { yield return Shape.FillProperty; yield return Shape.StrokeProperty; }
        }
        // only colours set from code (local values): template and style colours come with the new style
        static void Remap(DependencyObject o, DependencyProperty dp, Dictionary<string, string> map)
        {
            var b = o.ReadLocalValue(dp) as SolidColorBrush; if (b == null) return;
            if (IsSystemBrush(b)) return;   // Brushes.White etc. (marks and boxes on the video picture) stay
            var c = b.Color; if (c.A < 255 && c.R == 255 && c.G == 255 && c.B == 255) return;   // see-through white on the picture (blur box)
            var hex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            string to; if (!map.TryGetValue(hex, out to)) return;
            var n = (Color)ColorConverter.ConvertFromString(to); n.A = c.A;
            var nb = new SolidColorBrush(n) { Opacity = b.Opacity }; nb.Freeze();
            o.SetValue(dp, nb);
        }
        static bool IsSystemBrush(SolidColorBrush b)
        {
            return ReferenceEquals(b, Brushes.White) || ReferenceEquals(b, Brushes.Black) || ReferenceEquals(b, Brushes.Transparent);
        }
    }
}
