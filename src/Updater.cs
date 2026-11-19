// Hallow Clips - updates (v1.8, the same way Hallow Hub does it) from GitHub releases (asks first, checks the download's SHA-256 before installing).
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace HallowClips
{
    [DataContract] public class GhAsset
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "browser_download_url")] public string Url;
        [DataMember(Name = "digest")] public string Digest;   // "sha256:..."
    }
    [DataContract] public class GhRelease
    {
        [DataMember(Name = "tag_name")] public string Tag;
        [DataMember(Name = "body")] public string Notes;
        [DataMember(Name = "assets")] public GhAsset[] Assets;
        public Version Version { get { Version v; return Version.TryParse((Tag ?? "").TrimStart('v', 'V'), out v) ? v : new Version(0, 0); } }
        public GhAsset Zip { get { return Assets == null ? null : Assets.FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)); } }
    }

    public static class Updater
    {
        public const string Repo = "Dom428/hallow-clips";
        public static Version Current { get { return typeof(Updater).Assembly.GetName().Version; } }   // 1.10 > 1.9: Version compares numbers

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "HallowClips";
            return wc;
        }

        // Newest release on GitHub, or null if it can't be reached.
        public static GhRelease Latest()
        {
            try
            {
                using (var wc = Client())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    var json = wc.DownloadData("https://api.github.com/repos/" + Repo + "/releases/latest");
                    using (var ms = new MemoryStream(json)) return (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(ms);
                }
            }
            catch { return null; }
        }
        public static bool IsNewer(GhRelease r) { return r != null && r.Zip != null && r.Version > new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build)); }

        // Downloads the release zip, checks its SHA-256 against GitHub's, unpacks it and runs its installer
        // (which closes this copy, installs the new one and opens it). Returns an error message, or null when it started.
        // Only the app's own files are in the zip; FFmpeg (in %LOCALAPPDATA%\HallowClips\ffmpeg) is never downloaded or touched.
        public static string Install(GhRelease r)
        {
            string dir; var err = Fetch(r, out dir); if (err != null) return err;
            var script = Directory.GetFiles(dir, "Install.ps1", SearchOption.AllDirectories).FirstOrDefault();
            if (script == null) return "The update is missing its installer.";
            try { Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true }); return null; }
            catch (Exception e) { return "Update failed: " + e.Message; }
        }
        // downloads, checks the SHA-256 against GitHub's and unpacks; files = the unpacked folder
        public static string Fetch(GhRelease r, out string files)
        {
            files = null;
            var a = r.Zip; if (a == null) return "That release has no zip.";
            if (string.IsNullOrEmpty(a.Digest) || !a.Digest.StartsWith("sha256:")) return "GitHub didn't give a checksum for the download, so it wasn't installed.";
            var dir = Path.Combine(Path.GetTempPath(), "HallowClipsUpdate");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var zip = Path.Combine(dir, "update.zip");
                using (var wc = Client()) wc.DownloadFile(a.Url, zip);
                string hash;
                using (var f = File.OpenRead(zip)) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                if (hash != a.Digest.Substring(7).ToLowerInvariant()) return "The download didn't match GitHub's checksum, so it wasn't installed.";
                var x = Path.Combine(dir, "files");
                ZipFile.ExtractToDirectory(zip, x);
                if (Directory.GetFiles(x, "ff*.exe", SearchOption.AllDirectories).Any()) return "The update contains files it shouldn't, so it wasn't installed.";   // never FFmpeg
                files = x; return null;
            }
            catch (Exception e) { return "Update failed: " + e.Message; }
        }
    }
}
