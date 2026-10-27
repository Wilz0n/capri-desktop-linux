using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System.Linq;

namespace CapriDesktop
{
    internal static class Program
    {
        internal static string Smoke;
        internal static int[] Shelf;
        internal static string PipeName;
        internal static string RescueFile;
        internal static string DictationInbox;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 5 && args[0] == "--shelf")
            {
                Shelf = new[] { int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]) };
                return AppBuilder.Configure<ShelfApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            }
            if (args.Length >= 2 && args[0] == "--smoke") Smoke = Path.GetFullPath(args[1]);
            if (OperatingSystem.IsLinux())
            {
                string signature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE") ?? "default";
                string safe = signature.Replace('/', '-').Replace('\\', '-');
                PipeName = "capri-rescue-" + safe;
                string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
                RescueFile = Path.Combine(runtime, "capri-rescue-" + safe + ".request");
                DictationInbox = Path.Combine(runtime, "capri", "voice.txt");
                if (args.Length >= 2 && args[0] == "--install-voxtype") return InstallVoxtype(args[1]);
                if (args.Length >= 1 && args[0] == "--route-voxtype") return RouteVoxtype(PipeName, DictationInbox);
                if (args.Length >= 1 && args[0] == "--rescue") return Rescue(PipeName, RescueFile);
                if (args.Length >= 2 && args[0] == "--dictation") return DictationCommand(PipeName, args[1]);
            }
            return AppBuilder.Configure<CapriApp>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = false }).StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }

        private static int InstallVoxtype(string path)
        {
            return VoxtypeScript.Install(path) ? 0 : 1;
        }

        private static int Rescue(string pipeName, string rescueFile)
        {
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    pipe.Connect(700);
                    using (StreamWriter writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false), 1024, true))
                    using (StreamReader reader = new StreamReader(pipe, System.Text.Encoding.UTF8, false, 1024, true))
                    {
                        writer.WriteLine("rescue");
                        writer.Flush();
                        return reader.ReadLine() == "rescued" ? 0 : 1;
                    }
                }
            }
            catch (TimeoutException) { return WriteRescueRequest(rescueFile, 2); }
            catch (IOException) { return WriteRescueRequest(rescueFile, 2); }
        }

        private static int WriteRescueRequest(string rescueFile, int failureCode)
        {
            try { File.WriteAllText(rescueFile, "rescue"); return 0; }
            catch (IOException) { return failureCode; }
            catch (UnauthorizedAccessException) { return failureCode; }
        }

        private static int DictationCommand(string pipeName, string action)
        {
            if (action != "prepare" && action != "finish" && action != "cancel") return 4;
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    pipe.Connect(700);
                    using (StreamWriter writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false), 1024, true))
                    using (StreamReader reader = new StreamReader(pipe, System.Text.Encoding.UTF8, false, 1024, true))
                    {
                        writer.WriteLine("dictation:" + action);
                        writer.Flush();
                        string answer = reader.ReadLine();
                        return action == "prepare" && answer == "armed" || action == "finish" && answer == "accepted" || action == "cancel" && answer == "cancelled" ? 0 : 1;
                    }
                }
            }
            catch (TimeoutException) { return 2; }
            catch (IOException) { return 3; }
        }

        private static int RouteVoxtype(string pipeName, string inbox)
        {
            try
            {
                if (!File.Exists(inbox)) return 1;
                File.SetUnixFileMode(inbox, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                string text = File.ReadAllText(inbox).Trim();
                if (string.IsNullOrWhiteSpace(text)) return 1;
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    pipe.Connect(700);
                    using (StreamWriter writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false), 1024, true))
                    using (StreamReader reader = new StreamReader(pipe, System.Text.Encoding.UTF8, false, 1024, true))
                    {
                        writer.WriteLine("dictation:route:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)));
                        writer.Flush();
                        if (reader.ReadLine() != "routed") return 1;
                    }
                }
                File.Delete(inbox);
                return 0;
            }
            catch (TimeoutException) { return 2; }
            catch (IOException) { return 3; }
            catch (UnauthorizedAccessException) { return 4; }
        }

    }

    internal sealed class CapriApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            IClassicDesktopStyleApplicationLifetime desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                Pet pet = new Pet(desktop);
                if (OperatingSystem.IsLinux() && Program.Smoke == null) pet.StartRescueListener();
                NativeMenu menu = new NativeMenu();
                NativeMenuItem talk = new NativeMenuItem("Talk to her");
                talk.Click += delegate { pet.Talk.Open(); };
                NativeMenuItem bring = new NativeMenuItem("Bring her back");
                bring.Click += delegate { pet.Bring(); };
                NativeMenuItem brainSettings = new NativeMenuItem("Brain settings…");
                brainSettings.Click += delegate { pet.ShowBrainPanel(); };
                NativeMenuItem quit = new NativeMenuItem("Quit Capri");
                quit.Click += delegate { pet.Quit(); };
                menu.Items.Add(talk);
                menu.Items.Add(bring);
                menu.Items.Add(brainSettings);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(quit);
                TrayIcon tray = new TrayIcon();
                using (Stream chip = typeof(CapriApp).Assembly.GetManifestResourceStream("Capri.Chip.png")) tray.Icon = new WindowIcon(new Bitmap(chip));
                tray.ToolTipText = "Capri";
                tray.Menu = menu;
                TrayIcons icons = new TrayIcons();
                icons.Add(tray);
                TrayIcon.SetIcons(this, icons);
                pet.Show();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }

    internal sealed class ShelfApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Light;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            IClassicDesktopStyleApplicationLifetime desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                Window shelf = new Window
                {
                    Title = "Capri test shelf",
                    Width = Program.Shelf[2],
                    Height = Program.Shelf[3],
                    CanResize = false,
                    Topmost = true,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = new PixelPoint(Program.Shelf[0], Program.Shelf[1]),
                    Background = new SolidColorBrush(Color.FromRgb(238, 240, 243)),
                    Content = new TextBlock { Text = "Capri test shelf", Margin = new Thickness(16), Foreground = Brushes.Black }
                };
                shelf.Show();
                Thread reader = new Thread(delegate()
                {
                    string line;
                    while ((line = Console.In.ReadLine()) != null)
                    {
                        string[] parts = line.Trim().Split(' ');
                        Dispatcher.UIThread.Post(delegate
                        {
                            if (parts[0] == "move" && parts.Length == 3) shelf.Position = new PixelPoint(int.Parse(parts[1]), int.Parse(parts[2]));
                            else if (parts[0] == "hide") shelf.Hide();
                            else if (parts[0] == "show") shelf.Show();
                            else if (parts[0] == "quit") desktop.Shutdown(0);
                        });
                    }
                    Dispatcher.UIThread.Post(delegate { desktop.Shutdown(0); });
                });
                reader.IsBackground = true;
                reader.Start();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
