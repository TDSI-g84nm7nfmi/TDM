using System;
using System.Windows;
using System.Windows.Controls;
using TDM.Services;
using TDM.ViewModels;

namespace TDM.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

        public SettingsView()
        {
            InitializeComponent();
            DataContext = new SettingsViewModel();
            ViewModel.ThemeChanged += OnThemeChanged;
            ViewModel.BlurToggleRequested += OnBlurToggled;

            // 初始化主题单选
            Loaded += (_, _) =>
            {
                foreach (var rb in FindVisualChildren<RadioButton>(this))
                {
                    if (rb.Tag is string tag && tag == ViewModel.SelectedTheme)
                    {
                        rb.IsChecked = true;
                        break;
                    }
                }
            };
        }

        private void OnThemeChanged(object? sender, string themeName)
        {
            if (System.Windows.Forms.SystemInformation.UserInteractive)
            {
                ThemeManager.Set(themeName);
            }
        }

        private void OnBlurToggled(object? sender, bool enable)
        {
            if (System.Windows.Application.Current.MainWindow is Window w)
            {
                if (enable) BlurEffectHelper.EnableBlur(w, acrylic: true);
                else BlurEffectHelper.Disable(w);
            }
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e) { }

        private void OnThemeChanged(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                ViewModel.SelectedTheme = tag;
            }
        }

        private void OnRescanBrowser(object sender, RoutedEventArgs e)
        {
            try
            {
                var browsers = Services.BrowserScanner.Scan();
                if (browsers.Count == 0)
                {
                    System.Windows.MessageBox.Show("未检测到任何 Chromium 内核浏览器。", App.AppName,
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }
                var win = new TDM.Windows.ExtensionInstallerWindow(browsers)
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("扫描失败：" + ex.Message, App.AppName,
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void OnOpenLogWindow(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new TDM.Windows.LogWindow
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                win.Show();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("打开日志窗口失败：" + ex.Message, App.AppName,
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject dep) where T : DependencyObject
        {
            if (dep == null) yield break;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(dep); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(dep, i);
                if (child is T t) yield return t;
                foreach (var c in FindVisualChildren<T>(child)) yield return c;
            }
        }
    }
}
