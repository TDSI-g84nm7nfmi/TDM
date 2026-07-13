using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TDM.Views;
using TDM.ViewModels;
using TDM.Services;
using Windows.Storage.Streams;
using H.NotifyIcon;
using WinRT.Interop;

namespace TDM
{
    public sealed partial class MainWindow : Window
    {
        private bool _isMaximized;
        private bool _closing;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            SetupTrayIcon();
            SetupMica();

            ContentFrame.Navigate(typeof(DownloadView));
            UpdateStatusBar();

            Closed += OnClosed;
        }

        public void NavigateToDownload()
        {
            NavView.SelectedItem = NavDownload;
            ContentFrame.Navigate(typeof(DownloadView));
        }

        private void SetupTrayIcon()
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image\\icon.ico");
            if (File.Exists(iconPath))
            {
                TrayIcon.IconSource = new BitmapImage(new Uri(iconPath));
            }
            TrayIcon.ToolTipText = "TDM - TDSI Download Manager";

            var menu = new MenuFlyout();
            var showItem = new MenuFlyoutItem { Text = "显示主窗口" };
            showItem.Click += (_, _) =>
            {
                Activate();
                var hwnd = WindowNative.GetWindowHandle(this);
                NativeMethods.ShowWindowAsync(hwnd, 1);
            };
            menu.Items.Add(showItem);
            var exitItem = new MenuFlyoutItem { Text = "退出" };
            exitItem.Click += (_, _) =>
            {
                _closing = true;
                TrayIcon.Visibility = Visibility.Collapsed;
                Application.Current.Exit();
            };
            menu.Items.Add(exitItem);
            TrayIcon.ContextFlyout = menu;
        }

        private void SetupMica()
        {
            try
            {
                SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            }
            catch { }
        }

        private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is string tag)
            {
                Type? page = tag switch
                {
                    "download" => typeof(DownloadView),
                    "history" => typeof(HistoryView),
                    "settings" => typeof(SettingsView),
                    _ => null
                };
                if (page != null && ContentFrame.CurrentSourcePageType != page)
                    ContentFrame.Navigate(page);
            }
        }

        public void UpdateStatusBar()
        {
            var vm = DownloadViewModel.Instance;
            int active = 0;
            foreach (var item in vm.Items)
                if (item.Status == Models.DownloadStatus.Downloading) active++;
            StatusLabel.Text = active > 0 ? $"正在下载 {active} 个文件..." : "就绪";
            DownloadCountLabel.Text = $"{vm.Items.Count} 个任务";
        }

        private void OnThemeToggleClick(object sender, RoutedEventArgs e)
        {
            ThemeManager.CycleTheme();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            NativeMethods.ShowWindowAsync(hwnd, 6);
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            if (_isMaximized)
            {
                NativeMethods.ShowWindowAsync(hwnd, 1);
                _isMaximized = false;
            }
            else
            {
                NativeMethods.ShowWindowAsync(hwnd, 3);
                _isMaximized = true;
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _closing = true;
            TrayIcon.Visibility = Visibility.Collapsed;
            Application.Current.Exit();
        }

        private void OnVersionClick(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            Logger.Info("版本点击");
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            if (!_closing)
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                NativeMethods.ShowWindowAsync(hwnd, 6);
                args.Handled = true;
            }
        }
    }
}
