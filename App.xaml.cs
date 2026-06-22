using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TDM.Services;
using TDM.ViewModels;

namespace TDM
{
    public partial class App : Application
    {
        public static string AppName { get; } = "TDM";
        public static string AppFullName { get; } = "TDSI Download Manager";
        public static string AppVersion { get; } = "1.0.0";
        public static string AppAuthor { get; } = "B站@会飞的附魔下界合金剑";

        public static string DataDirectory { get; private set; } = "";
        public static string LogDirectory { get; private set; } = "";
        public static string ConfigDirectory { get; private set; } = "";

        /// <summary>
        /// 跨线程访问 UI Dispatcher 的便捷属性。
        /// </summary>
        public static Dispatcher CurrentDispatcher => Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        private static Mutex? _singleInstanceMutex;

        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例检测
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, $"TDM-SingleInstance-{Environment.MachineName}", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show($"{AppName} 已经在运行中！请检查系统托盘。", AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                Environment.Exit(0);
                return;
            }

            // 数据目录
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            DataDirectory = Path.Combine(docs, "TDM");
            LogDirectory = Path.Combine(DataDirectory, "Logs");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogDirectory);

            // Config 目录（在安装目录下）
            ConfigDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
            Directory.CreateDirectory(ConfigDirectory);

            // 日志初始化
            Logger.Init(LogDirectory);

            // 加载设置（必须在主题加载前）
            SettingsService.Load();

            // 主题加载
            ThemeManager.Load();

            // 启动浏览器桥接服务（接收扩展推过来的资源）
            try
            {
                BrowserBridgeService.Start();
                BrowserBridgeService.Instance.ResourceReceived += OnBrowserResourceReceived;
            }
            catch (Exception ex) { Logger.Warn("BrowserBridge 启动失败: " + ex.Message); }

            // 启动画面（轻量版）
            base.OnStartup(e);
        }

        private void OnBrowserResourceReceived(object? sender, Models.SniffedResource resource)
        {
            try
            {
                // 切到主线程
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // 推入嗅探资源队列
                        DownloadViewModel.Instance.AddIncomingResource(resource);
                        // 自动跳到下载页
                        if (MainWindow is MainWindow mw)
                        {
                            mw.NavDownload.IsSelected = true;
                            mw.Activate();
                            if (mw.WindowState == WindowState.Minimized) mw.WindowState = WindowState.Normal;
                        }
                        Logger.Info($"浏览器资源已入队：{resource.Filename} ({resource.Type})");
                    }
                    catch (Exception ex) { Logger.Warn("资源入队失败: " + ex.Message); }
                });
            }
            catch (Exception ex) { Logger.Warn("OnBrowserResourceReceived: " + ex.Message); }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                SettingsService.Save();
                ThemeManager.PersistCurrent();
            }
            catch { /* 忽略退出时的错误 */ }
            _singleInstanceMutex?.ReleaseMutex();
            base.OnExit(e);
        }

        // 防止异常处理器自身抛异常时陷入递归 MessageBox 风暴
        private bool _showingError = false;
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                Logger.Error("UI 线程未处理异常", e.Exception);
                if (_showingError) { e.Handled = true; return; }
                _showingError = true;
                MessageBox.Show(
                    $"发生未处理异常：{e.Exception.Message}\n\n程序将继续运行。",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                _showingError = false;
            }
            catch { }
            finally { e.Handled = true; }
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Error("非 UI 线程未处理异常", ex);
            }
        }
    }
}
