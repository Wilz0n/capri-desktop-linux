using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class DesktopEntry
    {
        internal string Name;
        internal string Exec;
        internal string Type;
        internal string Categories;
        internal bool NoDisplay;
        internal bool Hidden;
    }

    internal static class LinuxHands
    {
        private sealed class Candidate
        {
            internal string Name;
            internal string Id;
            internal string Exec;
            internal int Priority;
            internal bool Launchable;
        }

        private static readonly object gate = new object();
        private static List<Candidate> catalog;
        private static DateTime catalogAt;
        private static readonly Regex Scheme = new Regex(@"\A[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase);
        private static Func<string> browserSetting = delegate { return Output("xdg-settings", "get", "default-web-browser"); };
        private static Func<string> browserMime = delegate { return Output("xdg-mime", "query", "default", "x-scheme-handler/https"); };

        internal static void Install()
        {
            if (!OperatingSystem.IsLinux()) return;
            Hands.Catalog = Catalog;
            Hands.Run = delegate(string id)
            {
                if (!string.IsNullOrWhiteSpace(id) && (id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    BrowserChoice browser = LinuxBrowsers.Selected(null, browserSetting(), browserMime());
                    if (browser != null && RunExec(browser.Exec, id)) return;
                }
                Run(id);
            };
            Hands.Press = Press;
            Hands.NowPlaying = NowPlaying;
            Hands.PressPlay = PressPlay;
            Hands.Windows = delegate { return new List<OpenWindow>(); };
            Hands.Steer = delegate { return false; };
        }

        internal static DesktopEntry ParseDesktop(string text)
        {
            DesktopEntry entry = new DesktopEntry();
            bool inDesktop = false;
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r').Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    inDesktop = line == "[Desktop Entry]";
                    continue;
                }
                if (!inDesktop || line.Length == 0 || line[0] == '#') continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals);
                string value = line.Substring(equals + 1);
                if (key == "Name") entry.Name = value;
                else if (key == "Exec") entry.Exec = value;
                else if (key == "Type") entry.Type = value;
                else if (key == "Categories") entry.Categories = value;
                else if (key == "NoDisplay") entry.NoDisplay = IsTrue(value);
                else if (key == "Hidden") entry.Hidden = IsTrue(value);
            }
            return entry;
        }

        private static bool IsTrue(string value) { return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase); }

        private static List<Candidate> LoadCatalog()
        {
            Dictionary<string, Candidate> byFile = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
            List<string[]> roots = new List<string[]>
            {
                new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications"), "3" },
                new[] { "/var/lib/flatpak/exports/share/applications", "2" },
                new[] { "/usr/share/applications", "1" }
            };
            foreach (string[] root in roots)
            {
                if (!Directory.Exists(root[0])) continue;
                int priority = int.Parse(root[1], CultureInfo.InvariantCulture);
                string[] files;
                try { files = Directory.GetFiles(root[0], "*.desktop", SearchOption.AllDirectories); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (string file in files)
                {
                    string relative = file.Substring(root[0].Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    Candidate previous;
                    if (byFile.TryGetValue(relative, out previous) && previous.Priority >= priority) continue;
                    try
                    {
                        DesktopEntry entry = ParseDesktop(File.ReadAllText(file));
                        if (entry.Type != "Application" || entry.NoDisplay || entry.Hidden || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Exec))
                        {
                            byFile.Remove(relative);
                            continue;
                        }
                        byFile[relative] = new Candidate { Name = entry.Name, Id = file, Exec = entry.Exec, Priority = priority, Launchable = true };
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            List<Candidate> found = new List<Candidate>(byFile.Values);
            found.Sort(delegate(Candidate a, Candidate b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return found;
        }

        private static List<Candidate> Apps()
        {
            lock (gate)
            {
                if (catalog == null || DateTime.UtcNow - catalogAt > TimeSpan.FromMinutes(2))
                {
                    catalog = LoadCatalog();
                    catalogAt = DateTime.UtcNow;
                }
                return catalog;
            }
        }

        private static List<InstalledApp> Catalog()
        {
            List<InstalledApp> found = new List<InstalledApp>();
            foreach (Candidate app in Apps()) found.Add(new InstalledApp(app.Name, app.Launchable ? "capri-desktop:" + app.Id : app.Id));
            return found;
        }

        private static Candidate Find(string path)
        {
            foreach (Candidate app in Apps()) if (string.Equals(app.Id, path, StringComparison.Ordinal)) return app;
            return null;
        }

        private static ProcessStartInfo StartInfo(string command, IEnumerable<string> args)
        {
            ProcessStartInfo info = new ProcessStartInfo(command);
            info.UseShellExecute = false;
            foreach (string arg in args) info.ArgumentList.Add(arg);
            return info;
        }

        private static string Output(string command, params string[] args)
        {
            try
            {
                ProcessStartInfo info = StartInfo(command, args);
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    if (process == null) return "";
                    if (!process.WaitForExit(3000))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        return "";
                    }
                    return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : "";
                }
            }
            catch (System.ComponentModel.Win32Exception) { return ""; }
            catch (InvalidOperationException) { return ""; }
        }

        private static bool RunWait(string command, params string[] args)
        {
            try
            {
                using (Process process = Process.Start(StartInfo(command, args)))
                {
                    if (process == null) return false;
                    if (!process.WaitForExit(4000))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        return false;
                    }
                    return process.ExitCode == 0;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static bool RunDetached(string command, params string[] args)
        {
            try
            {
                ProcessStartInfo info = StartInfo(command, args);
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                Process process = Process.Start(info);
                if (process == null) return false;
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    using (Process child = (Process)state)
                    {
                        try
                        {
                            if (!child.WaitForExit(10000)) child.Kill();
                        }
                        catch (InvalidOperationException) { }
                        finally { child.Dispose(); }
                    }
                }, process);
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static void Run(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            const string Launcher = "capri-desktop:";
            const string UrlLauncher = "capri-url:";
            if (id.StartsWith(UrlLauncher, StringComparison.Ordinal))
            {
                string payload = id.Substring(UrlLauncher.Length);
                int colon = payload.IndexOf(':');
                if (colon <= 0) { RunDetached("xdg-open", payload); return; }
                string browserId = payload.Substring(0, colon);
                string url = payload.Substring(colon + 1);
                BrowserChoice browser = LinuxBrowsers.Selected(browserId);
                if (browser == null || !RunExec(browser.Exec, url)) RunDetached("xdg-open", url);
                return;
            }
            Candidate app = id.StartsWith(Launcher, StringComparison.Ordinal) ? Find(id.Substring(Launcher.Length)) : null;
            if (app != null)
            {
                RunExec(app.Exec, null);
                return;
            }
            if (id.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase) && Output("playerctl", "-p", "spotify", "status").Length > 0)
            {
                if (RunWait("playerctl", "-p", "spotify", "open", id)) return;
            }
            if (id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || Scheme.IsMatch(id))
                RunDetached("xdg-open", id);
        }

        internal static List<string> ExecArguments(string exec, string url = null)
        {
            List<string> args = new List<string>();
            StringBuilder word = new StringBuilder();
            bool quoted = false;
            bool escaped = false;
            for (int i = 0; i < exec.Length; i++)
            {
                char ch = exec[i];
                if (escaped) { word.Append(ch); escaped = false; continue; }
                if (ch == '\\') { escaped = true; continue; }
                if (ch == '"') { quoted = !quoted; continue; }
                if (char.IsWhiteSpace(ch) && !quoted)
                {
                    AddExecWord(args, word);
                    continue;
                }
                if (ch == '%' && i + 1 < exec.Length)
                {
                    char code = exec[i + 1];
                    i++;
                    if (code == '%') word.Append('%');
                    else if (code == 'u' || code == 'U' || code == 'f' || code == 'F')
                    {
                        if (!string.IsNullOrEmpty(url)) word.Append(url);
                        else
                        {
                            word.Length = 0;
                            while (i + 1 < exec.Length && !char.IsWhiteSpace(exec[i + 1])) i++;
                        }
                    }
                    else if (code == 'c' || code == 'k') { }
                    else if (code == 'i') AddExecWord(args, word);
                    continue;
                }
                word.Append(ch);
            }
            if (escaped) word.Append('\\');
            AddExecWord(args, word);
            return args;
        }

        private static bool RunExec(string exec, string url)
        {
            List<string> args = ExecArguments(exec, url);
            if (args.Count == 0) return false;
            string command = args[0];
            args.RemoveAt(0);
            return RunDetached(command, args.ToArray());
        }

        private static void AddExecWord(List<string> args, StringBuilder word)
        {
            if (word.Length == 0) return;
            args.Add(word.ToString());
            word.Length = 0;
        }

        internal static string[] MediaCommand(byte key)
        {
            if (key == 0xB3) return new[] { "playerctl", "-p", "spotify", "play-pause" };
            if (key == 0xB0) return new[] { "playerctl", "-p", "spotify", "next" };
            if (key == 0xB1) return new[] { "playerctl", "-p", "spotify", "previous" };
            if (key == 0xAF) return new[] { "wpctl", "set-volume", "-l", "1.0", "@DEFAULT_AUDIO_SINK@", "2%+" };
            if (key == 0xAE) return new[] { "wpctl", "set-volume", "-l", "1.0", "@DEFAULT_AUDIO_SINK@", "2%-" };
            if (key == 0xAD) return new[] { "wpctl", "set-mute", "@DEFAULT_AUDIO_SINK@", "toggle" };
            return null;
        }

        private static void Press(byte key)
        {
            string[] command = MediaCommand(key);
            if (command == null) return;
            string[] args = new string[command.Length - 1];
            Array.Copy(command, 1, args, 0, args.Length);
            if (command[0] == "playerctl")
            {
                List<string> playing = PlayingPlayers();
                if (MediaArbiter.ShouldPause(playing, "")) return;
                string player = MediaArbiter.ActivePlayer() ?? "spotify";
                if (player == "ambiguous") return;
                if (args.Length > 1) args[1] = player;
            }
            RunWait(command[0], args);
        }

        internal static void Pause(string player) { RunWait("playerctl", "-p", player, "pause"); }
        internal static void Next(string player) { RunWait("playerctl", "-p", player, "next"); }
        internal static void Previous(string player) { RunWait("playerctl", "-p", player, "previous"); }

        internal static List<string> Players()
        {
            List<string> found = new List<string>();
            foreach (string player in Output("playerctl", "-l").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) found.Add(player.Trim());
            return found;
        }

        internal static List<string> PlayingPlayers()
        {
            List<string> found = new List<string>();
            foreach (string player in Players()) if (Output("playerctl", "-p", player, "status") == "Playing") found.Add(player);
            return found;
        }

        internal static string PlayerName(string player)
        {
            if (player == null) return "";
            Match instance = Regex.Match(player, @"(?:chromium|chrome)\.instance(\d+)", RegexOptions.IgnoreCase);
            if (instance.Success)
            {
                try
                {
                    string command = File.ReadAllText("/proc/" + instance.Groups[1].Value + "/comm").Trim();
                    if (command == "chrome") return "Chrome";
                    return command.Length == 0 ? "Chrome" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(command);
                }
                catch (IOException) { return "Chrome"; }
                catch (UnauthorizedAccessException) { return "Chrome"; }
            }
            string display = player;
            int dot = display.IndexOf('.');
            if (dot >= 0) display = display.Substring(0, dot);
            if (display.Equals("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
            if (display.Equals("chromium", StringComparison.OrdinalIgnoreCase)) return "Chrome";
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(display.Replace('-', ' '));
        }

        internal static string PlayerTrack(string player) { return Output("playerctl", "-p", player, "metadata", "--format", "{{title}}"); }

        internal static void SetBrowserLookup(Func<string> settings, Func<string> mime)
        {
            browserSetting = settings ?? delegate { return ""; };
            browserMime = mime ?? delegate { return ""; };
        }

        internal static List<string> BrowserArgs(string exec, string url) { return ExecArguments(exec, url); }

        private static string NowPlaying()
        {
            string status = Output("playerctl", "-p", "spotify", "status");
            if (status == "Playing")
            {
                string metadata = Output("playerctl", "-p", "spotify", "metadata", "--format", "{{artist}} - {{title}}");
                return metadata.Length > 0 ? metadata : "Spotify";
            }
            if (status == "Paused" || status == "Stopped") return "Spotify";
            return "";
        }

        internal static string PlayingStatus(string player) { return Output("playerctl", "-p", player, "status"); }

        private static bool PressPlay(string name)
        {
            for (int i = 0; i < 24; i++)
            {
                if (Output("playerctl", "-p", "spotify", "status").Length > 0)
                {
                    if (!RunWait("playerctl", "-p", "spotify", "play")) return false;
                    return Output("playerctl", "-p", "spotify", "status") == "Playing";
                }
                Thread.Sleep(500);
            }
            return false;
        }
    }
}
