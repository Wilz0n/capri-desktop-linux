using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CapriDesktop
{
    internal static class BrainErrorLog
    {
        private static string PathName
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                return Path.Combine(root, "Capri", "brain-errors.log");
            }
        }

        internal static void Write(string message)
        {
            try
            {
                string path = PathName;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string safe = (message ?? "").Replace('\r', ' ').Replace('\n', ' ');
                safe = System.Text.RegularExpressions.Regex.Replace(safe, @"(?i)(AIza[0-9A-Za-z_-]{20,}|AQ\.[A-Za-z0-9._-]{20,}|sk-or-v1-[A-Za-z0-9]+|Bearer\s+\S+)", "[redacted]");
                File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + safe + Environment.NewLine, new UTF8Encoding(false));
                string[] lines = File.ReadAllLines(path);
                if (lines.Length > 50) File.WriteAllLines(path, lines.Skip(lines.Length - 50), new UTF8Encoding(false));
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception) { }
        }

        internal static string ReadForTest()
        {
            string path = PathName;
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
    }
}
