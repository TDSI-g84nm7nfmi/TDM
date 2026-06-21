using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TDM.Models;

namespace TDM.Services
{
    /// <summary>
    /// 网页资源嗅探器。
    /// 通过模拟真实浏览器（Headers、Accept-Encoding、Referer）抓取 HTML / JSON，
    /// 提取图片、视频、音频、文档等可下载资源。
    /// </summary>
    public class ResourceSniffer
    {
        private static readonly HttpClient SharedHttp = CreateSharedHttpClient();
        private readonly string _url;
        private CancellationTokenSource? _cts;

        public event EventHandler<SniffedResource>? ResourceFound;
        public event EventHandler? Completed;
        public event EventHandler<string>? Failed;
        public event EventHandler<string>? StatusChanged;

        public ResourceSniffer(string url)
        {
            _url = url;
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip
                                       | DecompressionMethods.Deflate
                                       | DecompressionMethods.Brotli,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 8,
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                ClientCertificateOptions = ClientCertificateOption.Automatic
            };

            var http = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 TDM/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp," +
                "image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8,en-US;q=0.7");
            http.DefaultRequestHeaders.Add("Sec-Ch-Ua",
                "\"Chromium\";v=\"124\", \"Not-A.Brand\";v=\"99\", \"Microsoft Edge\";v=\"124\"");
            http.DefaultRequestHeaders.Add("Sec-Ch-Ua-Mobile", "?0");
            http.DefaultRequestHeaders.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
            http.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            http.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            http.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            http.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            http.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            return http;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => RunAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                StatusChanged?.Invoke(this, "嗅探中…");

                if (!Uri.TryCreate(_url, UriKind.Absolute, out var baseUri)
                    || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
                {
                    Failed?.Invoke(this, "URL 无效，仅支持 http/https");
                    return;
                }

                var (html, contentType, finalUri) = await FetchAsync(_url, ct);
                if (html == null)
                {
                    Failed?.Invoke(this, "无法获取网页内容（可能被网站拒绝或网络不可达）");
                    return;
                }

                if (finalUri != null) baseUri = finalUri;

                // 仅返回了 JSON / 非 HTML，直接按 JSON 嗅探
                var isJson = !string.IsNullOrEmpty(contentType)
                    && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

                if (isJson)
                {
                    ScanJson(html, baseUri, found);
                }
                else
                {
                    ScanHtml(html, baseUri, found);
                }

                // 兜底：如果扫描完什么都没找到，把原始 URL 当作下载资源
                if (found.Count == 0 && !string.IsNullOrEmpty(_url))
                {
                    var url = finalUri?.AbsoluteUri ?? _url;
                    if (!found.Contains(url) && LooksLikeDirectFile(url, contentType))
                    {
                        var filename = Path.GetFileName(new Uri(url).LocalPath);
                        if (string.IsNullOrEmpty(filename))
                            filename = $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
                        ResourceFound?.Invoke(this, new SniffedResource
                        {
                            Url = url,
                            Type = GuessTypeFromUrl(url, contentType),
                            Filename = DownloadManager.SanitizeFileName(filename)
                        });
                        found.Add(url);
                    }
                }

                Completed?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                Failed?.Invoke(this, "已取消");
            }
            catch (Exception ex)
            {
                Logger.Error("嗅探失败", ex);
                Failed?.Invoke(this, $"嗅探失败：{ex.Message}");
            }
        }

        private async Task<(string? html, string contentType, Uri? finalUri)> FetchAsync(
            string url, CancellationToken ct)
        {
            // 第 1 次：带 Referer
            var referer = SafeOrigin(url);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(referer))
                req.Headers.Referrer = new Uri(referer);

            using var resp = await SharedHttp.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var finalUri = resp.RequestMessage?.RequestUri;

            // 文件直接下载
            var dispo = resp.Content.Headers.ContentDisposition;
            var cdFileName = dispo?.FileName?.Trim('"');
            if (!string.IsNullOrEmpty(cdFileName)
                || LooksLikeDirectFile(finalUri?.AbsoluteUri ?? url, contentType))
            {
                var single = new SniffedResource
                {
                    Url = finalUri?.AbsoluteUri ?? url,
                    Type = GuessTypeFromUrl(finalUri?.AbsoluteUri ?? url, contentType),
                    Filename = cdFileName
                        ?? Path.GetFileName(new Uri(finalUri?.AbsoluteUri ?? url).LocalPath)
                };
                if (string.IsNullOrEmpty(single.Filename))
                    single.Filename = $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
                single.Filename = DownloadManager.SanitizeFileName(single.Filename);
                ResourceFound?.Invoke(this, single);
                return (string.Empty, contentType, finalUri);
            }

            if (!resp.IsSuccessStatusCode)
            {
                Logger.Warn($"嗅探 HTTP {(int)resp.StatusCode}: {url}");
                return (null, contentType, finalUri);
            }

            // 仅当响应是文本类型时才读取为字符串
            if (!string.IsNullOrEmpty(contentType)
                && !contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                && !contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
                && !contentType.Contains("text", StringComparison.OrdinalIgnoreCase)
                && !contentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                // 其它二进制类型
                return (string.Empty, contentType, finalUri);
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var html = TryDecodeToString(bytes, contentType);

            // 第一次请求失败时，尝试不带自定义 Header 的简易 GET 请求作为回退
            // 某些网站的反爬机制会拒绝带有过多 Header 的请求
            if (string.IsNullOrEmpty(html) && !ct.IsCancellationRequested)
            {
                try
                {
                    using var fallbackReq = new HttpRequestMessage(HttpMethod.Get, url);
                    fallbackReq.Headers.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                    using var fallbackResp = await SharedHttp.SendAsync(fallbackReq, HttpCompletionOption.ResponseContentRead, ct);
                    if (fallbackResp.IsSuccessStatusCode)
                    {
                        var fbBytes = await fallbackResp.Content.ReadAsByteArrayAsync(ct);
                        html = TryDecodeToString(fbBytes, fallbackResp.Content.Headers.ContentType?.MediaType ?? "");
                        if (!string.IsNullOrEmpty(html))
                        {
                            contentType = fallbackResp.Content.Headers.ContentType?.MediaType ?? contentType;
                            finalUri ??= fallbackResp.RequestMessage?.RequestUri;
                        }
                    }
                }
                catch
                {
                    // 忽略回退失败
                }
            }

            return (html, contentType, finalUri);
        }

        private static string TryDecodeToString(byte[] bytes, string contentType)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            Encoding? encoding = null;
            var charset = ExtractCharset(contentType);
            if (!string.IsNullOrEmpty(charset))
            {
                try { encoding = Encoding.GetEncoding(charset); } catch { }
            }
            encoding ??= Encoding.UTF8;
            try
            {
                return encoding.GetString(bytes);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }

        private static string? ExtractCharset(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return null;
            var m = Regex.Match(contentType, @"charset\s*=\s*[""']?([^;""'>\s]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string? SafeOrigin(string url)
        {
            try
            {
                var u = new Uri(url);
                return $"{u.Scheme}://{u.Host}/";
            }
            catch
            {
                return null;
            }
        }

        private static bool LooksLikeDirectFile(string url, string contentType)
        {
            if (string.IsNullOrEmpty(url)) return false;
            var u = url.ToLowerInvariant();
            if (Regex.IsMatch(u, @"\.(zip|rar|7z|tar|gz|exe|pdf|docx?|xlsx?|pptx?|apk|iso|mp4|webm|mkv|mov|mp3|wav|flac|m4a|jpg|jpeg|png|gif|webp|ts|m3u8|flv)(\?|#|$)"))
                return true;
            if (!string.IsNullOrEmpty(contentType)
                && (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                    || contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                    || contentType == "application/zip"
                    || contentType == "application/x-rar-compressed"
                    || contentType == "application/pdf"
                    || contentType == "application/octet-stream"))
                return true;
            return false;
        }

        private void ScanHtml(string html, Uri baseUri, HashSet<string> found)
        {
            // === 阶段 1: HTML 标签 ===
            // 图片
            AddAll(Regex.Matches(html, @"<img[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "image");
            AddAll(Regex.Matches(html, @"data-src=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "image");
            AddAll(Regex.Matches(html, @"data-original=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "image");
            AddAll(Regex.Matches(html, @"data-lazy-src=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "image");
            AddAll(Regex.Matches(html, @"srcset=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "image");
            AddAll(Regex.Matches(html, @"background-image\s*:\s*url\([""']?([^)""']+)[""']?\)", RegexOptions.IgnoreCase),
                baseUri, found, "image");

            // 视频
            AddAll(Regex.Matches(html, @"<video[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "video");
            AddAll(Regex.Matches(html, @"<source[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "video");
            AddAll(Regex.Matches(html, @"poster=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "image");

            // 音频
            AddAll(Regex.Matches(html, @"<audio[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "audio");
            AddAll(Regex.Matches(html, @"<source[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
                baseUri, found, "audio");

            // 常见文件链接
            AddAll(Regex.Matches(html,
                @"href=[""']([^""']+\.(?:zip|rar|7z|tar|gz|exe|pdf|docx?|xlsx?|pptx?|apk|iso|mp4|webm|mkv|mov|mp3|wav|flac|m4a|ts|m3u8|flv))[""']",
                RegexOptions.IgnoreCase), baseUri, found, "file");

            // HLS playlist
            AddAll(Regex.Matches(html, @"(https?://[^\s""'<>]+\.m3u8[^\s""'<>]*)", RegexOptions.IgnoreCase),
                baseUri, found, "video");

            // === 阶段 2: Open Graph / Twitter Card Meta ===
            AddAll(Regex.Matches(html,
                @"<meta[^>]+property=""(?:og|twitter):(?:image|video|audio)""[^>]+content=""([^""]+)""",
                RegexOptions.IgnoreCase), baseUri, found, "image");
            AddAll(Regex.Matches(html,
                @"<meta[^>]+content=""([^""]+)""[^>]+property=""(?:og|twitter):(?:image|video|audio)""",
                RegexOptions.IgnoreCase), baseUri, found, "image");
            // og:video:url / og:video:secure_url
            AddAll(Regex.Matches(html,
                @"<meta[^>]+property=""og:video[^""]*""[^>]+content=""([^""]+)""",
                RegexOptions.IgnoreCase), baseUri, found, "video");
            AddAll(Regex.Matches(html,
                @"<meta[^>]+content=""([^""]+)""[^>]+property=""og:video[^""]*""",
                RegexOptions.IgnoreCase), baseUri, found, "video");
            // og:audio
            AddAll(Regex.Matches(html,
                @"<meta[^>]+property=""og:audio[^""]*""[^>]+content=""([^""]+)""",
                RegexOptions.IgnoreCase), baseUri, found, "audio");

            // === 阶段 3: JSON-LD (结构化数据) ===
            foreach (Match m in Regex.Matches(html,
                @"<script[^>]+type=[""']application/ld\+json[""']>(.*?)</script>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (m.Groups.Count >= 2 && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                    ScanJson(m.Groups[1].Value, baseUri, found);
            }

            // === 阶段 4: 嵌入式 JS 状态数据 (SPA 站点如 B站、YouTube) ===
            // window.__INITIAL_STATE__ (B站 / 知乎等)
            foreach (Match m in Regex.Matches(html,
                @"window\.__INITIAL_STATE__\s*=\s*(\{.+?\});</script>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (m.Groups.Count >= 2) TryEscapeAndScan(m.Groups[1].Value, baseUri, found);
            }
            // window.__NEXT_DATA__ (Next.js)
            foreach (Match m in Regex.Matches(html,
                @"<script[^>]+id=[""']__NEXT_DATA__[""'][^>]*type=[""']application/json[""']>(.*?)</script>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (m.Groups.Count >= 2) ScanJson(m.Groups[1].Value, baseUri, found);
            }
            // window.__NUXT__ (Nuxt.js)
            foreach (Match m in Regex.Matches(html,
                @"window\.__NUXT__\s*=\s*(\{.+?\});",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (m.Groups.Count >= 2) TryEscapeAndScan(m.Groups[1].Value, baseUri, found);
            }
            // window.__DATA__ / window.__PRELOADED_STATE__ (常见 SPA)
            foreach (Match m in Regex.Matches(html,
                @"window\.__(?:DATA|PRELOADED_STATE|INITIAL_STORE)__\s*=\s*(\{.+?\});",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (m.Groups.Count >= 2) TryEscapeAndScan(m.Groups[1].Value, baseUri, found);
            }

            // === 阶段 5: 嵌入 JSON (常见 API 数据内嵌) ===
            foreach (Match m in Regex.Matches(html,
                @">\s*(\{.*""(?:url|src|href|videoUrl|cover|pic|image)""\s*:.*\})\s*<",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (m.Groups.Count >= 2) TryEscapeAndScan(m.Groups[1].Value, baseUri, found);
            }

            // === 阶段 6: 如果仍无资源，全量提取所有 URL 并用文件扩展名兜底 ===
            if (found.Count == 0)
            {
                foreach (Match m in Regex.Matches(html,
                    @"(https?://[^\s""'<>,\]]+)",
                    RegexOptions.IgnoreCase))
                {
                    var rawUrl = m.Groups[1].Value.Trim().TrimEnd('.', ',', ';', '!', '?', ':', ')', ']', '}');
                    if (LooksLikeDirectFile(rawUrl, ""))
                        TryEmit(rawUrl, baseUri, found);
                }
            }
        }

        /// <summary>
        /// 尝试对 JS 转义字符串进行 unescape 后再扫描 JSON。
        /// </summary>
        private void TryEscapeAndScan(string raw, Uri baseUri, HashSet<string> found)
        {
            try
            {
                // JS 中的 \/ 反斜杠转义还原
                var unescaped = raw.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t");
                ScanJson(unescaped, baseUri, found);
            }
            catch { /* 忽略解析失败 */ }
        }

        private void ScanJson(string json, Uri baseUri, HashSet<string> found)
        {
            // 直接匹配所有 http(s) URL
            foreach (Match m in Regex.Matches(json, @"""(https?:\\/\\/|https?://)[^""'<>\\\s]+""", RegexOptions.IgnoreCase))
            {
                TryEmit(m.Groups[1].Value + m.Groups[0].Value.Substring(m.Groups[0].Value.IndexOf(m.Groups[1].Value) + m.Groups[1].Value.Length)
                    .TrimEnd('"'), baseUri, found);
            }
            foreach (Match m in Regex.Matches(json, @"(https?:\/\/[^\s""'<>]+)", RegexOptions.IgnoreCase))
            {
                TryEmit(m.Groups[1].Value, baseUri, found);
            }
            // 兼容转义反斜杠
            foreach (Match m in Regex.Matches(json, @"(https?:\\/\\/[^\s""'<>]+)", RegexOptions.IgnoreCase))
            {
                var s = m.Groups[1].Value.Replace("\\/", "/").Replace("\\:", ":");
                TryEmit(s, baseUri, found);
            }
        }

        private void TryEmit(string url, Uri baseUri, HashSet<string> found)
        {
            url = url.Trim().TrimEnd('"', '\'', ',', '\\', ')', ']', '}');
            var normalized = NormalizeUrl(url, baseUri);
            if (string.IsNullOrEmpty(normalized)) return;
            if (!found.Add(normalized)) return;
            var filename = Path.GetFileName(new Uri(normalized).LocalPath);
            if (string.IsNullOrEmpty(filename)) filename = $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
            var type = GuessTypeFromUrl(normalized, "");
            ResourceFound?.Invoke(this, new SniffedResource
            {
                Url = normalized,
                Type = type,
                Filename = DownloadManager.SanitizeFileName(filename)
            });
        }

        private void AddAll(MatchCollection matches, Uri baseUri, HashSet<string> found, string type)
        {
            foreach (Match m in matches)
            {
                if (m.Groups.Count < 2) continue;
                var raw = m.Groups[1].Value.Trim().Trim('"', '\'', '(', ')');
                if (string.IsNullOrEmpty(raw)) continue;
                // srcset 可能包含多个 url，取第一个
                var firstUrl = raw.Split(',')[0].Trim().Split(' ')[0];
                var normalized = NormalizeUrl(firstUrl, baseUri);
                if (string.IsNullOrEmpty(normalized)) continue;
                if (!found.Add(normalized)) continue;
                var filename = Path.GetFileName(new Uri(normalized).LocalPath);
                if (string.IsNullOrEmpty(filename)) filename = $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
                ResourceFound?.Invoke(this, new SniffedResource
                {
                    Url = normalized,
                    Type = type,
                    Filename = DownloadManager.SanitizeFileName(filename)
                });
            }
        }

        private static string? NormalizeUrl(string url, Uri baseUri)
        {
            if (string.IsNullOrEmpty(url)) return null;
            url = url.Trim();
            if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return null;
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
            if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return null;
            if (url.StartsWith("#")) return null;
            try
            {
                if (url.StartsWith("//"))
                    return baseUri.Scheme + ":" + url;
                if (url.StartsWith("/"))
                    return baseUri.Scheme + "://" + baseUri.Authority + url;
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return url;
                return new Uri(baseUri, url).AbsoluteUri;
            }
            catch
            {
                return null;
            }
        }

        private static string GuessTypeFromUrl(string url, string contentType)
        {
            var u = url.ToLowerInvariant();
            if (u.EndsWith(".mp4") || u.EndsWith(".webm") || u.EndsWith(".mkv") || u.EndsWith(".mov") || u.Contains(".m3u8") || u.EndsWith(".ts") || u.EndsWith(".flv"))
                return "video";
            if (u.EndsWith(".mp3") || u.EndsWith(".wav") || u.EndsWith(".flac") || u.EndsWith(".m4a"))
                return "audio";
            if (u.EndsWith(".jpg") || u.EndsWith(".jpeg") || u.EndsWith(".png") || u.EndsWith(".gif") || u.EndsWith(".webp") || u.EndsWith(".bmp") || u.EndsWith(".avif"))
                return "image";
            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return "video";
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "audio";
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "image";
            return "file";
        }
    }
}
