using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace TDM.Services
{
    public static class BlurEffectHelper
    {
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum AccentState
        {
            Disabled = 0,
            EnableGradient = 1,
            EnableTransparent = 2,
            EnableBlurBehind = 3,
            EnableAcrylicBlurBehind = 4,
            EnableHostBackdrop = 5
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19,
        }

        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        private enum DWM_SYSTEMBACKDROP_TYPE
        {
            DWMSBT_DISABLE = 1,
            DWMSBT_MAIN = 2,
            DWMSBT_TRANSIENT = 3,
            DWMSBT_TABBED = 4
        }

        public static bool IsWindows11
        {
            get
            {
                try
                {
                    var v = Environment.OSVersion.Version;
                    return v.Major >= 10 && v.Build >= 22000;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void EnableBlur(Window window, bool acrylic = true)
        {
            if (window == null) return;
            try
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                if (hwnd == IntPtr.Zero) return;

                if (IsWindows11)
                    EnableWin11(hwnd, acrylic);
                else
                    EnableWin10(hwnd, acrylic);
            }
            catch (Exception ex)
            {
                Logger.Warn($"启用毛玻璃失败: {ex.Message}");
            }
        }

        private static void EnableWin11(IntPtr hwnd, bool acrylic)
        {
            try
            {
                int useBackdrop = acrylic ? 3 : 2;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref useBackdrop, sizeof(int));
            }
            catch { }

            try
            {
                var accent = new AccentPolicy
                {
                    AccentState = acrylic ? AccentState.EnableAcrylicBlurBehind : AccentState.EnableBlurBehind,
                    AccentFlags = 2,
                    GradientColor = 0x00FFFFFF
                };
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = Marshal.SizeOf(typeof(AccentPolicy))
                };
                var ptr = Marshal.AllocHGlobal(data.SizeOfData);
                Marshal.StructureToPtr(accent, ptr, false);
                data.Data = ptr;
                try { SetWindowCompositionAttribute(hwnd, ref data); }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { }
        }

        private static void EnableWin10(IntPtr hwnd, bool acrylic)
        {
            try
            {
                var accent = new AccentPolicy
                {
                    AccentState = acrylic ? AccentState.EnableAcrylicBlurBehind : AccentState.EnableBlurBehind,
                    AccentFlags = 2,
                    GradientColor = 0x00FFFFFF
                };
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = Marshal.SizeOf(typeof(AccentPolicy))
                };
                var ptr = Marshal.AllocHGlobal(data.SizeOfData);
                Marshal.StructureToPtr(accent, ptr, false);
                data.Data = ptr;
                try { SetWindowCompositionAttribute(hwnd, ref data); }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { }
        }

        public static void Disable(Window window)
        {
            if (window == null) return;
            try
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                if (hwnd == IntPtr.Zero) return;
                int v = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, sizeof(int));
            }
            catch { }
            try
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                var accent = new AccentPolicy { AccentState = AccentState.Disabled };
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = Marshal.SizeOf(typeof(AccentPolicy))
                };
                var ptr = Marshal.AllocHGlobal(data.SizeOfData);
                Marshal.StructureToPtr(accent, ptr, false);
                data.Data = ptr;
                try { SetWindowCompositionAttribute(hwnd, ref data); }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch (Exception ex) { Logger.Warn("关闭毛玻璃失败: " + ex.Message); }
        }
    }
}
