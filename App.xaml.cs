using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using TDM.Services;
using TDM.ViewModels;

namespace TDM
{
    public partial class App : Application
    {
        public static string AppName { get; } = "TDM";
        public static string AppFullName { get; } = "TDSI Download Manager";
        public static string AppVersion { get; } = "2.0.0";
        public static string AppAuthor { get; } = "B站@会飞的附魔下界合金剑";

        public static string DataDirectory { get; private set; } = "";
        public static string LogDirectory { get; private set; } = "";
        public static string ConfigDirectory { get; private set; } = "";

        public static DispatcherQueue CurrentDispatcher => DispatcherQueue.GetForCurrentThread();

        private static Mutex? _singleInstanceMutex;
        private static Window? _mainWindow;
        public static Window CurrentWindow => _mainWindow ?? throw new InvalidOperationException("Main window not initialized.");

        public App()
        {
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, $"TDM-SingleInstance-{Environment.MachineName}", out createdNew);
            if (!createdNew)
            {
                try
                {
                    var existing = System.Diagnostics.Process.GetProcessesByName("TDM")
                        .FirstOrDefault(p => p.Id != System.Diagnostics.Process.GetCurrentProcess().Id);
                    if (existing != null)
                    {
                        var hwnd = existing.MainWindowHandle;
                        if (hwnd != IntPtr.Zero)
                        {
                            if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindowAsync(hwnd, 9);
                            NativeMethods.SetForegroundWindow(hwnd);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("激活已有实例失败: " + ex.Message);
                }
                Environment.Exit(0);
            }

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            DataDirectory = Path.Combine(docs, "TDM");
            LogDirectory = Path.Combine(DataDirectory, "Logs");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogDirectory);

            ConfigDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
            Directory.CreateDirectory(ConfigDirectory);

            Logger.Init(LogDirectory);
            SettingsService.Load();
            ThemeManager.Load();

            try
            {
                BrowserBridgeService.Start();
                BrowserBridgeService.Instance.ResourceReceived += OnBrowserResourceReceived;
            }
            catch (Exception ex) { Logger.Warn("BrowserBridge 启动失败: " + ex.Message); }

            _mainWindow = new MainWindow();
            _mainWindow.Activate();
        }

        private void OnBrowserResourceReceived(object? sender, Models.SniffedResource resource)
        {
            try
            {
                CurrentDispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        DownloadViewModel.Instance.AddIncomingResource(resource);
                        if (_mainWindow is MainWindow mw)
                        {
                            mw.NavigateToDownload();
                            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);
                            if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindowAsync(hwnd, 9);
                            NativeMethods.SetForegroundWindow(hwnd);
                        }
                        Logger.Info($"浏览器资源已入队：{resource.Filename} ({resource.Type})");
                    }
                    catch (Exception ex) { Logger.Warn("资源入队失败: " + ex.Message); }
                });
            }
            catch (Exception ex) { Logger.Warn("OnBrowserResourceReceived: " + ex.Message); }
        }

        public static void OnAppExit()
        {
            try
            {
                SettingsService.Save();
                ThemeManager.PersistCurrent();
            }
            catch { }
            _singleInstanceMutex?.ReleaseMutex();
        }

        private bool _showingError;
        private void OnUnhandledException(object? sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                Logger.Error("UI 线程未处理异常", e.Exception);
                if (_showingError) { e.Handled = true; return; }
                _showingError = true;
                NativeMethods.MessageBox(
                    IntPtr.Zero,
                    $"发生未处理异常：{e.Exception.Message}\n\n程序将继续运行。",
                    AppName,
                    0);
                _showingError = false;
            }
            catch { }
            finally { e.Handled = true; }
        }

        private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Error("非 UI 线程未处理异常", ex);
            }
        }
    }
}
