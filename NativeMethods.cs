using System;
using System.Runtime.InteropServices;

namespace TDM
{
    /// <summary>
    /// 原生 Win32 API 封装，用于激活已有实例窗口等场景。
    /// </summary>
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr hWnd);
    }
}
