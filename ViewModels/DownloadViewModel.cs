using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using TDM.Models;
using TDM.Services;

namespace TDM.ViewModels
{
    public class DownloadViewModel : ObservableObject
    {
        private string _url = string.Empty;
        private string _savePath = string.Empty;
        private int _threads = 4;
        private int _retries = 3;
        private DownloadItem? _selectedItem;
        private bool _isSniffing;
        private string _sniffStatus = string.Empty;

        public ObservableCollection<DownloadItem> ActiveItems { get; } = new();
        public ObservableCollection<SniffedResource> Resources { get; } = new();

        public string Url
        {
            get => _url;
            set
            {
                if (SetProperty(ref _url, value))
                {
                    OnUrlChanged();
                    ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string SavePath
        {
            get => _savePath;
            set => SetProperty(ref _savePath, value);
        }

        public int Threads
        {
            get => _threads;
            set => SetProperty(ref _threads, value);
        }

        public int Retries
        {
            get => _retries;
            set => SetProperty(ref _retries, value);
        }

        public DownloadItem? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        public bool IsSniffing
        {
            get => _isSniffing;
            set { if (SetProperty(ref _isSniffing, value)) ((RelayCommand)SniffCommand).RaiseCanExecuteChanged(); }
        }

        public string SniffStatus
        {
            get => _sniffStatus;
            set => SetProperty(ref _sniffStatus, value);
        }

        public ICommand StartCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand ResumeCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand SniffCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand BrowseResourceCommand { get; }
        public ICommand OpenTorrentCommand { get; }
        public RelayCommand OpenTorrentFileCommand { get; }

        public static DownloadViewModel Instance { get; } = new DownloadViewModel();

        public DownloadViewModel()
        {
            SavePath = SettingsService.Current.DefaultSaveDir;
            Threads = SettingsService.Current.DefaultThreads;
            Retries = SettingsService.Current.MaxRetries;

            StartCommand = new RelayCommand(_ => Start(), _ => CanStart());
            PauseCommand = new RelayCommand(_ => Pause(), _ => SelectedItem?.Status == DownloadStatus.Downloading);
            ResumeCommand = new RelayCommand(_ => Resume(), _ => SelectedItem?.Status == DownloadStatus.Paused);
            StopCommand = new RelayCommand(_ => Stop(), _ => SelectedItem != null);
            RemoveCommand = new RelayCommand(_ => Remove(), _ => SelectedItem != null);
            SniffCommand = new RelayCommand(_ => StartSniff(), _ => CanStart() && !IsSniffing);
            OpenFolderCommand = new RelayCommand(_ => OpenSelectedFolder(),
                _ => SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.FilePath));
            BrowseResourceCommand = new RelayCommand(p => StartFromResource(p as SniffedResource),
                p => p is SniffedResource);
            OpenTorrentCommand = new RelayCommand(_ => OpenTorrentDialog());
            OpenTorrentFileCommand = new RelayCommand(p => LoadTorrentFile(p as string));

            DownloadManager.Instance.ItemAdded += OnItemAdded;
            DownloadManager.Instance.ItemRemoved += OnItemRemoved;
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SelectedItem))
                {
                    ((RelayCommand)PauseCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ResumeCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)RemoveCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)OpenFolderCommand).RaiseCanExecuteChanged();
                }
            };
        }

        /// <summary>
        /// URL 变化时检测协议类型，给用户提示。
        /// </summary>
        private void OnUrlChanged()
        {
            if (string.IsNullOrWhiteSpace(_url)) return;
            var lower = _url.Trim().ToLowerInvariant();
            if (lower.StartsWith("magnet:"))
                SniffStatus = "检测到 magnet 链接，点击开始下载即可加入 BT 任务";
            else if (lower.StartsWith("ed2k://"))
                SniffStatus = "检测到 eD2k 链接，点击开始下载即可加入电驴任务";
        }

        public bool CanStart()
        {
            if (string.IsNullOrWhiteSpace(Url)) return false;
            var trimmed = Url.Trim();
            // 支持 http/https、magnet:、ed2k://
            if (trimmed.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("ed2k://", StringComparison.OrdinalIgnoreCase)) return true;
            return ClipboardMonitor.IsUrl(Url);
        }

        public void Start()
        {
            if (!CanStart()) return;
            var url = Url.Trim();
            DownloadManager.Instance.Add(url, SavePath, Threads, Retries);
            Url = string.Empty;
        }

        public void Pause()
        {
            if (SelectedItem != null) DownloadManager.Instance.Pause(SelectedItem);
        }

        public void Resume()
        {
            if (SelectedItem != null) DownloadManager.Instance.Resume(SelectedItem);
        }

        public void Stop()
        {
            if (SelectedItem != null) DownloadManager.Instance.Stop(SelectedItem);
        }

        public void Remove()
        {
            if (SelectedItem != null)
            {
                DownloadManager.Instance.Remove(SelectedItem);
                SelectedItem = null;
            }
        }

        /// <summary>
        /// 浏览器扩展 / native host 推过来的资源
        /// </summary>
        public void AddIncomingResource(SniffedResource resource)
        {
            if (resource == null) return;
            if (Resources.Any(r => r.Url == resource.Url)) return;
            Resources.Insert(0, resource);
            SniffStatus = $"已接收 {Resources.Count} 个资源";

            try
            {
                Url = resource.Url;
                StartFromResource(resource);
            }
            catch (Exception ex)
            {
                Logger.Warn("自动下载资源失败: " + ex.Message);
            }
        }

        public void OpenSelectedFolder()
        {
            if (SelectedItem == null || string.IsNullOrEmpty(SelectedItem.FilePath)) return;
            var dir = System.IO.Path.GetDirectoryName(SelectedItem.FilePath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start("explorer.exe", dir);
            }
        }

        public void StartFromResource(SniffedResource? res)
        {
            if (res == null || string.IsNullOrEmpty(res.Url)) return;
            if (!string.IsNullOrEmpty(res.Filename))
                DownloadManager.Instance.Add(res.Url, SavePath, Threads, Retries, res.Filename);
            else
                DownloadManager.Instance.Add(res.Url, SavePath, Threads, Retries);
        }

        public void AddBrowserResource(SniffedResource resource)
        {
            if (Resources.Any(r => r.Url == resource.Url)) return;
            Resources.Add(resource);
        }

        public void TogglePause()
        {
            if (SelectedItem == null) return;
            if (SelectedItem.Status == DownloadStatus.Downloading) Pause();
            else if (SelectedItem.Status == DownloadStatus.Paused) Resume();
        }

        /// <summary>
        /// 弹出文件选择对话框，加载 .torrent 文件。
        /// </summary>
        public void OpenTorrentDialog()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "选择 BT 种子文件",
                    Filter = "BT 种子文件 (*.torrent)|*.torrent|所有文件 (*.*)|*.*",
                    Multiselect = true
                };
                if (dlg.ShowDialog() == true)
                {
                    foreach (var f in dlg.FileNames)
                    {
                        LoadTorrentFile(f);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("打开种子文件失败", ex);
            }
        }

        /// <summary>
        /// 加载并添加 .torrent 任务。
        /// </summary>
        public void LoadTorrentFile(string? path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
            try
            {
                DownloadManager.Instance.AddTorrentFile(path, SavePath);
            }
            catch (Exception ex)
            {
                Logger.Error("加载种子失败: " + path, ex);
            }
        }

        #region 嗅探
        private ResourceSniffer? _sniffer;

        public void StartSniff()
        {
            if (IsSniffing || !CanStart()) return;
            Resources.Clear();
            IsSniffing = true;
            SniffStatus = "嗅探中…";

            _sniffer = new ResourceSniffer(Url);
            _sniffer.ResourceFound += (_, r) =>
            {
                App.CurrentDispatcher.Invoke(() => Resources.Add(r));
            };
            _sniffer.Completed += (_, _) =>
            {
                App.CurrentDispatcher.Invoke(() =>
                {
                    IsSniffing = false;
                    SniffStatus = Resources.Count == 0 ? "未发现可下载资源" : $"找到 {Resources.Count} 个资源";
                });
            };
            _sniffer.Failed += (_, msg) =>
            {
                App.CurrentDispatcher.Invoke(() =>
                {
                    IsSniffing = false;
                    SniffStatus = $"嗅探失败：{msg}";
                });
            };
            _sniffer.StatusChanged += (_, msg) =>
            {
                App.CurrentDispatcher.Invoke(() => SniffStatus = msg);
            };
            _sniffer.Start();
        }

        public void StopSniff()
        {
            if (_sniffer != null)
            {
                _sniffer.Stop();
                _sniffer = null;
            }
            IsSniffing = false;
        }
        #endregion

        private void OnItemAdded(object? sender, DownloadItem item)
        {
            App.CurrentDispatcher.Invoke(() => ActiveItems.Add(item));
        }

        private void OnItemRemoved(object? sender, DownloadItem item)
        {
            App.CurrentDispatcher.Invoke(() => ActiveItems.Remove(item));
        }
    }
}
