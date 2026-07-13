using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace TDM.Services
{
    public static class Win32Helper
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void BringToFront(Window window)
        {
            if (window == null) return;
            try
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                SetForegroundWindow(hwnd);
                ShowWindow(hwnd, 9);
            }
            catch { }
        }
    }
}
