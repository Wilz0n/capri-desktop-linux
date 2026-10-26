using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace CapriDesktop
{
    internal sealed class HyprMonitor
    {
        internal string Name;
        internal int X;
        internal int Y;
        internal int Width;
        internal int Height;
        internal double Scale;
        internal int WorkTop;
        internal int ActiveWorkspace;
        internal int SpecialWorkspace;
    }

    internal sealed class HyprWindow
    {
        internal string Address;
        internal long Handle;
        internal string Class;
        internal string Title;
        internal int Pid;
        internal int Monitor;
        internal string MonitorName;
        internal int X;
        internal int Y;
        internal int Width;
        internal int Height;
        internal int Workspace;
        internal string WorkspaceName;
        internal bool Special;
        internal bool Mapped;
        internal bool Hidden;
        internal bool Floating;
        internal bool Fullscreen;
        internal long FocusHistory;
    }

    internal static class Hypr
    {
        internal static string Run(params string[] args)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("hyprctl");
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                foreach (string arg in args) info.ArgumentList.Add(arg);
                using (Process process = Process.Start(info))
                {
                    if (process == null || !process.WaitForExit(2500))
                    {
                        if (process != null) try { process.Kill(); } catch (InvalidOperationException) { }
                        throw new InvalidOperationException("hyprctl timed out or failed to launch.");
                    }
                    string output = process.StandardOutput.ReadToEnd();
                    if (process.ExitCode != 0) throw new InvalidOperationException(process.StandardError.ReadToEnd());
                    return output;
                }
            }
            catch (System.ComponentModel.Win32Exception error) { throw new InvalidOperationException("Cannot run hyprctl: " + error.Message, error); }
        }

        internal static List<HyprMonitor> ParseMonitors(string json)
        {
            List<HyprMonitor> found = new List<HyprMonitor>();
            using (JsonDocument doc = JsonDocument.Parse(json))
                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    HyprMonitor monitor = new HyprMonitor();
                    monitor.Name = Text(item, "name");
                    monitor.X = Int(item, "x");
                    monitor.Y = Int(item, "y");
                    monitor.Width = Int(item, "width");
                    monitor.Height = Int(item, "height");
                    monitor.Scale = Double(item, "scale", 1);
                    JsonElement reserved;
                    if (item.TryGetProperty("reserved", out reserved) && reserved.ValueKind == JsonValueKind.Array && reserved.GetArrayLength() >= 4)
                        monitor.WorkTop = reserved[1].GetInt32();
                    JsonElement active;
                    if (item.TryGetProperty("activeWorkspace", out active)) monitor.ActiveWorkspace = Int(active, "id");
                    JsonElement special;
                    if (item.TryGetProperty("specialWorkspace", out special)) monitor.SpecialWorkspace = Int(special, "id");
                    found.Add(monitor);
                }
            return found;
        }

        internal static List<HyprWindow> ParseClients(string json)
        {
            List<HyprWindow> found = new List<HyprWindow>();
            using (JsonDocument doc = JsonDocument.Parse(json))
                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    HyprWindow window = new HyprWindow();
                    window.Address = Text(item, "address");
                    try { window.Handle = Convert.ToInt64(window.Address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? window.Address.Substring(2) : window.Address, 16); }
                    catch (Exception) { window.Handle = 0; }
                    window.Class = Text(item, "class");
                    window.Title = Text(item, "title");
                    window.Pid = Int(item, "pid");
                    window.Monitor = Int(item, "monitor", -1);
                    window.MonitorName = Text(item, "monitorName");
                    window.X = Pair(item, "at", 0);
                    window.Y = Pair(item, "at", 1);
                    window.Width = Pair(item, "size", 0);
                    window.Height = Pair(item, "size", 1);
                    window.Mapped = Bool(item, "mapped", true);
                    window.Hidden = Bool(item, "hidden");
                    window.Floating = Bool(item, "floating");
                    window.Fullscreen = Int(item, "fullscreen") != 0;
                    window.FocusHistory = Long(item, "focusHistoryID", long.MaxValue);
                    JsonElement workspace;
                    if (item.TryGetProperty("workspace", out workspace))
                    {
                        window.Workspace = Int(workspace, "id");
                        window.WorkspaceName = Text(workspace, "name");
                        window.Special = window.WorkspaceName.StartsWith("special:", StringComparison.OrdinalIgnoreCase);
                    }
                    found.Add(window);
                }
            return found;
        }

        internal static PixelRect ConvertBounds(HyprWindow window, HyprMonitor hypr, PixelRect avaloniaScreen, int scale)
        {
            int conversion = Math.Max(1, scale);
            int localX = window.X - hypr.X;
            int localY = window.Y - hypr.Y;
            int x = avaloniaScreen.X + (int)Math.Round(localX / (double)conversion);
            int y = avaloniaScreen.Y + (int)Math.Round(localY / (double)conversion);
            int width = (int)Math.Round(window.Width / (double)conversion);
            int height = (int)Math.Round(window.Height / (double)conversion);
            return new PixelRect(x, y, width, height);
        }

        internal static double ScaleWindowCoordinate(double local, int monitorScale, int xwaylandScale)
        {
            return local * Math.Max(1, xwaylandScale) / Math.Max(1, monitorScale);
        }

        internal static PixelRect ConvertBoundsObserved(HyprWindow window, HyprMonitor hypr, PixelRect avaloniaScreen, int xwaylandScale)
        {
            double ratio = xwaylandScale > 1 ? xwaylandScale / Math.Max(1, hypr.Scale) : 1.0;
            int x = avaloniaScreen.X + (int)Math.Round((window.X - hypr.X) * ratio);
            int y = avaloniaScreen.Y + (int)Math.Round((window.Y - hypr.Y) * ratio);
            int width = (int)Math.Round(window.Width * ratio);
            int height = (int)Math.Round(window.Height * ratio);
            return new PixelRect(x, y, width, height);
        }

        internal static int MeasureXWaylandScale(HyprMonitor monitor, IList<PixelRect> avaloniaScreens)
        {
            if (monitor == null || monitor.Width <= 0 || avaloniaScreens == null) return 1;
            foreach (PixelRect screen in avaloniaScreens)
                if (screen.X <= monitor.X && monitor.X < screen.Right)
                    return Math.Max(1, (int)Math.Round(screen.Width / (double)monitor.Width));
            return 1;
        }

        internal static bool Visible(HyprWindow window, IList<HyprMonitor> monitors, int selfPid, ICollection<long> skip)
        {
            if (window.Handle == 0 || skip.Contains(window.Handle) || !window.Mapped || window.Hidden || window.Width <= 0 || window.Height <= 0) return false;
            if (window.Monitor < 0 || window.Monitor >= monitors.Count) return false;
            HyprMonitor monitor = monitors[window.Monitor];
            return window.Workspace == monitor.ActiveWorkspace || (window.Special && monitor.SpecialWorkspace != 0 && window.WorkspaceName == "special:" + monitor.Name);
        }

        internal static string WindowAction(string action, string address)
        {
            string dispatcher;
            if (action == "switch_to") dispatcher = "focuswindow";
            else if (action == "close") dispatcher = "closewindow";
            else if (action == "minimize") dispatcher = "movetoworkspacesilent";
            else if (action == "close_tab") dispatcher = "sendshortcut";
            else return null;
            if (action == "minimize") return "movetoworkspacesilent special:minimized,address:" + address;
            if (action == "close_tab") return "sendshortcut CTRL,W,address:" + address;
            return dispatcher + " address:" + address;
        }

        private static string Text(JsonElement obj, string key)
        {
            JsonElement value;
            return obj.TryGetProperty(key, out value) && value.ValueKind == JsonValueKind.String ? value.GetString() : "";
        }

        private static bool Bool(JsonElement obj, string key, bool fallback = false)
        {
            JsonElement value;
            if (!obj.TryGetProperty(key, out value)) return fallback;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.Number) return value.GetInt32() != 0;
            return fallback;
        }

        private static int Int(JsonElement obj, string key, int fallback = 0)
        {
            JsonElement value;
            return obj.TryGetProperty(key, out value) && value.TryGetInt32(out int result) ? result : fallback;
        }

        private static long Long(JsonElement obj, string key, long fallback)
        {
            JsonElement value;
            return obj.TryGetProperty(key, out value) && value.TryGetInt64(out long result) ? result : fallback;
        }

        private static double Double(JsonElement obj, string key, double fallback)
        {
            JsonElement value;
            return obj.TryGetProperty(key, out value) && value.TryGetDouble(out double result) ? result : fallback;
        }

        private static int Pair(JsonElement obj, string key, int at)
        {
            JsonElement value;
            return obj.TryGetProperty(key, out value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > at ? value[at].GetInt32() : 0;
        }

        internal static int ResolveMonitorIndex(IList<HyprMonitor> monitors, string name, int index)
        {
            if (monitors == null || string.IsNullOrEmpty(name)) return index;
            for (int i = 0; i < monitors.Count; i++) if (monitors[i].Name == name) return i;
            return index;
        }

        internal static int MeasureCoordinateScale(HyprMonitor monitor, IList<PixelRect> avaloniaScreens)
        {
            if (monitor == null || monitor.Width <= 0) return 1;
            if (avaloniaScreens != null)
                foreach (PixelRect screen in avaloniaScreens)
                    if (screen.X <= monitor.X && monitor.X < screen.Right)
                        return Math.Max(1, (int)Math.Round(monitor.Width / (double)screen.Width));
            return 1;
        }
    }

    internal sealed class HyprlandDesk : Desk
    {
        private readonly Avalonia.Controls.TopLevel owner;
        private List<HyprMonitor> monitors;
        private PixelRect[] screens;
        private int[] coordinateScales;
        private int[] xwaylandScales;
        private string trouble;
        private int browserScale = 1;

        internal HyprlandDesk(Avalonia.Controls.TopLevel owner = null) { this.owner = owner; Refresh(); }

        internal override string Name { get { return "Hyprland"; } }
        internal override int CoordinateScale { get { return browserScale; } }

        private void Refresh()
        {
            try
            {
                monitors = Hypr.ParseMonitors(Hypr.Run("-j", "monitors"));
                screens = CurrentScreens(owner);
                coordinateScales = new int[monitors.Count];
                xwaylandScales = new int[monitors.Count];
                browserScale = 1;
                for (int i = 0; i < monitors.Count; i++)
                {
                    coordinateScales[i] = Hypr.MeasureCoordinateScale(monitors[i], screens);
                    xwaylandScales[i] = Hypr.MeasureXWaylandScale(monitors[i], screens);
                    if (coordinateScales[i] > browserScale) browserScale = coordinateScales[i];
                }
                trouble = null;
            }
            catch (Exception error)
            {
                monitors = new List<HyprMonitor>();
                screens = new PixelRect[0];
                coordinateScales = new int[0];
                xwaylandScales = new int[0];
                trouble = error.GetType().Name + ": " + error.Message;
            }
        }

        internal static PixelRect[] CurrentScreens(Avalonia.Controls.TopLevel owner)
        {
            Avalonia.Controls.Screens collection = owner == null ? null : owner.Screens;
            if (collection == null) return new PixelRect[0];
            PixelRect[] found = new PixelRect[collection.All.Count];
            for (int i = 0; i < found.Length; i++)
            {
                Avalonia.Platform.Screen screen = collection.All[i];
                found[i] = screen.Bounds;
            }
            return found;
        }

        internal override List<Spot> Windows(ICollection<long> skip)
        {
            Refresh();
            List<Spot> found = new List<Spot>();
            if (monitors.Count == 0) return found;
            List<HyprWindow> clients = Hypr.ParseClients(Hypr.Run("-j", "clients"));
            clients.Sort(delegate(HyprWindow a, HyprWindow b) { return a.FocusHistory.CompareTo(b.FocusHistory); });
            foreach (HyprWindow window in clients)
            {
                window.Monitor = Hypr.ResolveMonitorIndex(monitors, window.MonitorName, window.Monitor);
                if (!Hypr.Visible(window, monitors, Environment.ProcessId, skip)) continue;
                HyprMonitor monitor = monitors[window.Monitor];
                if (window.Pid == Environment.ProcessId) continue;
                PixelRect screen = FindAvaloniaScreen(monitor);
                if (screen.Width <= 0) continue;
                PixelRect bounds = Hypr.ConvertBoundsObserved(window, monitor, screen, xwaylandScales[window.Monitor]);
                bool seat = !window.Fullscreen;
                found.Add(Make(window.Handle, bounds, seat));
            }
            return found;
        }

        internal override Spot Find(long window)
        {
            foreach (Spot spot in Windows(new long[0])) if (spot.Window == window) return spot;
            return null;
        }

        internal override bool Alive(long window)
        {
            try
            {
                foreach (HyprWindow client in Hypr.ParseClients(Hypr.Run("-j", "clients"))) if (client.Handle == window) return true;
            }
            catch (Exception) { }
            return false;
        }

        internal override string Debug()
        {
            if (trouble != null) return trouble;
            List<string> parts = new List<string>();
            for (int i = 0; i < monitors.Count; i++)
            {
                HyprMonitor monitor = monitors[i];
                PixelRect screen = FindAvaloniaScreen(monitor);
                parts.Add(monitor.Name + " Hypr " + monitor.X + "," + monitor.Y + " " + monitor.Width + "x" + monitor.Height + " scale " + monitor.Scale + " XWayland " + screen + " coordinate scale " + coordinateScales[i] + " XWayland scale " + xwaylandScales[i]);
            }
            return string.Join("; ", parts.ToArray());
        }

        private PixelRect FindAvaloniaScreen(HyprMonitor monitor)
        {
            foreach (PixelRect screen in screens)
                if (screen.X <= monitor.X && monitor.X < screen.Right) return screen;
            if (screens.Length == monitors.Count)
            {
                for (int i = 0; i < monitors.Count; i++) if (monitors[i].Name == monitor.Name) return screens[i];
            }
            return new PixelRect();
        }
    }
}
