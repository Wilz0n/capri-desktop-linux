using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CapriDesktop
{
    internal sealed class Smoke
    {
        private readonly Pet pet;
        private readonly string folder;
        private readonly List<string> checks = new List<string>();
        private readonly List<Process> shelves = new List<Process>();
        private int step;
        private double started = -1;
        private double stepStart = -1;
        private double settled = -1;
        private double coverAt = -1;
        private double hideAt = -1;
        private bool finished;
        private Process shelf;
        private Process cover;
        private PixelRect shelfAsked;
        private PixelRect coverAsked;
        private Spot shelfSpot;
        private PixelPoint hipBefore;
        private int dx;
        private int dy;

        internal Smoke(Pet pet, string folder)
        {
            this.pet = pet;
            this.folder = folder;
            Directory.CreateDirectory(folder);
        }

        internal void Tick(double now)
        {
            if (finished) return;
            if (started < 0) started = now;
            if (stepStart < 0) stepStart = now;
            if (now - started > 90) { Finish("Timed out at step " + step + "."); return; }
            if (now - stepStart > 15) { checks.Add("INFO " + Seen()); Finish("Step " + step + " took longer than 15 seconds."); return; }
            try
            {
                if (Step(now))
                {
                    step++;
                    stepStart = now;
                }
            }
            catch (Exception error) { Finish(error.Message); }
        }

        private int Px(double dip) { return (int)Math.Round(dip * pet.DesktopScaling); }

        private PixelRect Work()
        {
            Screen screen = pet.Screens.Primary ?? pet.ScreenHere();
            if (screen == null) throw new Exception("No screen was found.");
            return screen.WorkingArea;
        }

        private bool Sitting()
        {
            Ledge seat = pet.Seat;
            return seat != null && pet.Hip.Y == seat.Y;
        }

        private bool Step(double now)
        {
            if (step == 0)
            {
                foreach (Clip clip in pet.Clips) if (!clip.Even || clip.Strip.PixelSize.Height != 384) throw new Exception("A sprite strip doesn't split into even 384 px cells.");
                checks.Add("PASS sprites: all 10 clips, standing and perched, load from the app and split into even cells");
                foreach (HyprMonitor monitor in Hypr.ParseMonitors("[{\"name\":\"DP-1\",\"x\":0,\"y\":0,\"width\":1920,\"height\":1080,\"scale\":1,\"reserved\":[0,28,0,0],\"activeWorkspace\":{\"id\":5},\"specialWorkspace\":{\"id\":0}},{\"name\":\"HDMI-A-1\",\"x\":161,\"y\":1080,\"width\":1920,\"height\":1080,\"scale\":1.2,\"reserved\":[0,28,0,0],\"activeWorkspace\":{\"id\":3},\"specialWorkspace\":{\"id\":-94}}]")) if (monitor.Name == "HDMI-A-1" && (monitor.X != 161 || monitor.Y != 1080 || monitor.Scale != 1.2 || monitor.WorkTop != 28)) throw new Exception("The scaled HDMI monitor JSON was parsed incorrectly.");
                List<HyprWindow> hyprClients = Hypr.ParseClients("[{\"address\":\"0xabc\",\"class\":\"test\",\"title\":\"Shelf\",\"pid\":42,\"monitor\":2,\"monitorName\":\"HDMI-A-1\",\"at\":[400,1140],\"size\":[1000,600],\"workspace\":{\"id\":3,\"name\":\"3\"},\"mapped\":true,\"hidden\":false,\"floating\":true,\"fullscreen\":0,\"focusHistoryID\":2},{\"address\":\"broken\",\"monitor\":0,\"workspace\":{\"id\":5}}]");
                PixelRect converted = Hypr.ConvertBounds(hyprClients[0], new HyprMonitor { X = 161, Y = 1080, Width = 1920, Height = 1080, Scale = 1.2 }, new PixelRect(3840, 0, 1920, 1080), 1);
                PixelRect observed = Hypr.ConvertBoundsObserved(hyprClients[0], new HyprMonitor { X = 161, Y = 1080, Width = 1920, Height = 1080, Scale = 1.2 }, new PixelRect(3840, 0, 1920, 1080), 1);
                if (converted.X != 4079 || converted.Y != 60 || converted.Width != 1000 || observed.X != 4079 || observed.Y != 60 || observed.Width != 1000 || hyprClients[0].Handle != 0xabc || hyprClients[1].Handle != 0) throw new Exception("Hyprland XWayland coordinate conversion or address parsing failed: " + converted + " observed " + observed + " address " + hyprClients[0].Handle + "/" + hyprClients[1].Handle + ".");
                if (Hypr.ResolveMonitorIndex(new[] { new HyprMonitor { Name = "HDMI-A-1" } }, "HDMI-A-1", 0) != 0 || Hypr.MeasureCoordinateScale(new HyprMonitor { Width = 1920 }, new[] { new PixelRect(3840, 0, 1920, 1080) }) != 1) throw new Exception("Hyprland monitor matching or XWayland scale measurement failed.");
                if (Hypr.MeasureXWaylandScale(new HyprMonitor { Width = 1920 }, new[] { new PixelRect(3840, 0, 1920, 1080) }) != 1 || Hypr.MeasureXWaylandScale(new HyprMonitor { Width = 1920 }, new[] { new PixelRect(3840, 0, 3840, 2160) }) != 1) throw new Exception("XWayland output scale measurement failed.");
                checks.Add("PASS Hyprland JSON: scaled HDMI geometry, monitor matching and XWayland conversion parse");
                DesktopEntry entry = LinuxHands.ParseDesktop("[Desktop Entry]\nName=Default name\nName[es]=Nombre localizado\nExec=/usr/bin/example --open %u\nType=Application\n");
                if (entry.Name != "Default name" || entry.Exec != "/usr/bin/example --open %u" || entry.Type != "Application") throw new Exception("A desktop entry was parsed incorrectly.");
                DesktopEntry hidden = LinuxHands.ParseDesktop("[Desktop Entry]\nName=Hidden app\nExec=/usr/bin/hidden\nType=Application\nNoDisplay=true\n");
                if (!hidden.NoDisplay) throw new Exception("NoDisplay was not parsed.");
                checks.Add("PASS desktop parser: unlocalized Name wins over Name[es], and NoDisplay is recognized");
                string[] media = LinuxHands.MediaCommand(0xAF);
                if (media == null || string.Join(" ", media) != "wpctl set-volume -l 1.0 @DEFAULT_AUDIO_SINK@ 2%+") throw new Exception("The volume-up media key was mapped incorrectly.");
                if (LinuxHands.MediaCommand(0xB3) == null || LinuxHands.MediaCommand(0xAD) == null || LinuxHands.MediaCommand(0xFE) != null) throw new Exception("A media key mapping was missing or unexpected.");
                checks.Add("PASS media command mapping: play/pause, volume and mute keys map to Linux commands without launching them");
                List<string> browserArgs = LinuxHands.BrowserArgs("google-chrome --new-window %u", "https://example.com/a?x=1");
                if (browserArgs.Count != 3 || browserArgs[2] != "https://example.com/a?x=1") throw new Exception("A browser URL placeholder was not substituted safely.");
                List<string> noUrlArgs = LinuxHands.BrowserArgs("spotify --uri=%u", null);
                if (noUrlArgs.Count != 1 || noUrlArgs[0] != "spotify") throw new Exception("An empty desktop URL placeholder left a malformed argument.");
                checks.Add("PASS desktop Exec fields: URLs substitute as a single argument and empty URL arguments are removed");
                if (LinuxBrowsers.DefaultDesktop("google-chrome.desktop", "firefox.desktop") != "google-chrome.desktop") throw new Exception("The XDG browser setting didn't take precedence.");
                if (LinuxBrowsers.DefaultDesktop("", "/apps/google-chrome.desktop") != "google-chrome.desktop") throw new Exception("The MIME browser fallback didn't resolve its desktop entry.");
                checks.Add("PASS browser preference: xdg-settings takes precedence over MIME handler and IDs use exact desktop file names");
                List<string> chromeArgs = LinuxBrowsers.ExecArgs("google-chrome --new-window %u", "https://example.com/");
                if (chromeArgs.Count != 3 || chromeArgs[2] != "https://example.com/") throw new Exception("A browser Exec URL field wasn't substituted.");
                checks.Add("PASS browser Exec: selected-browser URL is passed as a separate argument");
                if (MediaArbiter.Choose("Spotify", new[] { "spotify", "chromium.instance4834" }) != "spotify" || MediaArbiter.Choose("Chrome", new[] { "spotify", "chromium.instance4834" }) != "chromium.instance4834" || MediaArbiter.Choose("ninguno", new[] { "spotify", "chromium.instance4834" }) != "none" || MediaArbiter.Choose("ambos", new[] { "spotify", "chromium.instance4834" }) != "all") throw new Exception("Media arbitration couldn't resolve a player choice.");
                checks.Add("PASS media arbitration: Spotify, Chrome, both and neither choices resolve without controlling players");
                if (!MediaArbiter.ShouldPause(new List<string> { "spotify", "chromium.instance4834" }, "pausa") || MediaArbiter.ShouldPause(new List<string> { "spotify", "chromium.instance4834" }, "quiero escuchar Spotify")) throw new Exception("Ambiguous media actions weren't separated from explicit player choice.");
                checks.Add("PASS media safety: ambiguous commands ask which player, explicit player intent doesn't");
                if (pet.ActualTransparencyLevel == WindowTransparencyLevel.Transparent) checks.Add("PASS her window is see-through");
                else checks.Add("LIMIT her window isn't see-through here (" + pet.ActualTransparencyLevel + "); on Linux that needs a compositor");
                checks.Add("INFO window list: " + pet.Desk.Name);
                return true;
            }
            if (step == 1)
            {
                if (pet.Falling) return false;
                if (!Sitting()) throw new Exception("She didn't sit down after falling in.");
                checks.Add("PASS falling: she drops in from the top of the screen and sits on " + (pet.Seat.Floor ? "the bottom of the work area" : "the top of a window below her"));
                Snap(pet.Content as Control, "pet.png");
                pet.Grab();
                if (pet.Seat != null || pet.Falling || !pet.Dragging) throw new Exception("Grabbing her didn't lift her off her seat.");
                PixelRect area = Work();
                pet.Move(pet.Seated(area.X + area.Width / 2, area.Bottom - Px(100)));
                if (!pet.Drop()) throw new Exception("Dropping her just above the floor didn't make her fall.");
                return true;
            }
            if (step == 2)
            {
                if (pet.Falling) return false;
                if (!Sitting()) throw new Exception("A dropped Capri didn't sit down again.");
                checks.Add("PASS drag and drop: grabbing her lifts her off, dropping her near a ledge makes her fall and sit on it");
                PixelPoint hip = pet.Hip;
                pet.Resize(320);
                if (pet.Hip != hip) throw new Exception("Resizing moved her off her seat: " + hip + " to " + pet.Hip + ".");
                pet.Resize(240);
                if (pet.Hip != hip) throw new Exception("Resizing back moved her off her seat.");
                checks.Add("PASS size: changing her size keeps her sitting on the same spot");
                pet.Think = null;
                pet.Brain.Model = null;
                pet.Hear("hello?");
                if (pet.Talk.Reply != Pet.Offline) throw new Exception("No brain didn't explain how to get one: " + pet.Talk.Reply);
                checks.Add("PASS no brain: she says to install Ollama and pull a model");
                pet.Talk.Dismiss();
                pet.Think = delegate(string words) { return words == "hey" ? "Just vibing, choom." : "wrong words"; };
                pet.Talk.Pill.Press(1);
                if (!pet.Talk.Typing) throw new Exception("The pencil didn't open the typing box.");
                pet.Talk.Submit("hey");
                return true;
            }
            if (step == 3)
            {
                if (!pet.Talk.Done) return false;
                if (settled < 0) settled = now;
                if (now - settled < 0.3) return false;
                if (pet.Talk.Shown != "Just vibing, choom.") throw new Exception("Her reply showed wrong: " + pet.Talk.Shown);
                Bubble bubble = pet.Talk.Bubble;
                if (bubble.Position.Y + (int)Math.Round(bubble.Height * bubble.DesktopScaling) > pet.Head.Y + 2) throw new Exception("Her bubble covered her head.");
                if (!pet.Talk.Pill.IsVisible) throw new Exception("The pencil and mic pill didn't show under her while she talked.");
                Snap(pet.Talk.Face, "talk.png");
                Snap(pet.Talk.Typer.Content as Control, "typer.png");
                Snap(pet.Talk.Pill.Content as Control, "pill.png");
                Scene("desktop.png", new Window[] { pet, bubble, pet.Talk.Pill }, new PixelRect[0]);
                checks.Add("PASS chat: the typing box sends a line to her brain, the reply types out in a bubble above her head");
                pet.Talk.Dismiss();
                PixelRect area = Work();
                shelfAsked = new PixelRect(area.X + area.Width / 2 - Px(350), area.Y + area.Height / 2 - Px(140), Px(700), Px(360));
                shelf = Shelf(shelfAsked);
                return true;
            }
            if (step == 4)
            {
                shelfSpot = Near(shelfAsked, true);
                if (shelfSpot == null) return false;
                pet.StandUp();
                pet.Move(pet.Seated(shelfSpot.Bounds.X + Px(220), shelfSpot.Bounds.Y - Px(12)));
                if (!pet.Drop()) throw new Exception("She couldn't reach the test shelf 12 px under her.");
                return true;
            }
            if (step == 5)
            {
                if (pet.Falling) return false;
                Ledge seat = pet.Seat;
                if (seat == null || seat.Window != shelfSpot.Window || pet.Hip.Y != shelfSpot.Bounds.Y) throw new Exception("She didn't land on the test shelf's top edge.");
                if (pet.Showing != pet.PerchGlitch) throw new Exception("Sitting on the shelf didn't glitch her on.");
                checks.Add("PASS sitting: dropped over a window, she lands on its top edge and glitches into her seat");
                Scene("shelf.png", new Window[] { pet }, new[] { shelfSpot.Bounds });
                hipBefore = pet.Hip;
                dx = Px(120);
                dy = Px(60);
                Send(shelf, "move " + (shelfAsked.X + dx) + " " + (shelfAsked.Y + dy));
                return true;
            }
            if (step == 6)
            {
                Ledge seat = pet.Seat;
                if (seat == null) throw new Exception("She stood up when the shelf moved.");
                if (seat.Y != shelfSpot.Bounds.Y + dy) return false;
                if (pet.Hip.Y != seat.Y || pet.Hip.X != hipBefore.X + dx) throw new Exception("She didn't move with the shelf: hip " + pet.Hip + ", expected " + new PixelPoint(hipBefore.X + dx, seat.Y) + ".");
                checks.Add("PASS following: when the window moves, she moves with it and stays seated");
                coverAsked = new PixelRect(pet.Hip.X - Px(130), seat.Y - Px(60), Px(260), Px(200));
                cover = Shelf(coverAsked);
                return true;
            }
            if (step == 7)
            {
                if (coverAt < 0)
                {
                    if (Near(coverAsked, false) == null) return false;
                    coverAt = now;
                }
                if (pet.Seat != null) return false;
                double waited = now - coverAt;
                if (waited < 0.45 || waited > 2) throw new Exception("She stood up " + waited.ToString("0.00") + " s after a window covered her seat; it should be about 0.7 s.");
                if (pet.Falling) throw new Exception("Standing up made her fall.");
                checks.Add("PASS covered: a window over her seat makes her stand up after " + waited.ToString("0.0") + " s");
                Send(cover, "quit");
                return true;
            }
            if (step == 8)
            {
                if (!cover.HasExited) return false;
                shelfSpot = Near(new PixelRect(shelfAsked.X + dx, shelfAsked.Y + dy, shelfAsked.Width, shelfAsked.Height), true);
                if (shelfSpot == null) return false;
                pet.Move(pet.Seated(shelfSpot.Bounds.X + Px(220), shelfSpot.Bounds.Y - Px(12)));
                if (!pet.Drop()) throw new Exception("She couldn't get back onto the shelf.");
                return true;
            }
            if (step == 9)
            {
                if (pet.Falling) return false;
                if (pet.Seat == null || pet.Seat.Window != shelfSpot.Window) throw new Exception("She didn't sit back on the shelf.");
                Send(shelf, "hide");
                hideAt = now;
                return true;
            }
            if (step == 10)
            {
                if (pet.Seat != null) return false;
                double waited = now - hideAt;
                if (waited > 1.5) throw new Exception("She took " + waited.ToString("0.00") + " s to stand up after her window hid.");
                checks.Add("PASS window gone: when her window hides, she stands up after " + waited.ToString("0.0") + " s");
                Send(shelf, "quit");
                Finish(null);
                return true;
            }
            return false;
        }

        private Process Shelf(PixelRect rect)
        {
            ProcessStartInfo start = new ProcessStartInfo(Environment.ProcessPath, "--shelf " + rect.X + " " + rect.Y + " " + (int)Math.Round(rect.Width / pet.DesktopScaling) + " " + (int)Math.Round(rect.Height / pet.DesktopScaling));
            start.UseShellExecute = false;
            start.RedirectStandardInput = true;
            Process process = Process.Start(start);
            if (process == null) throw new Exception("The test shelf window didn't start.");
            shelves.Add(process);
            return process;
        }

        private static void Send(Process process, string line)
        {
            if (process != null && !process.HasExited) process.StandardInput.WriteLine(line);
        }

        private string Seen()
        {
            List<string> parts = new List<string>();
            foreach (Spot spot in pet.Windows()) parts.Add(spot.Window + " " + spot.Bounds + (spot.Seat ? " seat" : ""));
            return "window list " + pet.Desk.Name + (pet.DeskTrouble == null ? "" : " after " + pet.DeskTrouble) + ", scale " + pet.Desk.Scale.ToString("0.###") + ", shelf asked " + shelfAsked + " " + (shelf == null ? "not started" : shelf.HasExited ? "exited " + shelf.ExitCode : "running") + ", saw " + (parts.Count == 0 ? "no windows" : string.Join("; ", parts)) + ", " + pet.Desk.Debug();
        }

        private Spot Near(PixelRect asked, bool seat)
        {
            Spot best = null;
            int bestFar = int.MaxValue;
            foreach (Spot spot in pet.Windows())
            {
                if (seat && !spot.Seat) continue;
                int far = Math.Abs(spot.Bounds.X - asked.X) + Math.Abs(spot.Bounds.Y - asked.Y) + Math.Abs(spot.Bounds.Width - asked.Width);
                if (far < Px(120) && far < bestFar) { best = spot; bestFar = far; }
            }
            return best;
        }

        private void Snap(Control control, string name)
        {
            if (control == null) return;
            PixelSize size = new PixelSize(Math.Max(1, (int)Math.Ceiling(control.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(control.Bounds.Height)));
            using (RenderTargetBitmap shot = new RenderTargetBitmap(size, new Vector(96, 96)))
            {
                shot.Render(control);
                shot.Save(Path.Combine(folder, name));
            }
        }

        private void Scene(string name, Window[] windows, PixelRect[] boxes)
        {
            int left = int.MaxValue;
            int top = int.MaxValue;
            int right = int.MinValue;
            int bottom = int.MinValue;
            double scale = pet.DesktopScaling;
            foreach (Window window in windows)
            {
                left = Math.Min(left, window.Position.X);
                top = Math.Min(top, window.Position.Y);
                right = Math.Max(right, window.Position.X + (int)Math.Ceiling(window.Bounds.Width * scale));
                bottom = Math.Max(bottom, window.Position.Y + (int)Math.Ceiling(window.Bounds.Height * scale));
            }
            foreach (PixelRect box in boxes)
            {
                left = Math.Min(left, box.X);
                top = Math.Min(top, box.Y);
                right = Math.Max(right, box.Right);
                bottom = Math.Max(bottom, box.Bottom);
            }
            double pad = 40;
            PixelSize size = new PixelSize((int)Math.Ceiling((right - left) / scale + pad * 2), (int)Math.Ceiling((bottom - top) / scale + pad * 2));
            using (RenderTargetBitmap scene = new RenderTargetBitmap(size, new Vector(96, 96)))
            {
                using (DrawingContext context = scene.CreateDrawingContext())
                {
                    context.FillRectangle(new SolidColorBrush(Color.FromRgb(238, 240, 243)), new Rect(0, 0, size.Width, size.Height));
                    foreach (PixelRect box in boxes)
                    {
                        Rect place = new Rect(pad + (box.X - left) / scale, pad + (box.Y - top) / scale, box.Width / scale, box.Height / scale);
                        context.FillRectangle(new SolidColorBrush(Color.FromRgb(200, 204, 210)), place);
                        context.FillRectangle(new SolidColorBrush(Color.FromRgb(120, 126, 136)), new Rect(place.X, place.Y, place.Width, Math.Min(32, place.Height)));
                    }
                    foreach (Window window in windows)
                    {
                        Control face = window.Content as Control;
                        if (face == null || !window.IsVisible) continue;
                        PixelSize part = new PixelSize(Math.Max(1, (int)Math.Ceiling(face.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(face.Bounds.Height)));
                        RenderTargetBitmap shot = new RenderTargetBitmap(part, new Vector(96, 96));
                        shot.Render(face);
                        context.DrawImage(shot, new Rect(pad + (window.Position.X - left) / scale, pad + (window.Position.Y - top) / scale, part.Width, part.Height));
                    }
                }
                scene.Save(Path.Combine(folder, name));
            }
        }

        private void Finish(string failure)
        {
            if (finished) return;
            finished = true;
            foreach (Process process in shelves)
            {
                try
                {
                    if (process.HasExited) continue;
                    process.StandardInput.WriteLine("quit");
                    if (!process.WaitForExit(3000)) process.Kill();
                }
                catch (Exception) { }
            }
            List<string> lines = new List<string>(checks);
            lines.Insert(0, "Capri cross-platform smoke test on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
            if (failure != null) lines.Add("FAIL " + failure);
            File.WriteAllLines(Path.Combine(folder, "smoke-test.txt"), lines);
            pet.Finish(failure == null ? 0 : 1);
        }
    }
}
