using System;
using System.IO;
using System.Text;
using System.Linq;

namespace CapriDesktop
{
    internal static class BrainSecretStore
    {
        private static string Folder
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                return Path.Combine(root, "Capri");
            }
        }

        private static string PathFor(string provider)
        {
            string safe = new string((provider ?? "").ToLowerInvariant().Where(delegate(char ch) { return char.IsLetterOrDigit(ch) || ch == '-'; }).ToArray());
            if (safe.Length == 0) throw new ArgumentException("Provider name is empty.");
            return Path.Combine(Folder, safe + ".key");
        }

        internal static void Save(string provider, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is empty.");
            Directory.CreateDirectory(Folder);
            File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string path = PathFor(provider);
            File.WriteAllText(path, key.Trim(), new UTF8Encoding(false));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        internal static string Load(string provider)
        {
            string path = PathFor(provider);
            if (!File.Exists(path)) return null;
            try { return File.ReadAllText(path, Encoding.UTF8).Trim(); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static void Forget(string provider)
        {
            string path = PathFor(provider);
            if (File.Exists(path)) File.Delete(path);
        }

        internal static bool IsKey(string provider, string key)
        {
            if (provider == "openrouter" || provider == "gemini") return !string.IsNullOrWhiteSpace(key);
            if (provider == "nvidia") return (key ?? "").StartsWith("nvapi-", StringComparison.Ordinal);
            return false;
        }

        internal static string PathForTest(string provider) { return PathFor(provider); }
    }
}
