using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TDM.Models;
using TDM.Services;
using TDM.ViewModels;
using TDM.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;

namespace TDM
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private DownloadView? _downloadView;
        private HistoryView? _historyView;
        private SettingsView? _settingsView;
        private WelcomeView? _welcomeView;

        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _trayMenu;
        private ClipboardMonitor? _clipboard;
        private BrowserBridgeService? _browserBridge;
        private bool _userClosed;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public MainWindow()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Logger.Error("XAML 初始化失败", ex);
                MessageBox.Show("界面初始化失败：" + ex.Message, App.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
            DataContext = this;

            // 启动视图（在构造时就创建，捕获异常；这样 XAML 中 IsSelected 触发 SelectionChanged 时不会 NRE）
            _welcomeView = SafeCreate(() => new WelcomeView());
            _downloadView = SafeCreate(() => new DownloadView());
            _historyView = SafeCreate(() => new HistoryView());
            _settingsView = SafeCreate(() => new SettingsView());
            if (ViewHost != null) ViewHost.Content = _welcomeView;

            // 应用图标（在 Loaded 之后才安全设置）
            SourceInitialized += (_, _) =>
            {
                try
                {
                    var src = IconHelper.GetAppIcon();
                    if (src != null)
                    {
                        this.Icon = src;
                        // 新版 UI 用品牌渐变 "T" 字符作为 logo，不再用 Image
                    }
                }
                catch { }
            };

            // 加载完成后执行剩余订阅、剪贴板、托盘等
            Loaded += (_, _) =>
            {
                try
                {
                    // 状态栏事件订阅
                    DownloadManager.Instance.ItemAdded += (_, _) => UpdateStatus();
                    DownloadManager.Instance.ItemRemoved += (_, _) => UpdateStatus();
                    DownloadManager.Instance.ItemCompleted += OnItemCompleted;
                    DownloadManager.Instance.ItemFailed += OnItemFailed;

                    // 剪贴板
                    try
                    {
                        _clipboard = new ClipboardMonitor();
                        _clipboard.UrlDetected += OnClipboardUrl;
                        if (SettingsService.Current.EnableClipboard) _clipboard.Start();
                    }
                    catch (Exception ex) { Logger.Warn("剪贴板初始化失败: " + ex.Message); }

                    // 托盘图标
                    InitTrayIcon();

                    // 启动浏览器桥接服务（用于接收浏览器扩展发来的资源）
                    try
                    {
                        if (_browserBridge == null)
                        {
                            _browserBridge = BrowserBridgeService.Instance;
                            _browserBridge.ResourceReceived += OnBrowserResourceReceived;
                            // 启动由 App.xaml.cs 完成
                        }
                    }
                    catch (Exception ex) { Logger.Warn("BrowserBridge 启动失败: " + ex.Message); }

                    // 启动扩展 WebSocket 桥接服务器（浏览器扩展通过 WebSocket 直接连接 TDM）
                    try
                    {
                        var bridge = Bridge.ExtensionBridgeServer.Instance;
                        if (!bridge.IsRunning) bridge.Start();
                        bridge.OnConnectionChanged += OnExtensionConnectionChanged;
                        UpdateExtensionStatus(bridge.IsConnected);
                    }
                    catch (Exception ex) { Logger.Warn("ExtensionBridgeServer 启动失败: " + ex.Message); }

                    // 主题切换
                    ThemeManager.ThemeChanged += (_, _) =>
                    {
                        UpdateVersionInfo();
                        AnimateThemeTransition();
                    };

                    // 历史
                    HistoryService.Load();
                    _historyView?.Refresh();

                    UpdateStatus();
                    UpdateVersionInfo();

                    // 启动完成动画
                    AnimateStartup();

                    // 异步：扫描浏览器并询问是否安装扩展
                    Dispatcher.BeginInvoke(new Action(() => ScanAndOfferExtension()), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    Logger.Error("Loaded 阶段初始化失败", ex);
                }

                // 毛玻璃
                try
                {
                    if (SettingsService.Current.AcrylicBlur)
                    {
                        BlurEffectHelper.EnableBlur(this, acrylic: true);
                        // 新版 UI 用 VisualBrush 光晕背景，毛玻璃自然透出
                    }
                }
                catch (Exception ex) { Logger.Warn("毛玻璃初始化失败: " + ex.Message); }

                // 启动统计定时器（1s 刷新仪表板）
                try { StartStatsTimer(); } catch { }
            };
        }

        private T? SafeCreate<T>(Func<T> factory) where T : FrameworkElement
        {
            try { return factory(); }
            catch (Exception ex)
            {
                Logger.Error($"创建视图 {typeof(T).Name} 失败", ex);
                return null;
            }
        }

        private void InitTrayIcon()
        {
            try
            {
                _trayMenu = new ContextMenuStrip();
                _trayMenu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
                _trayMenu.Items.Add("暂停/继续", null, (_, _) => _downloadView?.TogglePause());
                _trayMenu.Items.Add(new ToolStripSeparator());
                _trayMenu.Items.Add("设置", null, (_, _) => { NavSettings.IsSelected = true; });
                _trayMenu.Items.Add("关于 TDM", null, (_, _) => ShowAbout());
                _trayMenu.Items.Add(new ToolStripSeparator());
                _trayMenu.Items.Add("退出", null, (_, _) => { _userClosed = true; Close(); });

                System.Drawing.Icon trayIco;
                try
                {
                    var exe = Process.GetCurrentProcess().MainModule?.FileName;
                    trayIco = (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                        ? (System.Drawing.Icon.ExtractAssociatedIcon(exe) ?? System.Drawing.SystemIcons.Application)
                        : System.Drawing.SystemIcons.Application;
                }
                catch
                {
                    trayIco = System.Drawing.SystemIcons.Application;
                }

                _trayIcon = new NotifyIcon
                {
                    Icon = trayIco,
                    Text = "TDM - TDSI Download Manager",
                    Visible = false,
                    ContextMenuStrip = _trayMenu
                };
                _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
                Logger.Info("托盘图标已初始化");
            }
            catch (Exception ex)
            {
                Logger.Error("托盘图标初始化失败", ex);
            }
        }

        private void OnNavChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavWelcome == null || ViewHost == null) return;

            FrameworkElement? nextView = null;
            if (NavWelcome.IsSelected) nextView = _welcomeView;
            else if (NavDownload.IsSelected) nextView = _downloadView;
            else if (NavHistory.IsSelected)
            {
                _historyView?.Refresh();
                nextView = _historyView;
            }
            else if (NavSettings.IsSelected) nextView = _settingsView;
            if (nextView == null) return;

            // 视图切换淡入淡出动画（Windows 11 Fluent 风格）
            if (ViewHost.Content is FrameworkElement current && current != nextView)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                fadeOut.Completed += (_, _) =>
                {
                    ViewHost.Content = nextView;
                    nextView.Opacity = 0;
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    nextView.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                };
                current.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            else
            {
                ViewHost.Content = nextView;
            }
        }

        #region 拖放支持
        private void OnWindowDragEnter(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnWindowDrop(object sender, System.Windows.DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent(DataFormats.Text)) return;
                if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    var text = (string)e.Data.GetData(DataFormats.Text);
                    var firstUrl = text.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(s => ClipboardMonitor.IsUrl(s));
                    if (firstUrl != null)
                    {
                        NavDownload.IsSelected = true;
                        _downloadView?.SetUrl(firstUrl);
                        SetStatus("已通过拖放填入链接", 3000);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("拖放处理失败: " + ex.Message);
            }
        }
        #endregion

        #region 剪贴板
        private void OnClipboardUrl(object? sender, string url)
        {
            if (!SettingsService.Current.EnableClipboard) return;
            if (_downloadView != null && NavDownload.IsSelected)
            {
                if (_downloadView.CurrentUrl == url) return;
                _downloadView.SetUrl(url);
                SetStatus("已自动填入剪贴板链接", 3000);
            }
            else
            {
                SetStatus($"检测到剪贴板链接：{Truncate(url, 60)}", 3000);
            }

            if (SettingsService.Current.NotifyFinished && _trayIcon != null && _trayIcon.Visible)
            {
                _trayIcon.BalloonTipTitle = "TDM";
                _trayIcon.BalloonTipText = $"检测到链接：{Truncate(url, 60)}";
                _trayIcon.ShowBalloonTip(2500);
            }
        }
        #endregion

        #region 浏览器扩展桥接
        private void OnBrowserResourceReceived(object? sender, SniffedResource resource)
        {
            Dispatcher.Invoke(() =>
            {
                if (_downloadView == null) return;
                _downloadView.ViewModel.AddBrowserResource(resource);
                if (!NavDownload.IsSelected) NavDownload.IsSelected = true;
                SetStatus($"从浏览器接收到资源：{resource.Filename}", 3000);
            });
        }
        #endregion

        #region 状态栏
        public void SetStatus(string text, int timeoutMs = 0)
        {
            if (StatusLabel == null) return;
            StatusLabel.Text = text;
            if (timeoutMs > 0)
            {
                var dispatcher = Dispatcher;
                dispatcher.DelayInvoke(() =>
                {
                    if (StatusLabel.Text == text) StatusLabel.Text = "就绪";
                }, timeoutMs);
            }
        }

        private void UpdateStatus()
        {
            if (DownloadCountLabel == null) return;
            int active = DownloadManager.Instance.Items.Count;
            DownloadCountLabel.Text = active == 0 ? "无活动任务" : $"{active} 个任务";
        }

        private void UpdateVersionInfo()
        {
            if (VersionLabel != null) VersionLabel.Text = $"v{App.AppVersion}";
        }
        #endregion

        #region 事件
        private void OnItemCompleted(object? sender, Models.DownloadItem item)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus();
                SetStatus($"下载完成：{item.FileName}", 5000);
                HistoryService.Add(new Models.HistoryEntry
                {
                    Url = item.Url,
                    FilePath = item.FilePath,
                    Size = item.TotalSize,
                    Status = "完成",
                    StartTime = item.StartTime,
                    EndTime = DateTime.Now
                });
                HistoryService.Save();
                _historyView?.Refresh();

                if (_trayIcon != null && SettingsService.Current.NotifyFinished)
                {
                    if (!_trayIcon.Visible) _trayIcon.Visible = true;
                    _trayIcon.BalloonTipTitle = "下载完成";
                    _trayIcon.BalloonTipText = item.FileName;
                    _trayIcon.ShowBalloonTip(3000);
                }
            });
        }

        private void OnItemFailed(object? sender, Models.DownloadItem item)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus();
                SetStatus($"下载失败：{item.FileName}", 5000);
                HistoryService.Add(new Models.HistoryEntry
                {
                    Url = item.Url,
                    FilePath = item.FilePath,
                    Size = item.DownloadedBytes,
                    Status = "失败",
                    StartTime = item.StartTime,
                    EndTime = DateTime.Now,
                    Error = item.ErrorMessage
                });
                HistoryService.Save();
                _historyView?.Refresh();

                if (_trayIcon != null && SettingsService.Current.NotifyError)
                {
                    if (!_trayIcon.Visible) _trayIcon.Visible = true;
                    _trayIcon.BalloonTipTitle = "下载失败";
                    _trayIcon.BalloonTipText = item.ErrorMessage ?? "未知错误";
                    _trayIcon.ShowBalloonTip(4000);
                }
            });
        }
        #endregion

        #region 标题栏按钮
        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            if (_trayIcon != null && !_trayIcon.Visible)
            {
                _trayIcon.Visible = true;
                _trayIcon.BalloonTipTitle = "TDM";
                _trayIcon.BalloonTipText = "TDM 已最小化到系统托盘";
                _trayIcon.ShowBalloonTip(2000);
            }
            Hide();
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                    if (MaximizeButton != null) MaximizeButton.ToolTip = "最大化";
                }
                else
                {
                    WindowState = WindowState.Maximized;
                    if (MaximizeButton != null) MaximizeButton.ToolTip = "向下还原";
                }
            }
            catch (Exception ex) { Logger.Warn("最大化切换失败: " + ex.Message); }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            // 复用现有关闭逻辑（托盘化或真正退出）
            Close();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e) => ShowAbout();

        public void ShowAbout()
        {
            try
            {
                var win = new Windows.AboutWindow { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error("关于窗口打开失败", ex);
            }
        }
        #endregion

        #region 窗口行为
        private void ShowMainWindow()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized && SettingsService.Current.MinimizeToTray)
            {
                if (_trayIcon != null && !_trayIcon.Visible) _trayIcon.Visible = true;
                Hide();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_userClosed && SettingsService.Current.MinimizeToTray)
            {
                e.Cancel = true;
                if (_trayIcon != null && !_trayIcon.Visible) _trayIcon.Visible = true;
                Hide();
                _trayIcon?.ShowBalloonTip(1500, "TDM", "TDM 仍在后台运行", System.Windows.Forms.ToolTipIcon.Info);
                return;
            }

            // 真正的关闭 → 先播放退出动画
            e.Cancel = true;
            AnimateExit(() =>
            {
                _userClosed = true;
                DisposeTray();
                Application.Current.Shutdown();
            });
        }

        private void DisposeTray()
        {
            try
            {
                _clipboard?.Dispose();
                _browserBridge?.Dispose();
                _browserBridge = null;
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
            catch (Exception ex) { Logger.Warn("释放托盘资源失败: " + ex.Message); }
        }
        #endregion

        #region 新版 UI 交互（主题切换、搜索、新建、扩展状态、统计刷新）
        private void OnThemeToggleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ThemeManager.CycleTheme();
            }
            catch (Exception ex) { Logger.Warn("主题切换失败: " + ex.Message); }
        }

        private void OnQuickSearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            try
            {
                var text = (QuickSearchBox.Text ?? "").Trim();
                if (string.IsNullOrEmpty(text)) return;

                // 看起来像 URL → 填到下载
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    NavDownload.IsSelected = true;
                    _downloadView?.SetUrl(text);
                    QuickSearchBox.Clear();
                    SetStatus("已填入搜索链接", 2500);
                    return;
                }

                // 否则：尝试切换到历史
                NavHistory.IsSelected = true;
                _historyView?.ApplySearchFilter(text);
                SetStatus($"搜索历史：{text}", 2500);
            }
            catch (Exception ex) { Logger.Warn("搜索失败: " + ex.Message); }
        }

        private void OnNewDownloadClick(object sender, RoutedEventArgs e)
        {
            try
            {
                NavDownload.IsSelected = true;
                _downloadView?.FocusUrlBox();
            }
            catch (Exception ex) { Logger.Warn("新建下载失败: " + ex.Message); }
        }

        private void OnExtensionStatusClick(object sender, RoutedEventArgs e)
        {
            try
            {
                NavSettings.IsSelected = true;
                SetStatus("请在设置中扫描并安装浏览器扩展", 3000);
            }
            catch (Exception ex) { Logger.Warn("跳转设置失败: " + ex.Message); }
        }

        private void OnExtensionConnectionChanged(bool connected)
        {
            try { Dispatcher.Invoke(() => UpdateExtensionStatus(connected)); }
            catch { }
        }

        private void UpdateExtensionStatus(bool connected)
        {
            try
            {
                if (ExtStatusDot == null || ExtStatusText == null) return;
                if (connected)
                {
                    ExtStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x1B, 0xC9, 0x82));
                    ExtStatusText.Text = "扩展已连接";
                }
                else
                {
                    ExtStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xC9, 0x9A, 0x1B));
                    ExtStatusText.Text = "扩展未安装";
                }
            }
            catch { }
        }

        // 实时刷新统计卡（每 1 秒）
        private DispatcherTimer? _statsTimer;
        private void StartStatsTimer()
        {
            try
            {
                if (_statsTimer != null) return;
                _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _statsTimer.Tick += (_, _) =>
                {
                    try { UpdateStats(); }
                    catch (Exception ex) { Logger.Warn("统计刷新失败: " + ex.Message); }
                };
                _statsTimer.Start();
            }
            catch (Exception ex) { Logger.Warn("统计定时器启动失败: " + ex.Message); }
        }

        private void UpdateStats()
        {
            try
            {
                if (_downloadView == null) return;
                int running = 0, completed = 0, failed = 0;
                long totalSpeed = 0;
                foreach (var item in DownloadManager.Instance.Items)
                {
                    var st = item.StatusText ?? "";
                    if (st == "完成") completed++;
                    else if (st == "失败" || st == "错误") failed++;
                    else { running++; totalSpeed += (long)item.Speed; }
                }
                if (_downloadView.StatRunning != null) _downloadView.StatRunning.Text = running.ToString();
                if (_downloadView.StatCompleted != null) _downloadView.StatCompleted.Text = completed.ToString();
                if (_downloadView.StatFailed != null) _downloadView.StatFailed.Text = failed.ToString();
                if (_downloadView.StatSpeed != null) _downloadView.StatSpeed.Text = FormatBytes(totalSpeed) + "/s";
                if (SpeedLabel != null) SpeedLabel.Text = running > 0 ? FormatBytes(totalSpeed) + "/s" : "--";
            }
            catch (Exception ex) { Logger.Warn("更新统计失败: " + ex.Message); }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.#} {sizes[order]}";
        }
        #endregion

        #region 启动/退出动画
        private void AnimateStartup()
        {
            try
            {
                this.Opacity = 0;
                var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                this.BeginAnimation(Window.OpacityProperty, anim);
            }
            catch (Exception ex) { Logger.Warn("启动动画失败: " + ex.Message); }
        }

        private void AnimateExit(Action after)
        {
            try
            {
                var anim = new DoubleAnimation(this.Opacity, 0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                anim.Completed += (_, _) => after?.Invoke();
                this.BeginAnimation(Window.OpacityProperty, anim);
            }
            catch
            {
                after?.Invoke();
            }
        }
        #endregion

        #region 主题切换动画（Windows 风格丝滑过渡：水波纹高光 + 淡入淡出）
        private void AnimateThemeTransition()
        {
            try
            {
                // 1) 主视图淡出再淡入（让色彩过渡更柔和）
                if (ViewHost?.Content is FrameworkElement fe)
                {
                    var fadeOut = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                    };
                    fe.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    Dispatcher.DelayInvoke(() =>
                    {
                        var fadeIn = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(220))
                        {
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                        };
                        fe.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    }, 200);
                }

                // 2) 高光水波纹扫过（用渐变 brush 动画）
                if (ThemeSweep != null && ThemeSweep.Fill is LinearGradientBrush lb)
                {
                    ThemeSweep.Visibility = Visibility.Visible;
                    ThemeSweep.Opacity = 0;

                    // 如果 brush 已冻结（XAML 加载时可能被自动 Freeze），Clone 一份
                    var sweep = lb.IsFrozen ? lb.Clone() : lb;
                    if (lb.IsFrozen) ThemeSweep.Fill = sweep;

                    try
                    {
                        // 把高光颜色设为主题色
                        var primary = (Color)Application.Current.Resources["PrimaryColor"];
                        sweep.GradientStops[0].Color = Color.FromArgb(0, primary.R, primary.G, primary.B);
                        sweep.GradientStops[1].Color = Color.FromArgb(90, primary.R, primary.G, primary.B);
                        sweep.GradientStops[2].Color = Color.FromArgb(0, primary.R, primary.G, primary.B);

                        // 从左 → 右移动高光中心（用 brush 自身的 StartPoint/EndPoint 动画）
                        var startAnim = new PointAnimation(new Point(-1, 0), new Point(0, 0), TimeSpan.FromMilliseconds(700))
                        {
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };
                        var endAnim = new PointAnimation(new Point(0, 0), new Point(1, 0), TimeSpan.FromMilliseconds(700))
                        {
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };
                        sweep.BeginAnimation(LinearGradientBrush.StartPointProperty, startAnim);
                        sweep.BeginAnimation(LinearGradientBrush.EndPointProperty, endAnim);
                    }
                    catch (Exception ex) { Logger.Warn("高光 brush 动画失败: " + ex.Message); }

                    // 透明度淡入淡出
                    var opa = new DoubleAnimationUsingKeyFrames();
                    opa.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, TimeSpan.Zero));
                    opa.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1, TimeSpan.FromMilliseconds(120), new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }));
                    opa.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1, TimeSpan.FromMilliseconds(420)));
                    opa.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(0, TimeSpan.FromMilliseconds(700), new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }));
                    opa.Completed += (_, _) => ThemeSweep.Visibility = Visibility.Collapsed;
                    ThemeSweep.BeginAnimation(UIElement.OpacityProperty, opa);
                }
            }
            catch (Exception ex) { Logger.Warn("主题切换动画失败: " + ex.Message); }
        }
        #endregion

        #region 版本号连点 → 开发者模式（日志窗口）
        private DateTime[] _versionClicks = new DateTime[5];
        private int _versionClickCount = 0;

        private void OnVersionLabelClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var now = DateTime.Now;
                // 5 秒内的连点才计
                if (_versionClickCount > 0 && (now - _versionClicks[_versionClickCount - 1]).TotalSeconds > 5)
                {
                    _versionClickCount = 0;
                }
                _versionClicks[_versionClickCount % 5] = now;
                _versionClickCount++;
                int left = 5 - _versionClickCount;
                if (_versionClickCount < 5)
                {
                    SetStatus($"再点 {Math.Max(0, left)} 次进入开发者模式", 1500);
                    return;
                }
                // 达到 5 次 → 打开日志窗口
                _versionClickCount = 0;
                ShowLogWindow();
            }
            catch (Exception ex) { Logger.Warn("连点版本号失败: " + ex.Message); }
        }

        public void ShowLogWindow()
        {
            try
            {
                var win = new Windows.LogWindow { Owner = this };
                win.Show();
            }
            catch (Exception ex)
            {
                Logger.Error("日志窗口打开失败", ex);
            }
        }
        #endregion

        #region 浏览器扩展扫描
        private bool _browserScanDone = false;

        private void ScanAndOfferExtension()
        {
            if (_browserScanDone) return;
            _browserScanDone = true;
            try
            {
                if (!SettingsService.Current.ScanBrowsersOnStartup) return;

                Logger.Info("正在扫描系统中的浏览器...");
                var browsers = Services.BrowserScanner.Scan();
                var unregistered = browsers.Where(b => !b.NativeHostRegistered).ToList();
                Logger.Info($"扫描完成：发现 {browsers.Count} 个浏览器，{unregistered.Count} 个未注册 native host");

                if (browsers.Count == 0)
                {
                    // 没装任何 Chromium 浏览器 → 在状态栏友好提示
                    SetStatus("未检测到 Chromium 内核浏览器，可手动到设置中扫描", 5000);
                    return;
                }

                if (unregistered.Count == 0)
                {
                    // 全部已注册
                    return;
                }

                // 显示对话框
                var win = new Windows.ExtensionInstallerWindow(browsers) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Warn("扫描浏览器失败: " + ex.Message);
            }
        }
        #endregion

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, max) + "…";
        }
    }
}
