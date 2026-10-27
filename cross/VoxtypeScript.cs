using System;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    internal static class VoxtypeScript
    {
        internal static string Build()
        {
            return """
                #!/usr/bin/env bash
                set -e
                action="${1:-}"
                if [[ "$action" != "toggle" ]]; then exit 2; fi
                exec voxtype record toggle
                """;
        }

        internal static bool Install(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) return false;
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, Build(), new UTF8Encoding(false));
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
