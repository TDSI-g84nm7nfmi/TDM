using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TDM.Services
{
    /// <summary>
    /// 毛玻璃 (Acrylic / Blur) 效果封装。
    /// Win10: DwmEnableBlurBehindWindow (Mica-like)
    /// Win11: SetWindowCompositionAttribute (AccentPolicy=Acrylic)
    /// </summary>
    public static class BlurEffectHelper
    {
        #region Win32 imports

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_BLURBEHIND
        {
            public int dwFlags;
            public bool fEnable;
            public IntPtr hRgnBlur;
            public bool fTransitionOnMaximized;
        }

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
            EnableHostBackdrop = 5  // Win11
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19,
            WCA_HOST_BACKDROP_BRUSH = 0x13  // Win11
        }

        // DWMWA values
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;  // Win11 22H2+

        private enum DWM_SYSTEMBACKDROP_TYPE
        {
            DWMSBT_DISABLE = 1,
            DWMSBT_MAIN = 2,           // Mica
            DWMSBT_TRANSIENT = 3,      // Acrylic
            DWMSBT_TABBED = 4          // Tabs
        }

        #endregion

        public static bool IsWindows11 { get; } = IsWin11();
        private static bool IsWin11()
        {
            try
            {
                var v = Environment.OSVersion.Version;
                return v.Major >= 10 && v.Build >= 22000;
            }
            catch { return false; }
        }

        public static void EnableBlur(Window window, bool acrylic = true)
        {
            if (window == null) return;
            try
            {
                var helper = new WindowInteropHelper(window);
                if (helper.Handle == IntPtr.Zero) return;

                if (IsWindows11)
                {
                    EnableWin11(helper.Handle, acrylic);
                }
                else
                {
                    EnableWin10(helper.Handle, acrylic);
                }
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
                int useBackdrop = acrylic ? 3 : 2; // 3=Acrylic, 2=Mica
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref useBackdrop, sizeof(int));
            }
            catch { }

            try
            {
                // 同时设置 WCA_ACCENT_POLICY 兼容旧 Win11
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
                var blur = new DWM_BLURBEHIND
                {
                    dwFlags = 1, // DWM_BB_ENABLE
                    fEnable = true,
                    hRgnBlur = IntPtr.Zero,
                    fTransitionOnMaximized = false
                };
                DwmEnableBlurBehindWindow(hwnd, ref blur);
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

        public static void Disable(Window window)
        {
            if (window == null) return;
            try
            {
                var helper = new WindowInteropHelper(window);
                if (helper.Handle == IntPtr.Zero) return;
                int v = 1;
                DwmSetWindowAttribute(helper.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref v, sizeof(int));
            }
            catch { }
            try
            {
                var helper = new WindowInteropHelper(window);
                var accent = new AccentPolicy { AccentState = AccentState.Disabled };
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = Marshal.SizeOf(typeof(AccentPolicy))
                };
                var ptr = Marshal.AllocHGlobal(data.SizeOfData);
                Marshal.StructureToPtr(accent, ptr, false);
                data.Data = ptr;
                try { SetWindowCompositionAttribute(helper.Handle, ref data); }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch (Exception ex) { Logger.Warn("关闭毛玻璃失败: " + ex.Message); }
        }
    }
}
