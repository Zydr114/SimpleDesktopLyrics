using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EasyDesktopLyrics.Interop;

internal static class Win32
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

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

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    private static SubclassProc? _subclassDelegate;
    private static IntPtr _subclassedHwnd;
    private static bool _clickThrough;

    /// <summary>通过 WM_NCHITTEST 返回 HTTRANSPARENT 实现鼠标穿透（兼容 DirectComposition）。</summary>
    public static void SetClickThrough(IntPtr hwnd, bool enable)
    {
        if (hwnd == IntPtr.Zero) return;

        // 基础样式：无焦点、不进任务栏
        var ex = GetWindowLongW(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLongW(hwnd, GWL_EXSTYLE, ex);

        if (enable == _clickThrough && hwnd == _subclassedHwnd) return;
        _clickThrough = enable;

        if (enable)
        {
            // 窗口子类化：WM_NCHITTEST 返回 HTTRANSPARENT → 所有点击穿透
            if (_subclassDelegate == null)
                _subclassDelegate = OnSubclassMsg;
            SetWindowSubclass(hwnd, _subclassDelegate, UIntPtr.Zero, UIntPtr.Zero);
            _subclassedHwnd = hwnd;
        }
        else if (_subclassedHwnd == hwnd)
        {
            if (_subclassDelegate != null)
                RemoveWindowSubclass(hwnd, _subclassDelegate, UIntPtr.Zero);
            _subclassedHwnd = IntPtr.Zero;
        }
    }

    private static IntPtr OnSubclassMsg(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == WM_NCHITTEST)
            return (IntPtr)HTTRANSPARENT;
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public static void AssertTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

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
