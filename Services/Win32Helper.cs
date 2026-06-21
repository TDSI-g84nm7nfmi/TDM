using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TDM.Services
{
    /// <summary>
    /// Win32 与系统级辅助方法。
    /// </summary>
    public static class Win32Helper
    {
        // 防止重复启动
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void BringToFront(Window window)
        {
            if (window == null) return;
            try
            {
                var helper = new WindowInteropHelper(window);
                SetForegroundWindow(helper.Handle);
                ShowWindow(helper.Handle, 9); // SW_RESTORE
            }
            catch { }
        }
    }
}
