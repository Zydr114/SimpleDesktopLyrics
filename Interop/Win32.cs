using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EasyDesktopLyrics.Interop;

/// <summary>悬浮窗所需的 Win32 扩展样式与置顶维持。</summary>
internal static class Win32
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020; // 鼠标穿透
    private const int WS_EX_NOACTIVATE = 0x08000000;  // 永不获得焦点
    private const int WS_EX_TOOLWINDOW = 0x00000080;  // 不出现在 Alt+Tab / 任务栏

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>
    /// 设置/取消鼠标穿透。不引入 WS_EX_LAYERED（与 Avalonia DirectComposition 渲染冲突）。
    /// </summary>
    public static void SetClickThrough(IntPtr hwnd, bool enable)
    {
        if (hwnd == IntPtr.Zero) return;
        var ex = GetWindowLongW(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        ex = enable ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongW(hwnd, GWL_EXSTYLE, ex);
    }

    /// <summary>幂等重申置顶（对抗后来置顶的窗口）。</summary>
    public static void AssertTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

/// <summary>开机自启（HKCU Run 键）。</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EasyDesktopLyrics";

    public static void Sync(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        var current = key.GetValue(ValueName) as string;
        if (enabled)
        {
            var expected = $"\"{Environment.ProcessPath}\" --autostart";
            if (current != expected)
                key.SetValue(ValueName, expected);
        }
        else if (current != null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
