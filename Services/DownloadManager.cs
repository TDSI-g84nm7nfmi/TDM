using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TDM.Models;

namespace TDM.Services
{
    /// <summary>
    /// 多线程分片下载器（单任务）。
    /// </summary>
    public class ChunkedDownloader : IDisposable
    {
        private readonly DownloadItem _item;
        private readonly HttpClient _http;
        private readonly int _chunkSize;
        private readonly int _maxRetries;
        private readonly string _progressFile;

        private CancellationTokenSource? _cts;
        private Task? _mainTask;
        private long _totalSize;
        private long _downloadedBytes;
        private readonly ConcurrentDictionary<int, long> _chunkProgress = new();
        private readonly object _lock = new();
        private DateTime _lastSpeedTime = DateTime.UtcNow;
        private long _lastSpeedBytes;
        private string? _tempDir;

        public event EventHandler<long>? ProgressBytesChanged;
        public event EventHandler<double>? SpeedChanged;
        public event EventHandler? Completed;
        public event EventHandler<string>? Failed;
        public event EventHandler? Paused;
        public event EventHandler? Resumed;
        public event EventHandler<long>? SizeDetermined;

        public bool IsPaused { get; private set; }
        public bool IsStopped { get; private set; }
        public long DownloadedBytes => Interlocked.Read(ref _downloadedBytes);

        public ChunkedDownloader(DownloadItem item, HttpClient http, int chunkSize, int maxRetries)
        {
            _item = item;
            _http = http;
            _chunkSize = Math.Max(8, chunkSize) * 1024;
            _maxRetries = Math.Max(0, maxRetries);
            _progressFile = item.FilePath + ".TDM-progress";
        }

        public void Start()
        {
            if (_mainTask != null) return;
            IsStopped = false;
            _cts = new CancellationTokenSource();
            _mainTask = Task.Run(() => RunAsync(_cts.Token));
        }

        public void Pause()
        {
            if (IsPaused) return;
            IsPaused = true;
            _cts?.Cancel();
            Paused?.Invoke(this, EventArgs.Empty);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            _cts = new CancellationTokenSource();
            _mainTask = Task.Run(() => RunAsync(_cts.Token));
            Resumed?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            IsStopped = true;
            IsPaused = false;
            try { _cts?.Cancel(); } catch { }
            try { _mainTask?.Wait(2000); } catch { }
            CleanupTemp();
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private async Task RunAsync(CancellationToken outerCt)
        {
            try
            {
                // 1. 获取文件大小
                _item.Status = DownloadStatus.Connecting;
                if (!await TryDetermineSize(outerCt))
                {
                    _item.Status = DownloadStatus.Failed;
                    _item.ErrorMessage = "无法获取文件大小";
                    Failed?.Invoke(this, _item.ErrorMessage);
                    return;
                }
                SizeDetermined?.Invoke(this, _totalSize);
                _item.TotalSize = _totalSize;

                // 2. 加载已下载进度
                var progress = LoadProgress();
                if (progress == null) progress = new long[_item.Threads];

                // 3. 创建临时目录
                _tempDir = Path.Combine(Path.GetTempPath(), "TDM_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_tempDir);

                _item.Status = DownloadStatus.Downloading;
                _item.StartTime = DateTime.Now;

                // 4. 多线程分片下载
                long partSize = _totalSize / _item.Threads;
                var tasks = new List<Task>();
                using var pauseGate = new SemaphoreSlim(1, 1);
                pauseGate.Wait();

                for (int i = 0; i < _item.Threads; i++)
                {
                    long start = i * partSize;
                    long end = (i == _item.Threads - 1) ? _totalSize - 1 : (start + partSize - 1);
                    long already = progress[i];
                    int idx = i;

                    tasks.Add(Task.Run(async () =>
                    {
                        await DownloadPartAsync(idx, start, end, already, pauseGate, outerCt);
                    }));
                }

                // 启动后释放 pauseGate（除非已被暂停）
                _ = Task.Run(async () =>
                {
                    while (!outerCt.IsCancellationRequested && !IsStopped)
                    {
                        if (!IsPaused) { try { pauseGate.Release(); break; } catch { /* already released */ } }
                        await Task.Delay(100);
                    }
                });

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    Logger.Error("分片下载异常", ex);
                }

                if (IsStopped)
                {
                    _item.Status = DownloadStatus.Canceled;
                    return;
                }

                if (IsPaused)
                {
                    _item.Status = DownloadStatus.Paused;
                    return;
                }

                // 5. 检查是否所有分片完成
                long total = 0;
                for (int i = 0; i < _item.Threads; i++)
                {
                    total += _chunkProgress.GetValueOrDefault(i, progress[i]);
                }
                if (total < _totalSize)
                {
                    _item.Status = DownloadStatus.Failed;
                    _item.ErrorMessage = "分片不完整";
                    Failed?.Invoke(this, _item.ErrorMessage);
                    return;
                }

                // 6. 合并文件
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_item.FilePath) ?? ".");
                    await MergeFilesAsync(progress);
                }
                catch (Exception ex)
                {
                    _item.Status = DownloadStatus.Failed;
                    _item.ErrorMessage = $"合并文件失败: {ex.Message}";
                    Failed?.Invoke(this, _item.ErrorMessage);
                    return;
                }

                // 7. 完成
                ClearProgress();
                _item.Status = DownloadStatus.Completed;
                _item.EndTime = DateTime.Now;
                _item.Progress = 100;
                CleanupTemp();
                Completed?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                if (!IsStopped)
                {
                    _item.Status = DownloadStatus.Paused;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("下载异常", ex);
                _item.Status = DownloadStatus.Failed;
                _item.ErrorMessage = ex.Message;
                Failed?.Invoke(this, ex.Message);
            }
        }

        private async Task<bool> TryDetermineSize(CancellationToken ct)
        {
            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, _item.Url);
                    if (!string.IsNullOrEmpty(_item.Referer))
                        req.Headers.Referrer = new Uri(_item.Referer);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        _totalSize = resp.Content.Headers.ContentLength ?? 0;
                        if (_totalSize > 0) return true;
                    }
                    // 某些服务器不支持 HEAD，回退到 GET 探测
                    using var getReq = new HttpRequestMessage(HttpMethod.Get, _item.Url);
                    getReq.Headers.Range = new RangeHeaderValue(0, 0);
                    if (!string.IsNullOrEmpty(_item.Referer))
                        getReq.Headers.Referrer = new Uri(_item.Referer);
                    using var getResp = await _http.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (getResp.IsSuccessStatusCode)
                    {
                        _totalSize = getResp.Content.Headers.ContentLength
                                     ?? getResp.Content.Headers.ContentRange?.Length
                                     ?? 0;
                        return _totalSize > 0;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Warn($"探测文件大小失败(第{attempt + 1}次): {ex.Message}");
                    if (attempt >= _maxRetries) return false;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }
            return false;
        }

        private async Task DownloadPartAsync(int idx, long start, long end, long alreadyDone, SemaphoreSlim pauseGate, CancellationToken outerCt)
        {
            long actualStart = start + alreadyDone;
            if (actualStart > end) return;

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, _item.Url);
                    req.Headers.Range = new RangeHeaderValue(actualStart, end);
                    if (!string.IsNullOrEmpty(_item.Referer))
                        req.Headers.Referrer = new Uri(_item.Referer);

                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, outerCt);
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"HTTP {resp.StatusCode}");
                    }

                    var tempFile = Path.Combine(_tempDir!, $"part_{idx}");
                    using var fs = new FileStream(tempFile, FileMode.Append, FileAccess.Write, FileShare.Read, _chunkSize);

                    using var stream = await resp.Content.ReadAsStreamAsync();
                    var buffer = new byte[_chunkSize];
                    long local = alreadyDone;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, outerCt)) > 0)
                    {
                        if (IsStopped) return;
                        if (IsPaused)
                        {
                            // 等待恢复（pauseGate 会被重新等待/释放来控制）
                            try { await pauseGate.WaitAsync(CancellationToken.None); } catch { }
                            if (IsStopped) return;
                        }

                        await fs.WriteAsync(buffer, 0, read, outerCt);
                        local += read;
                        actualStart += read;
                        _chunkProgress[idx] = local;

                        long total = Interlocked.Add(ref _downloadedBytes, read);
                        ProgressBytesChanged?.Invoke(this, total);
                        _item.DownloadedBytes = total;
                        _item.Progress = _totalSize > 0 ? Math.Min(100.0, total * 100.0 / _totalSize) : 0;

                        EmitSpeed(total);
                        SaveProgress(idx, local);
                    }
                    return;
                }
                catch (OperationCanceledException)
                {
                    if (IsStopped) return;
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"分片 {idx} 下载失败(第{attempt + 1}次): {ex.Message}");
                    if (attempt >= _maxRetries)
                    {
                        throw;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), outerCt);
                }
            }
        }

        private void EmitSpeed(long total)
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastSpeedTime).TotalSeconds;
            if (elapsed >= 0.5)
            {
                var speed = (total - _lastSpeedBytes) / elapsed;
                _lastSpeedBytes = total;
                _lastSpeedTime = now;
                if (speed > 0)
                {
                    _item.Speed = speed;
                    SpeedChanged?.Invoke(this, speed);
                }
            }
        }

        private async Task MergeFilesAsync(long[] initialProgress)
        {
            // 检查目标可写
            try
            {
                using var _ = File.OpenWrite(_item.FilePath);
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("目标文件被占用，没有写入权限");
            }

            using var output = new FileStream(_item.FilePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
            for (int i = 0; i < _item.Threads; i++)
            {
                var tempFile = Path.Combine(_tempDir!, $"part_{i}");
                if (!File.Exists(tempFile))
                {
                    throw new FileNotFoundException($"分片 {i} 丢失: {tempFile}");
                }
                using var input = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
                await input.CopyToAsync(output);
            }
        }

        private long[]? LoadProgress()
        {
            try
            {
                if (!File.Exists(_progressFile)) return null;
                var json = File.ReadAllText(_progressFile);
                var data = System.Text.Json.JsonSerializer.Deserialize<ProgressData>(json);
                if (data == null || data.Timestamp + 7 * 24 * 3600 < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    return null;
                if (data.TotalSize != _totalSize || data.Progress == null || data.Progress.Length != _item.Threads)
                    return null;
                return data.Progress;
            }
            catch
            {
                return null;
            }
        }

        private void SaveProgress(int idx, long value)
        {
            try
            {
                lock (_lock)
                {
                    var progress = new long[_item.Threads];
                    for (int i = 0; i < _item.Threads; i++)
                    {
                        progress[i] = _chunkProgress.GetValueOrDefault(i, 0);
                    }
                    progress[idx] = value;
                    var data = new ProgressData
                    {
                        Progress = progress,
                        TotalSize = _totalSize,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                    File.WriteAllText(_progressFile,
                        System.Text.Json.JsonSerializer.Serialize(data));
                }
            }
            catch { /* 进度保存失败不应中断下载 */ }
        }

        private void ClearProgress()
        {
            try { if (File.Exists(_progressFile)) File.Delete(_progressFile); }
            catch { }
        }

        private void CleanupTemp()
        {
            try
            {
                if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch { }
            _tempDir = null;
        }

        private class ProgressData
        {
            public long[]? Progress { get; set; }
            public long TotalSize { get; set; }
            public long Timestamp { get; set; }
        }
    }

    /// <summary>
    /// 下载任务管理器（管理多个下载任务）。
    /// </summary>
    public class DownloadManager : IDisposable
    {
        private static readonly Lazy<DownloadManager> _instance = new(() => new DownloadManager());
        public static DownloadManager Instance => _instance.Value;

        private readonly HttpClient _http;
        private readonly ConcurrentDictionary<string, ChunkedDownloader> _downloaders = new();
        private readonly ConcurrentDictionary<string, DownloadItem> _items = new();

        public event EventHandler<DownloadItem>? ItemAdded;
        public event EventHandler<DownloadItem>? ItemRemoved;
        public event EventHandler<DownloadItem>? ItemCompleted;
        public event EventHandler<DownloadItem>? ItemFailed;

        public IReadOnlyCollection<DownloadItem> Items
        {
            get { lock (_items) { return _items.Values.ToList(); } }
        }

        public DownloadManager()
        {
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(SettingsService.Current.ConnectionTimeoutSec)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        }

        public DownloadItem? Add(string url, string saveDir, int? threads = null, int? retries = null)
            => Add(url, saveDir, threads, retries, null);

        public DownloadItem? Add(string url, string saveDir, int? threads, int? retries, string? suggestedFilename)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;

            // 检查重复
            if (_items.ContainsKey(url)) return _items[url];

            // 生成文件名
            var filename = suggestedFilename;
            if (string.IsNullOrEmpty(filename))
                filename = string.IsNullOrEmpty(uri.LocalPath) || uri.LocalPath == "/"
                    ? $"download_{DateTime.Now:yyyyMMdd_HHmmss}"
                    : Path.GetFileName(uri.LocalPath);
            filename = SanitizeFileName(filename);
            var fullPath = GetUniquePath(Path.Combine(saveDir, filename));

            try
            {
                Directory.CreateDirectory(saveDir);
            }
            catch
            {
                return null;
            }

            var item = new DownloadItem
            {
                Url = url,
                FilePath = fullPath,
                FileName = Path.GetFileName(fullPath),
                Threads = threads ?? SettingsService.Current.DefaultThreads,
                Status = DownloadStatus.Queued,
                StartTime = DateTime.Now,
            };

            _items[url] = item;
            ItemAdded?.Invoke(this, item);

            StartDownload(item, retries ?? SettingsService.Current.MaxRetries);
            return item;
        }

        public void Pause(DownloadItem item)
        {
            if (_downloaders.TryGetValue(item.Url, out var dl))
            {
                dl.Pause();
            }
        }

        public void Resume(DownloadItem item)
        {
            if (_downloaders.TryGetValue(item.Url, out var dl))
            {
                dl.Resume();
            }
        }

        public void Stop(DownloadItem item)
        {
            if (_downloaders.TryRemove(item.Url, out var dl))
            {
                dl.Stop();
                dl.Dispose();
            }
            if (_items.TryRemove(item.Url, out var it))
            {
                ItemRemoved?.Invoke(this, it);
            }
        }

        public void Remove(DownloadItem item)
        {
            Stop(item);
        }

        private void StartDownload(DownloadItem item, int retries)
        {
            var dl = new ChunkedDownloader(item, _http,
                SettingsService.Current.ChunkSizeKB, retries);

            dl.ProgressBytesChanged += (_, _) => { /* 通过 item 通知 */ };
            dl.SpeedChanged += (_, s) => item.Speed = s;
            dl.SizeDetermined += (_, sz) => item.TotalSize = sz;
            dl.Paused += (_, _) => item.Status = DownloadStatus.Paused;
            dl.Resumed += (_, _) => item.Status = DownloadStatus.Downloading;
            dl.Completed += (_, _) =>
            {
                _downloaders.TryRemove(item.Url, out ChunkedDownloader? _dl);
                ItemCompleted?.Invoke(this, item);
            };
            dl.Failed += (_, msg) =>
            {
                item.ErrorMessage = msg;
                _downloaders.TryRemove(item.Url, out ChunkedDownloader? _dl);
                ItemFailed?.Invoke(this, item);
            };

            _downloaders[item.Url] = dl;
            dl.Start();
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim().Trim('.');
            if (name.Length > 200)
            {
                var ext = Path.GetExtension(name);
                name = name.Substring(0, 200 - ext.Length) + ext;
            }
            return string.IsNullOrEmpty(name) ? $"download_{DateTime.Now:yyyyMMdd_HHmmss}" : name;
        }

        public static string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path) ?? "";
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int i = 1;
            while (File.Exists(Path.Combine(dir, $"{stem} ({i}){ext}"))) i++;
            return Path.Combine(dir, $"{stem} ({i}){ext}");
        }

        public void Dispose()
        {
            foreach (var dl in _downloaders.Values) dl.Dispose();
            _downloaders.Clear();
            _http.Dispose();
        }
    }
}
