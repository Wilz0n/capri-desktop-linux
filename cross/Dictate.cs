using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;

namespace CapriDesktop
{
    internal enum DictationState { Unavailable, Idle, Recording, Transcribing, Error }

    internal static class Dictate
    {
        internal static Func<string> StateReaderOverride;
        internal static string DirectoryPath
        {
            get
            {
                string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                return string.IsNullOrWhiteSpace(runtime) ? null : Path.Combine(runtime, "capri");
            }
        }

        internal static string Inbox { get { return DirectoryPath == null ? null : Path.Combine(DirectoryPath, "voice.txt"); } }
        internal static string StatePath
        {
            get
            {
                string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                return string.IsNullOrWhiteSpace(runtime) ? null : Path.Combine(runtime, "voxtype", "state");
            }
        }

        internal static bool IsAvailable
        {
            get { return FindExecutable("voxtype") != null && StatePath != null && File.Exists(StatePath) && DirectoryPath != null; }
        }

        internal static bool ParseState(string text, out DictationState state)
        {
            string value = (text ?? "").Trim().ToLowerInvariant();
            if (value == "idle") { state = DictationState.Idle; return true; }
            if (value == "recording") { state = DictationState.Recording; return true; }
            if (value == "transcribing") { state = DictationState.Transcribing; return true; }
            state = DictationState.Error;
            return false;
        }

        internal static DictationState ReadState()
        {
            if (StateReaderOverride != null)
            {
                DictationState fake;
                return ParseState(StateReaderOverride(), out fake) ? fake : DictationState.Error;
            }
            if (!IsAvailable) return DictationState.Unavailable;
            try
            {
                DictationState state;
                return ParseState(File.ReadAllText(StatePath), out state) ? state : DictationState.Error;
            }
            catch (IOException) { return DictationState.Unavailable; }
            catch (UnauthorizedAccessException) { return DictationState.Unavailable; }
        }

        internal static bool Start()
        {
            if (!IsAvailable || ReadState() != DictationState.Idle) return false;
            try
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                File.SetUnixFileMode(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                if (File.Exists(Inbox)) File.Delete(Inbox);
                string exe = FindExecutable("voxtype");
                return Run(exe, "record", "start", "--file=" + Inbox, "--no-osd");
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }

        internal static bool Stop()
        {
            string exe = FindExecutable("voxtype");
            return exe != null && Run(exe, "record", "stop");
        }

        internal static string ReadTranscript()
        {
            if (Inbox == null || !File.Exists(Inbox)) return null;
            try
            {
                File.SetUnixFileMode(Inbox, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                string text = File.ReadAllText(Inbox).Trim();
                File.Delete(Inbox);
                return text.Length == 0 ? null : text;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static bool WriteStateForTest(string folder, string text)
        {
            if (folder == null) return false;
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "state"), text ?? "");
                return true;
            }
            catch (IOException) { return false; }
        }

        internal static string ReadInboxForTest(string folder)
        {
            string path = Path.Combine(folder, "voice.txt");
            if (!File.Exists(path)) return null;
            string value = File.ReadAllText(path).Trim();
            File.Delete(path);
            return value.Length == 0 ? null : value;
        }

        internal static void ResetForTest() { StateReaderOverride = null; }

        private static bool Run(string executable, params string[] args)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(executable);
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                foreach (string arg in args) info.ArgumentList.Add(arg);
                using (Process process = Process.Start(info))
                {
                    if (process == null || !process.WaitForExit(2500))
                    {
                        if (process != null) try { process.Kill(); } catch (InvalidOperationException) { }
                        return false;
                    }
                    return process.ExitCode == 0;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static string FindExecutable(string name)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string folder in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                string candidate = Path.Combine(folder, name);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
