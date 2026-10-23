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
        public static bool IsOwn(IntPtr handle)
        {
            uint id;
            GetWindowThreadProcessId(handle, out id);
            return id == Process.GetCurrentProcess().Id;
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
