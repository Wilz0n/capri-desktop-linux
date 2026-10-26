using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    internal sealed class BrowserChoice
    {
        internal string Id;
        internal string Name;
        internal string Exec;
        internal string File;
    }

    internal static class LinuxBrowsers
    {
        private static DateTime loadedAt;
        private static List<BrowserChoice> loaded;
        private static Func<string> readSettings = delegate { return Read("xdg-settings", "get", "default-web-browser"); };
        private static Func<string> readMime = delegate { return Read("xdg-mime", "query", "default", "x-scheme-handler/https"); };

        internal static List<BrowserChoice> List()
        {
            if (loaded != null && DateTime.UtcNow - loadedAt < TimeSpan.FromMinutes(2)) return new List<BrowserChoice>(loaded);
            Dictionary<string, BrowserChoice> choices = new Dictionary<string, BrowserChoice>(StringComparer.OrdinalIgnoreCase);
            List<string> roots = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications"),
                "/var/lib/flatpak/exports/share/applications",
                "/usr/share/applications"
            };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string[] files;
                try { files = Directory.GetFiles(root, "*.desktop", SearchOption.AllDirectories); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (string file in files)
                {
                    try
                    {
                        DesktopEntry entry = LinuxHands.ParseDesktop(File.ReadAllText(file));
                        if (entry.Type != "Application" || entry.NoDisplay || entry.Hidden || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Exec) || string.IsNullOrEmpty(entry.Categories) || !HasBrowserCategory(entry.Categories)) continue;
                        string id = Path.GetFileName(file);
                        if (!choices.ContainsKey(id) || !file.StartsWith("/usr/share/applications/", StringComparison.Ordinal))
                        {
                            if (!choices.ContainsKey(id) || file.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications"), StringComparison.Ordinal))
                                choices[id] = new BrowserChoice { Id = id, Name = entry.Name, Exec = entry.Exec, File = file };
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            loaded = new List<BrowserChoice>(choices.Values);
            loaded.Sort(delegate(BrowserChoice a, BrowserChoice b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            loadedAt = DateTime.UtcNow;
            return new List<BrowserChoice>(loaded);
        }

        private static bool HasBrowserCategory(string categories)
        {
            foreach (string category in (categories ?? "").Split(';')) if (category == "WebBrowser") return true;
            return false;
        }

        internal static string DefaultDesktop(string settings, string mime)
        {
            string selected = (settings ?? "").Trim();
            if (selected.Length == 0) selected = (mime ?? "").Trim();
            if (selected.Length == 0) return null;
            return Path.GetFileName(selected);
        }

        internal static BrowserChoice Selected(string saved, string settings, string mime)
        {
            string id = string.IsNullOrEmpty(saved) ? DefaultDesktop(settings, mime) : saved;
            if (id == null) return null;
            foreach (BrowserChoice browser in List()) if (string.Equals(browser.Id, id, StringComparison.OrdinalIgnoreCase)) return browser;
            return null;
        }

        internal static BrowserChoice Selected(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (BrowserChoice browser in List()) if (string.Equals(browser.Id, id, StringComparison.OrdinalIgnoreCase)) return browser;
            return null;
        }

        internal static string AutomaticLabel()
        {
            BrowserChoice choice = Selected(null, readSettings(), readMime());
            return choice == null ? "system default" : choice.Name;
        }

        internal static List<string> ExecArgs(string exec, string url) { return LinuxHands.BrowserArgs(exec, url); }

        internal static void SetDefaultReaders(Func<string> settings, Func<string> mime)
        {
            readSettings = settings ?? delegate { return ""; };
            readMime = mime ?? delegate { return ""; };
        }

        internal static void ResetForTest()
        {
            loaded = null;
            loadedAt = DateTime.MinValue;
            readSettings = delegate { return ""; };
            readMime = delegate { return ""; };
        }

        internal static bool BrowserExecHasUrlField(string exec)
        {
            return exec != null && (exec.Contains("%u") || exec.Contains("%U") || exec.Contains("%f") || exec.Contains("%F"));
        }

        private static string Read(string command, params string[] args)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(command);
                start.UseShellExecute = false;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                foreach (string arg in args) start.ArgumentList.Add(arg);
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(start))
                {
                    if (process == null || !process.WaitForExit(3000) || process.ExitCode != 0) return "";
                    return process.StandardOutput.ReadToEnd().Trim();
                }
            }
            catch (System.ComponentModel.Win32Exception) { return ""; }
            catch (InvalidOperationException) { return ""; }
        }
    }
}
