using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TDM.Models
{
    public enum DownloadStatus
    {
        Idle,
        Queued,
        Connecting,
        Metadata,    // BT/ED2K 等需要元数据
        Downloading,
        Paused,
        Completed,
        Failed,
        Canceled
    }

    public enum DownloadProtocol
    {
        Http,
        Bt,
        Ed2k
    }

    public class DownloadItem : INotifyPropertyChanged
    {
        private string _url = string.Empty;
        private string _filePath = string.Empty;
        private string _fileName = string.Empty;
        private long _totalSize;
        private long _downloadedBytes;
        private double _progress; // 0-100
        private double _speed;    // bytes/sec
        private DownloadStatus _status = DownloadStatus.Idle;
        private DownloadProtocol _protocol = DownloadProtocol.Http;
        private string? _errorMessage;
        private DateTime _startTime = DateTime.Now;
        private DateTime? _endTime;
        private int _threads = 4;
        private int _retryCount;
        private string? _referer;
        private int _seeders;
        private int _peers;
        private string? _infoHash;

        public string Url
        {
            get => _url;
            set { if (_url != value) { _url = value; OnPropertyChanged(); } }
        }

        public string FilePath
        {
            get => _filePath;
            set { if (_filePath != value) { _filePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileName)); } }
        }

        public string FileName
        {
            get => !string.IsNullOrEmpty(_fileName) ? _fileName : System.IO.Path.GetFileName(_filePath);
            set { if (_fileName != value) { _fileName = value; OnPropertyChanged(); } }
        }

        public long TotalSize
        {
            get => _totalSize;
            set { if (_totalSize != value) { _totalSize = value; OnPropertyChanged(); } }
        }

        public long DownloadedBytes
        {
            get => _downloadedBytes;
            set { if (_downloadedBytes != value) { _downloadedBytes = value; OnPropertyChanged(); } }
        }

        public double Progress
        {
            get => _progress;
            set { if (Math.Abs(_progress - value) > 0.0001) { _progress = value; OnPropertyChanged(); } }
        }

        public double Speed
        {
            get => _speed;
            set { if (Math.Abs(_speed - value) > 0.0001) { _speed = value; OnPropertyChanged(); } }
        }

        public DownloadStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public DownloadProtocol Protocol
        {
            get => _protocol;
            set
            {
                if (_protocol != value)
                {
                    _protocol = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProtocolText));
                }
            }
        }

        public string ProtocolText => _protocol switch
        {
            DownloadProtocol.Http => "HTTP",
            DownloadProtocol.Bt => "BT",
            DownloadProtocol.Ed2k => "eD2k",
            _ => "?"
        };

        public string StatusText => _status switch
        {
            DownloadStatus.Idle => "等待中",
            DownloadStatus.Queued => "队列中",
            DownloadStatus.Connecting => "连接中",
            DownloadStatus.Metadata => "解析元数据",
            DownloadStatus.Downloading => "下载中",
            DownloadStatus.Paused => "已暂停",
            DownloadStatus.Completed => "完成",
            DownloadStatus.Failed => "失败",
            DownloadStatus.Canceled => "已取消",
            _ => "未知"
        };

        public string? ErrorMessage
        {
            get => _errorMessage;
            set { if (_errorMessage != value) { _errorMessage = value; OnPropertyChanged(); } }
        }

        public DateTime StartTime
        {
            get => _startTime;
            set { if (_startTime != value) { _startTime = value; OnPropertyChanged(); } }
        }

        public DateTime? EndTime
        {
            get => _endTime;
            set { if (_endTime != value) { _endTime = value; OnPropertyChanged(); } }
        }

        public int Threads
        {
            get => _threads;
            set { if (_threads != value) { _threads = value; OnPropertyChanged(); } }
        }

        public int RetryCount
        {
            get => _retryCount;
            set { if (_retryCount != value) { _retryCount = value; OnPropertyChanged(); } }
        }

        public string? Referer
        {
            get => _referer;
            set { if (_referer != value) { _referer = value; OnPropertyChanged(); } }
        }

        public int Seeders
        {
            get => _seeders;
            set { if (_seeders != value) { _seeders = value; OnPropertyChanged(); } }
        }

        public int Peers
        {
            get => _peers;
            set { if (_peers != value) { _peers = value; OnPropertyChanged(); } }
        }

        public string? InfoHash
        {
            get => _infoHash;
            set { if (_infoHash != value) { _infoHash = value; OnPropertyChanged(); } }
        }

        public TimeSpan Elapsed => (EndTime ?? DateTime.Now) - StartTime;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
