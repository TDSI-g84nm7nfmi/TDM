using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TDM.Services;

namespace TDM.Windows
{
    public partial class ExtensionInstallerWindow : Window
    {
        private readonly List<BrowserScanner.BrowserInfo> _browsers;

        public bool RememberChoice
        {
            get => RememberChoiceCheck.IsChecked == true;
        }

        /// <summary>扩展目录在 %LocalAppData%\TDM\extension</summary>
        private static string ExtensionDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TDM", "extension");

        public ExtensionInstallerWindow(List<BrowserScanner.BrowserInfo> browsers)
        {
            InitializeComponent();
            _browsers = browsers;

            _browsers = _browsers
                .OrderBy(b => b.NativeHostRegistered ? 1 : 0)
                .ThenBy(b => b.Name)
                .ToList();

            foreach (var b in _browsers)
            {
                var item = CreateItem(b);
                BrowserList.Items.Add(item);
            }

            UpdateSummary();
        }

        private ListBoxItem CreateItem(BrowserScanner.BrowserInfo b)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBorder = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)),
                Child = new TextBlock
                {
                    Text = b.Name.Substring(0, 1).ToUpper(), FontSize = 16,
                    FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            sp.Children.Add(new TextBlock
            {
                Text = b.Name, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
            });
            sp.Children.Add(new TextBlock
            {
                Text = b.ExecutablePath ?? "", FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x80)),
                Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            var status = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(b.NativeHostRegistered
                    ? Color.FromRgb(0x4C, 0xAF, 0x50) : Color.FromRgb(0xFF, 0x98, 0x00)),
                Child = new TextBlock
                {
                    Text = b.NativeHostRegistered ? "已注册" : "未注册",
                    Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold
                }
            };
            Grid.SetColumn(status, 2);
            grid.Children.Add(status);

            var item = new ListBoxItem { Content = grid, Tag = b, IsSelected = !b.NativeHostRegistered };
            return item;
        }

        private void UpdateSummary()
        {
            int total = _browsers.Count;
            int registered = _browsers.Count(b => b.NativeHostRegistered);
            int unregistered = total - registered;
            SummaryLabel.Text = $"共检测到 {total} 个浏览器内核。";
            InstallButton.Content = $"为 {unregistered} 个浏览器安装扩展";
        }

        // ========== 安装 ==========
        // 扩展通过 WebSocket (ws://127.0.0.1:19199/tdm) 直接与 TDM 通信，
        // 无需 Python、注册表、扩展 ID 计算。只需要复制扩展 + --load-extension。

        private void OnInstall(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = BrowserList.SelectedItems
                    .OfType<ListBoxItem>()
                    .Select(i => i.Tag as BrowserScanner.BrowserInfo)
                    .Where(b => b != null)
                    .Cast<BrowserScanner.BrowserInfo>()
                    .ToList();

                if (selected.Count == 0)
                {
                    MessageBox.Show("没有选中浏览器。", App.AppName,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 1) 复制扩展文件
                if (!PrepareExtensionFiles())
                {
                    MessageBox.Show("扩展文件准备失败。", App.AppName,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2) 加载到浏览器
                if (AutoInstallCheck.IsChecked == true)
                {
                    foreach (var b in selected)
                    {
                        try { LoadToBrowser(b); }
                        catch (Exception ex) { Logger.Warn($"加载到 {b.Name} 失败: {ex.Message}"); }
                    }
                }

                if (RememberChoice) SettingsService.Update(s => s.ScanBrowsersOnStartup = false);
                SettingsService.Save();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Logger.Error("安装扩展失败", ex);
                MessageBox.Show("安装失败：" + ex.Message, App.AppName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool PrepareExtensionFiles()
        {
            try
            {
                var src = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extension");
                if (!Directory.Exists(src)) { Logger.Warn("未找到扩展目录: " + src); return false; }
                CopyDirContents(src, ExtensionDir);
                Logger.Info($"扩展已复制到: {ExtensionDir}");
                return true;
            }
            catch (Exception ex) { Logger.Warn($"准备扩展失败: {ex.Message}"); return false; }
        }

        private static void LoadToBrowser(BrowserScanner.BrowserInfo browser)
        {
            string exe = browser.ExecutablePath ?? "";
            if (!File.Exists(exe)) { Logger.Warn($"浏览器主程序不存在: {exe}"); return; }

            string procName = Path.GetFileNameWithoutExtension(exe);
            KillProcess(procName);
            System.Threading.Thread.Sleep(1000);

            string args = $"--load-extension=\"{ExtensionDir}\" --no-first-run";
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
            });
            Logger.Info($"已启动 {browser.Name} 并加载扩展: {ExtensionDir}");
        }

        private static void KillProcess(string processName)
        {
            try { foreach (var p in Process.GetProcessesByName(processName)) { try { p.Kill(); } catch { } } }
            catch { }
        }

        private static void CopyDirContents(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, f);
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(f, target, overwrite: true);
            }
        }

        private void OnOpenExtFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(ExtensionDir);
                Process.Start(new ProcessStartInfo("explorer.exe", ExtensionDir) { UseShellExecute = true });
            }
            catch (Exception ex) { Logger.Warn("打开扩展目录失败: " + ex.Message); }
        }

        private void OnOpenHelp(object sender, RoutedEventArgs e)
        {
            try
            {
                var helpFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extension", "加载扩展.txt");
                if (File.Exists(helpFile))
                    Process.Start(new ProcessStartInfo(helpFile) { UseShellExecute = true });
            }
            catch (Exception ex) { Logger.Warn("打开说明失败: " + ex.Message); }
        }

        private void OnSkip(object sender, RoutedEventArgs e)
        {
            if (RememberChoice) SettingsService.Update(s => s.ScanBrowsersOnStartup = false);
            SettingsService.Save();
            DialogResult = false;
            Close();
        }
    }
}