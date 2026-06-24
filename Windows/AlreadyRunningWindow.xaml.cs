using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TDM.Services;

namespace TDM.Windows
{
    public partial class AlreadyRunningWindow : Window
    {
        public AlreadyRunningWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => ActivateExistingWindow();
        }

        private void OnDragMove(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            ActivateExistingWindow();
            DialogResult = true;
            Close();
        }

        private static void ActivateExistingWindow()
        {
            try
            {
                var existing = Process.GetProcessesByName("TDM")
                    .FirstOrDefault(p => p.Id != Process.GetCurrentProcess().Id);
                if (existing == null) return;

                var hwnd = existing.MainWindowHandle;
                if (hwnd == IntPtr.Zero) return;

                if (NativeMethods.IsIconic(hwnd))
                    NativeMethods.ShowWindowAsync(hwnd, 9 /* SW_RESTORE */);
                NativeMethods.SetForegroundWindow(hwnd);
            }
            catch (Exception ex)
            {
                Logger.Warn("激活已有实例失败: " + ex.Message);
            }
        }
    }
}
