using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ReadingFocus
{
    internal static class Native
    {
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint processId);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int command);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder name, int max);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardOwner();
        internal delegate void WinEvent(IntPtr hook, uint ev, IntPtr hwnd, int objectId, int childId, uint threadId, uint milliseconds);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent callback, uint processId, uint threadId, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint command);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect rect);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        private static readonly int OwnId = Process.GetCurrentProcess().Id;
        public static long Now { get { return unchecked((long)GetTickCount64()); } }
        public static string ProcessName(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return "";
            try
            {
                uint id;
                GetWindowThreadProcessId(handle, out id);
                using (Process p = Process.GetProcessById((int)id)) return p.ProcessName.ToLowerInvariant();
            }
            catch { return ""; }
        }
        public static string AppName(IntPtr handle)
        {
            try
            {
                uint id;
                GetWindowThreadProcessId(handle, out id);
                using (Process p = Process.GetProcessById((int)id))
                {
                    string description = p.MainModule.FileVersionInfo.FileDescription;
                    return string.IsNullOrWhiteSpace(description) ? p.ProcessName : description.Trim();
                }
            }
            catch { return ProcessName(handle); }
        }
        public static bool IsOwn(IntPtr handle)
        {
            uint id;
            GetWindowThreadProcessId(handle, out id);
            return id == OwnId;
        }
        // Top-level windows a user would see as open apps: visible, restorable,
        // titled, not tool or click-through overlays, not owned dialogs, and not
        // cloaked on another virtual desktop.
        public static List<IntPtr> AppWindows()
        {
            List<IntPtr> windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr l) { if (IsAppWindow(h)) windows.Add(h); return true; }, IntPtr.Zero);
            return windows;
        }
        private static bool IsAppWindow(IntPtr h)
        {
            if (!IsWindowVisible(h) || IsIconic(h) || GetWindowTextLength(h) == 0) return false;
            int ex = GetWindowLong(h, -20);
            if ((ex & 0x80) != 0 || (ex & 0x20) != 0 || (ex & 0x08000000) != 0) return false;
            if ((ex & 0x40000) == 0 && GetWindow(h, 4) != IntPtr.Zero) return false;
            Rect r;
            if (!GetWindowRect(h, out r) || r.Right <= r.Left || r.Bottom <= r.Top) return false;
            int cloaked;
            return DwmGetWindowAttribute(h, 14, out cloaked, 4) != 0 || cloaked == 0;
        }
    }
    internal static class WindowPolicy
    {
        private static readonly HashSet<string> ReadingApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "zotero", "explorer", "chrome", "msedge", "firefox", "brave", "opera", "opera_gx",
            "vivaldi", "waterfox", "floorp", "librewolf", "arc", "zen", "browser", "qqbrowser",
            "360chrome", "360chromex", "360se", "sogouexplorer", "iexplore"
        };
        private static readonly HashSet<string> SystemApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lockapp", "logonui", "consent", "credentialuibroker", "taskmgr", "securityhealthsystray",
            "sechealthui", "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp",
            "textinputhost", "applicationframehost", "systemsettings", "dwm", "winlogon"
        };
        public static bool IsReadingApp(string name) { return ReadingApps.Contains(name ?? ""); }
        public static bool ShouldBlock(string name) { return !string.IsNullOrEmpty(name) && !IsReadingApp(name) && !SystemApps.Contains(name); }
        public static bool IsSystemSurface(IntPtr handle)
        {
            StringBuilder b = new StringBuilder(256);
            Native.GetClassName(handle, b, b.Capacity);
            string s = b.ToString();
            return s == "Shell_TrayWnd" || s == "Shell_SecondaryTrayWnd" || s == "Progman" || s == "WorkerW"
                || s == "MultitaskingViewFrame" || s == "XamlExplorerHostIslandWindow" || s == "#32770";
        }
    }
}
