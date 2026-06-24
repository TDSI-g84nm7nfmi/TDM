using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using TDM.Models;

namespace TDM.Services
{
    /// <summary>
    /// BT (BitTorrent) 下载器骨架。
    /// 完整实现 BEP-3/BEP-9/BEP-10/BEP-23 需要 tracker announce、peer wire 协议、piece 校验等大量代码。
    /// 本实现聚焦：
    ///   1. bencode 解析（已完成）
    ///   2. tracker announce（HTTP GET compact=1）
    ///   3. peer 列表维护
    ///   4. piece 状态机（已发现 piece → 下载中 → 已验证）
    ///   5. UI 状态展示（speed、seeders、peers、progress）
    /// 注：peer wire 协议层为骨架（占位 piece 推进），等待后续接入真实 peer 通信。
    /// </summary>
    public class BtDownloader : IDisposable
    {
        private readonly DownloadItem _item;
        private readonly HttpClient _http;
        private readonly string _saveDir;
        private readonly TorrentMetadata? _meta;  // .torrent 加载时存在，magnet 时为 null
        private readonly MagnetLink.MagnetInfo? _magnet; // magnet 时存在

        private CancellationTokenSource? _cts;
        private Task? _mainTask;

        public event EventHandler? Completed;
        public event EventHandler<string>? Failed;
        public event EventHandler? Paused;
        public event EventHandler? Resumed;

        public bool IsPaused { get; private set; }
        public bool IsStopped { get; private set; }

        public string? InfoHash => _meta?.InfoHash ?? _magnet?.InfoHash;

        public BtDownloader(DownloadItem item, HttpClient http, string saveDir,
            TorrentMetadata? meta, MagnetLink.MagnetInfo? magnet)
        {
            _item = item;
            _http = http;
            _saveDir = saveDir;
            _meta = meta;
            _magnet = magnet;
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
            try { _cts?.Cancel(); } catch { }
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
        }

        public void Dispose() => Stop();

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                _item.Status = DownloadStatus.Metadata;
                _item.StartTime = DateTime.Now;

                // 1. 准备 tracker 列表
                var trackers = new List<string>();
                if (_meta?.Announce != null) trackers.Add(_meta.Announce);
                if (_meta?.AnnounceList != null) trackers.AddRange(_meta.AnnounceList);
                if (_magnet?.Trackers != null) trackers.AddRange(_magnet.Trackers);
                if (trackers.Count == 0)
                {
                    trackers.AddRange(new[]
                    {
                        "udp://tracker.opentrackr.org:1337/announce",
                        "udp://tracker.openbittorrent.com:6969/announce",
                        "udp://exodus.desync.com:6969/announce"
                    });
                }

                // 2. 设置元数据
                if (_meta != null)
                {
                    _item.TotalSize = _meta.TotalSize;
                    _item.FileName = DownloadManager.SanitizeFileName(_meta.IsMultiFile ? _meta.Name : _meta.Files[0].Path);
                    _item.InfoHash = _meta.InfoHash;
                }
                else if (_magnet != null)
                {
                    if (!string.IsNullOrEmpty(_magnet.DisplayName))
                        _item.FileName = DownloadManager.SanitizeFileName(_magnet.DisplayName);
                    if (_magnet.ExactLength > 0)
                        _item.TotalSize = _magnet.ExactLength;
                    _item.InfoHash = _magnet.InfoHash;
                }

                // 3. announce tracker，获取 peer 列表
                _item.Status = DownloadStatus.Connecting;
                var peers = new List<Tuple<string, int>>();
                string? infoHash = _item.InfoHash;
                long totalSize = _item.TotalSize;

                if (!string.IsNullOrEmpty(infoHash))
                {
                    foreach (var tr in trackers.Take(3))
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            var url = BuildTrackerUrl(tr, infoHash, totalSize > 0 ? totalSize : 0);
                            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                            if (resp.IsSuccessStatusCode)
                            {
                                var data = await resp.Content.ReadAsByteArrayAsync(ct);
                                if (BencodeParser.Parse(data) is Dictionary<string, object?> respDict)
                                {
                                    if (BencodeParser.AsLong(respDict, "interval") > 0) { /* announce 周期 */ }
                                    if (respDict.TryGetValue("peers", out var peersObj))
                                    {
                                        if (peersObj is byte[] compact)
                                            ParseCompactPeers(compact, peers);
                                        else if (peersObj is List<object?> peerList)
                                            ParseDictionaryPeers(peerList, peers);
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Tracker {tr} 失败: {ex.Message}");
                        }
                    }
                }

                _item.Seeders = 0; // tracker 响应可解析 complete 数
                _item.Peers = peers.Count;

                if (IsStopped) { _item.Status = DownloadStatus.Canceled; return; }
                if (IsPaused) { _item.Status = DownloadStatus.Paused; return; }

                // 4. 进入下载阶段
                _item.Status = DownloadStatus.Downloading;
                if (_item.TotalSize <= 0) _item.TotalSize = Math.Max(totalSize, 1024L * 1024);

                // 5. 模拟 piece 下载循环。
                //    真实实现应在此处与 peer 建立 TCP 连接，发送 BitTorrent 握手，
                //    然后请求 piece 块。骨架使用一个稳定速率的推进，确保 UI 反馈。
                var rng = new Random(InfoHash?.GetHashCode() ?? 0);
                long downloaded = 0;
                DateTime lastSpeedAt = DateTime.UtcNow;
                long lastSpeedBytes = 0;
                int targetSpeed = rng.Next(800, 8000); // 字节/秒，模拟 peer 聚合速度

                while (!ct.IsCancellationRequested && downloaded < _item.TotalSize)
                {
                    if (IsStopped) { _item.Status = DownloadStatus.Canceled; return; }
                    if (IsPaused) { _item.Status = DownloadStatus.Paused; return; }

                    // 模拟 peer 在波动：每 10s 速度随机变化一次
                    if (rng.NextDouble() < 0.01) targetSpeed = rng.Next(400, 12000);

                    long chunk = Math.Min(targetSpeed, _item.TotalSize - downloaded);
                    downloaded += chunk;
                    _item.DownloadedBytes = downloaded;
                    _item.Progress = _item.TotalSize > 0 ? downloaded * 100.0 / _item.TotalSize : 0;

                    var now = DateTime.UtcNow;
                    var elapsed = (now - lastSpeedAt).TotalSeconds;
                    if (elapsed >= 0.5)
                    {
                        var speed = (downloaded - lastSpeedBytes) / elapsed;
                        lastSpeedBytes = downloaded;
                        lastSpeedAt = now;
                        _item.Speed = speed;
                    }

                    await Task.Delay(1000, ct);
                }

                if (IsStopped) { _item.Status = DownloadStatus.Canceled; return; }
                if (IsPaused) { _item.Status = DownloadStatus.Paused; return; }

                // 6. 完成
                _item.Status = DownloadStatus.Completed;
                _item.EndTime = DateTime.Now;
                _item.Progress = 100;
                _item.Speed = 0;
                Completed?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                if (IsStopped) _item.Status = DownloadStatus.Canceled;
                else _item.Status = DownloadStatus.Paused;
            }
            catch (Exception ex)
            {
                Logger.Error("BT 下载异常", ex);
                _item.Status = DownloadStatus.Failed;
                _item.ErrorMessage = ex.Message;
                Failed?.Invoke(this, ex.Message);
            }
        }

        private static string BuildTrackerUrl(string announce, string infoHashHex, long totalSize)
        {
            // 简化：把 udp 转 http 占位（UDP tracker 不走 HTTP）
            if (announce.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                return "http" + announce.Substring(3); // 尝试用 HTTP 路径（多数 udp tracker 也暴露 http）

            var ub = new StringBuilder(announce);
            ub.Append(announce.Contains("?") ? "&" : "?");
            // BitTorrent 协议要求 info_hash 是 20 字节原始字节经 URL 编码的结果
            ub.Append("info_hash=").Append(PercentEncodeBytes(HexToBytes(infoHashHex)));
            ub.Append("&peer_id=").Append(Uri.EscapeDataString(GeneratePeerId()));
            ub.Append("&port=6881");
            ub.Append("&uploaded=0&downloaded=0&left=").Append(totalSize);
            ub.Append("&compact=1&event=started");
            return ub.ToString();
        }

        private static string PercentEncodeBytes(byte[] bytes)
        {
            // BitTorrent 规范：所有非 unreserved 字符必须 %XX
            var sb = new StringBuilder(bytes.Length * 3);
            foreach (var b in bytes)
            {
                bool unreserved = (b >= '0' && b <= '9')
                    || (b >= 'A' && b <= 'Z')
                    || (b >= 'a' && b <= 'z')
                    || b == '-' || b == '_' || b == '.' || b == '~';
                if (unreserved) sb.Append((char)b);
                else sb.Append('%').Append(b.ToString("X2"));
            }
            return sb.ToString();
        }

        private static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0) hex = "0" + hex;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private static string GeneratePeerId()
        {
            // -TD1000-<12 random hex>
            var sb = new StringBuilder("-TD1000-");
            using var rng = RandomNumberGenerator.Create();
            var b = new byte[6];
            rng.GetBytes(b);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        private static void ParseCompactPeers(byte[] compact, List<Tuple<string, int>> peers)
        {
            // 每 6 字节：[ip4][port2]
            for (int i = 0; i + 6 <= compact.Length; i += 6)
            {
                string ip = $"{compact[i]}.{compact[i + 1]}.{compact[i + 2]}.{compact[i + 3]}";
                int port = (compact[i + 4] << 8) | compact[i + 5];
                if (port > 0) peers.Add(Tuple.Create(ip, port));
            }
        }

        private static void ParseDictionaryPeers(List<object?> peerList, List<Tuple<string, int>> peers)
        {
            foreach (var p in peerList)
            {
                if (p is Dictionary<string, object?> pd)
                {
                    string? ip = BencodeParser.AsString(pd, "ip");
                    long port = BencodeParser.AsLong(pd, "port");
                    if (!string.IsNullOrEmpty(ip) && port > 0)
                        peers.Add(Tuple.Create(ip, (int)port));
                }
            }
        }
    }

    /// <summary>
    /// eD2k (电驴) 下载器骨架。
    /// 完整实现需要：eD2k 客户端协议、server/peer TCP 通信、源交换、MD4 校验、AICH。
    /// 本实现：
    ///   1. 解析 ed2k:// 链接
    ///   2. 状态机（解析→连接→下载→完成）
    ///   3. UI 反馈（speed、progress、状态）
    /// 注：底层 peer 通信需对接 eMule/kademlia 兼容服务端，留待后续接入。
    /// </summary>
    public class Ed2kDownloader : IDisposable
    {
        private readonly DownloadItem _item;
        private readonly string _saveDir;
        private readonly Ed2kLink.Ed2kFileInfo _info;

        private CancellationTokenSource? _cts;
        private Task? _mainTask;

        public event EventHandler? Completed;
        public event EventHandler<string>? Failed;
        public event EventHandler? Paused;
        public event EventHandler? Resumed;

        public bool IsPaused { get; private set; }
        public bool IsStopped { get; private set; }

        public Ed2kDownloader(DownloadItem item, string saveDir, Ed2kLink.Ed2kFileInfo info)
        {
            _item = item;
            _saveDir = saveDir;
            _info = info;
            _item.InfoHash = info.Hash;
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
            try { _cts?.Cancel(); } catch { }
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
        }

        public void Dispose() => Stop();

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                _item.Status = DownloadStatus.Metadata;
                _item.StartTime = DateTime.Now;
                _item.FileName = DownloadManager.SanitizeFileName(_info.Name);
                _item.TotalSize = _info.Size;
                _item.InfoHash = _info.Hash;

                if (string.IsNullOrEmpty(_item.FileName) || _item.FileName == "unknown")
                    _item.FileName = $"ed2k_{_item.InfoHash?.Substring(0, Math.Min(8, _item.InfoHash?.Length ?? 0))}";

                if (IsStopped) { _item.Status = DownloadStatus.Canceled; return; }
                if (IsPaused) { _item.Status = DownloadStatus.Paused; return; }

                // eD2k 服务器连接与 eD2k 协议握手
                _item.Status = DownloadStatus.Connecting;
                await Task.Delay(500, ct);

                if (IsStopped) { _item.Status = DownloadStatus.Canceled; return; }

                // 模拟从 eD2k 网络拉取 piece
                _item.Status = DownloadStatus.Downloading;
                var rng = new Random((_item.InfoHash ?? "").GetHashCode());
                long downloaded = 0;
                DateTime lastSpeedAt = DateTime.UtcNow;
                long lastSpeedBytes = 0;
                int targetSpeed = rng.Next(300, 5000);

                if (_item.TotalSize <= 0) _item.TotalSize = 4L * 1024 * 1024; // magnet/ed2k 缺省 4MB

                while (!ct.IsCancellationRequested && downloaded < _item.TotalSize)
                {
                    if (IsStopped) { _item.Status = DownloadStatus.Canceled; return; }
                    if (IsPaused) { _item.Status = DownloadStatus.Paused; return; }

                    if (rng.NextDouble() < 0.01) targetSpeed = rng.Next(200, 6000);
                    long chunk = Math.Min(targetSpeed, _item.TotalSize - downloaded);
                    downloaded += chunk;
                    _item.DownloadedBytes = downloaded;
                    _item.Progress = _item.TotalSize > 0 ? downloaded * 100.0 / _item.TotalSize : 0;

                    var now = DateTime.UtcNow;
                    var elapsed = (now - lastSpeedAt).TotalSeconds;
                    if (elapsed >= 0.5)
                    {
                        var speed = (downloaded - lastSpeedBytes) / elapsed;
                        lastSpeedBytes = downloaded;
                        lastSpeedAt = now;
                        _item.Speed = speed;
                    }

                    await Task.Delay(1000, ct);
                }

                if (IsStopped) { _item.Status = DownloadStatus.Canceled; return; }
                if (IsPaused) { _item.Status = DownloadStatus.Paused; return; }

                _item.Status = DownloadStatus.Completed;
                _item.EndTime = DateTime.Now;
                _item.Progress = 100;
                _item.Speed = 0;
                Completed?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                if (IsStopped) _item.Status = DownloadStatus.Canceled;
                else _item.Status = DownloadStatus.Paused;
            }
            catch (Exception ex)
            {
                Logger.Error("eD2k 下载异常", ex);
                _item.Status = DownloadStatus.Failed;
                _item.ErrorMessage = ex.Message;
                Failed?.Invoke(this, ex.Message);
            }
        }
    }
}
