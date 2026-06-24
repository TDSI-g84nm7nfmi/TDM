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

            var iconPath = new System.Windows.Shapes.Path
            {
                Width = 16, Height = 16, Stretch = Stretch.Uniform,
                Fill = Brushes.White,
                Data = this.TryFindResource("IconBrowser") as Geometry,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var iconBorder = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(6),
                Background = BrowserColor(b.Name),
                Child = iconPath
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

        private static Brush BrowserColor(string name)
        {
            string n = name?.ToLowerInvariant() ?? "";
            if (n.Contains("chrome")) return new SolidColorBrush(Color.FromRgb(0x4C, 0x8B, 0xFF));
            if (n.Contains("edge")) return new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7));
            if (n.Contains("tabbit")) return new SolidColorBrush(Color.FromRgb(0x9C, 0x4F, 0xFF));
            if (n.Contains("brave")) return new SolidColorBrush(Color.FromRgb(0xFB, 0x54, 0x2B));
            if (n.Contains("vivaldi")) return new SolidColorBrush(Color.FromRgb(0xEF, 0x39, 0x39));
            if (n.Contains("opera")) return new SolidColorBrush(Color.FromRgb(0xFF, 0x1B, 0x2D));
            if (n.Contains("yandex")) return new SolidColorBrush(Color.FromRgb(0xFC, 0xD0, 0x0A));
            if (n.Contains("360")) return new SolidColorBrush(Color.FromRgb(0x12, 0xB9, 0xE8));
            if (n.Contains("qq")) return new SolidColorBrush(Color.FromRgb(0x12, 0xB9, 0xE8));
            return new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF));
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

            // 1) 杀光同进程名所有实例（含子进程和已锁共享内存的 helper）
            KillAll(procName);
            System.Threading.Thread.Sleep(800);
            // 2) 通用 Chromium helper 进程（renderer、gpu、utility、crashpad 等）
            //    这些进程持有共享内存句柄，会导致新进程报"配额不足"
            KillHelperProcesses();
            System.Threading.Thread.Sleep(800);

            // 3) 启动浏览器加载扩展（独立 user-data-dir 避免冲突）
            string userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TDM", "browser_profiles", browser.Id ?? procName);
            Directory.CreateDirectory(userDataDir);

            string args = $"--load-extension=\"{ExtensionDir}\" " +
                          $"--user-data-dir=\"{userDataDir}\" " +
                          $"--no-first-run --no-default-browser-check " +
                          $"--disable-features=ChromeWhatsNewUI";

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = args,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Normal,
                    });
                    Logger.Info($"已启动 {browser.Name} 并加载扩展: {ExtensionDir} (尝试 {attempt})");
                    return;
                }
                catch (Exception ex) when (attempt < 3)
                {
                    Logger.Warn($"启动 {browser.Name} 失败（尝试 {attempt}/3）: {ex.Message}");
                    KillHelperProcesses();
                    System.Threading.Thread.Sleep(1500);
                }
                catch (Exception ex)
                {
                    Logger.Error($"启动 {browser.Name} 最终失败: {ex.Message}", ex);
                    MessageBox.Show(
                        $"启动 {browser.Name} 失败：{ex.Message}\n\n" +
                        "可能原因：\n" +
                        "• 浏览器有残留进程占用共享内存\n" +
                        "• 系统资源不足\n\n" +
                        "请手动关闭所有浏览器窗口后重试。",
                        App.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static void KillAll(string processName)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(processName))
                {
                    try { p.Kill(); p.WaitForExit(2000); } catch { }
                }
            }
            catch { }
        }

        // 杀掉所有 Chromium helper 进程（renderer、gpu、crashpad、utility 等）
        // 这些进程即使主程序已退出，仍可能持有共享内存句柄导致配额不足
        private static void KillHelperProcesses()
        {
            string[] helpers = {
                "chrome", "msedge", "chromium", "brave", "vivaldi", "tabbit", "tabbitbrowser",
                "360chrome", "qqbrowser", "centbrowser", "yandex", "iridium",
                "chrome_gpu_child", "chrome_renderer", "chrome_crashpad", "chrome_utility",
                "msedge_gpu_child", "msedge_renderer", "msedge_crashpad", "msedge_utility"
            };
            foreach (var name in helpers)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(); p.WaitForExit(1000); } catch { }
                    }
                }
                catch { }
            }
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