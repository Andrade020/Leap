using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Leap;

public record WindowInfo(IntPtr Hwnd, string Title, string ProcessName);

/// <summary>Enumera e foca janelas de nível superior — o "trocador de buffers" do Leap.</summary>
public static class WindowSwitcher
{
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    private const int GwlExstyle = -20;
    private const int WsExToolwindow = 0x00000080;
    private const int DwmwaCloaked = 14;
    private const int SwRestore = 9;

    public static List<WindowInfo> List()
    {
        var result = new List<WindowInfo>();
        var self = Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            if ((GetWindowLong(hwnd, GwlExstyle) & WsExToolwindow) != 0) return true;

            // janelas UWP "fantasma" ficam cloaked
            var cloaked = 0;
            DwmGetWindowAttribute(hwnd, DwmwaCloaked, ref cloaked, sizeof(int));
            if (cloaked != 0) return true;

            var len = GetWindowTextLength(hwnd);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == self) return true;

            var proc = "";
            try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }

            result.Add(new WindowInfo(hwnd, sb.ToString(), proc));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public static void Focus(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
        SetForegroundWindow(hwnd);
    }
}
