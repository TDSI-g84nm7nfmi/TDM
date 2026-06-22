using System;
using System.Windows.Input;
using TDM.Services;

namespace TDM.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private string _selectedTheme = ThemeManager.Blue;
        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (SetProperty(ref _selectedTheme, value))
                {
                    ThemeManager.Set(value);
                    ThemeChanged?.Invoke(this, value);
                }
            }
        }

        public string[] AvailableThemes { get; } = { "purple", "blue", "green", "dark", "pink" };

        private string _defaultSaveDir = SettingsService.Current.DefaultSaveDir;
        public string DefaultSaveDir
        {
            get => _defaultSaveDir;
            set
            {
                if (SetProperty(ref _defaultSaveDir, value))
                {
                    SettingsService.Update(s => s.DefaultSaveDir = value);
                    SettingsService.Save();
                }
            }
        }

        private int _defaultThreads = SettingsService.Current.DefaultThreads;
        public int DefaultThreads
        {
            get => _defaultThreads;
            set
            {
                if (SetProperty(ref _defaultThreads, value))
                {
                    SettingsService.Update(s => s.DefaultThreads = value);
                    SettingsService.Save();
                }
            }
        }

        private int _maxRetries = SettingsService.Current.MaxRetries;
        public int MaxRetries
        {
            get => _maxRetries;
            set
            {
                if (SetProperty(ref _maxRetries, value))
                {
                    SettingsService.Update(s => s.MaxRetries = value);
                    SettingsService.Save();
                }
            }
        }

        private int _chunkSizeKB = SettingsService.Current.ChunkSizeKB;
        public int ChunkSizeKB
        {
            get => _chunkSizeKB;
            set
            {
                if (SetProperty(ref _chunkSizeKB, value))
                {
                    SettingsService.Update(s => s.ChunkSizeKB = value);
                    SettingsService.Save();
                }
            }
        }

        private bool _enableClipboard = SettingsService.Current.EnableClipboard;
        public bool EnableClipboard
        {
            get => _enableClipboard;
            set
            {
                if (SetProperty(ref _enableClipboard, value))
                {
                    SettingsService.Update(s => s.EnableClipboard = value);
                    SettingsService.Save();
                }
            }
        }

        private bool _minimizeToTray = SettingsService.Current.MinimizeToTray;
        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set
            {
                if (SetProperty(ref _minimizeToTray, value))
                {
                    SettingsService.Update(s => s.MinimizeToTray = value);
                    SettingsService.Save();
                }
            }
        }

        private bool _notifyFinished = SettingsService.Current.NotifyFinished;
        public bool NotifyFinished
        {
            get => _notifyFinished;
            set
            {
                if (SetProperty(ref _notifyFinished, value))
                {
                    SettingsService.Update(s => s.NotifyFinished = value);
                    SettingsService.Save();
                }
            }
        }

        private bool _notifyError = SettingsService.Current.NotifyError;
        public bool NotifyError
        {
            get => _notifyError;
            set
            {
                if (SetProperty(ref _notifyError, value))
                {
                    SettingsService.Update(s => s.NotifyError = value);
                    SettingsService.Save();
                }
            }
        }

        private bool _acrylicBlur = SettingsService.Current.AcrylicBlur;
        public bool AcrylicBlur
        {
            get => _acrylicBlur;
            set
            {
                if (SetProperty(ref _acrylicBlur, value))
                {
                    SettingsService.Update(s => s.AcrylicBlur = value);
                    SettingsService.Save();
                    BlurToggleRequested?.Invoke(this, value);
                }
            }
        }

        private bool _scanBrowsersOnStartup = SettingsService.Current.ScanBrowsersOnStartup;
        public bool ScanBrowsersOnStartup
        {
            get => _scanBrowsersOnStartup;
            set
            {
                if (SetProperty(ref _scanBrowsersOnStartup, value))
                {
                    SettingsService.Update(s => s.ScanBrowsersOnStartup = value);
                    SettingsService.Save();
                }
            }
        }

        private string _closeAction = SettingsService.Current.CloseAction ?? "ask";
        public string CloseAction
        {
            get => _closeAction;
            set
            {
                if (SetProperty(ref _closeAction, value))
                {
                    SettingsService.Update(s => s.CloseAction = value);
                    SettingsService.Save();
                }
            }
        }

        public ICommand BrowseCommand { get; }
        public ICommand OpenDataFolderCommand { get; }
        public ICommand OpenLogFolderCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand ResetSettingsCommand { get; }

        public event EventHandler<string>? ThemeChanged;
        public event EventHandler<bool>? BlurToggleRequested;

        public SettingsViewModel()
        {
            BrowseCommand = new RelayCommand(_ => Browse());
            OpenDataFolderCommand = new RelayCommand(_ => OpenFolder(App.DataDirectory));
            OpenLogFolderCommand = new RelayCommand(_ => OpenFolder(App.LogDirectory));
            ClearHistoryCommand = new RelayCommand(_ => ClearHistory());
            ResetSettingsCommand = new RelayCommand(_ => Reset());
        }

        private void Browse()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = DefaultSaveDir,
                Description = "选择默认下载目录"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                DefaultSaveDir = dlg.SelectedPath;
            }
        }

        private void OpenFolder(string path)
        {
            if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", path);
        }

        private void ClearHistory()
        {
            var result = System.Windows.MessageBox.Show("确定要清空所有下载历史吗？此操作不可恢复。",
                "清空历史", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                HistoryService.Clear();
                HistoryService.Save();
            }
        }

        private void Reset()
        {
            var result = System.Windows.MessageBox.Show("重置所有设置为默认值？", "重置设置",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.Yes) return;
            SettingsService.Update(s =>
            {
                s.Theme = "purple";
                s.DefaultSaveDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TDM", "Downloads");
                s.DefaultThreads = 4;
                s.MaxRetries = 3;
                s.ChunkSizeKB = 64;
                s.EnableClipboard = true;
                s.MinimizeToTray = true;
                s.NotifyFinished = true;
                s.NotifyError = true;
                s.AcrylicBlur = false;
                s.ScanBrowsersOnStartup = true;
            });
            SettingsService.Save();

            DefaultSaveDir = SettingsService.Current.DefaultSaveDir;
            DefaultThreads = SettingsService.Current.DefaultThreads;
            MaxRetries = SettingsService.Current.MaxRetries;
            ChunkSizeKB = SettingsService.Current.ChunkSizeKB;
            EnableClipboard = SettingsService.Current.EnableClipboard;
            MinimizeToTray = SettingsService.Current.MinimizeToTray;
            NotifyFinished = SettingsService.Current.NotifyFinished;
            NotifyError = SettingsService.Current.NotifyError;
            AcrylicBlur = SettingsService.Current.AcrylicBlur;
            ScanBrowsersOnStartup = SettingsService.Current.ScanBrowsersOnStartup;
            SelectedTheme = "blue";
        }
    }
}
