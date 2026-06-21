using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TDM.Models;

namespace TDM.Services
{
    public class BrowserBridgeService : IDisposable
    {
        private static BrowserBridgeService? _instance;
        public static BrowserBridgeService Instance => _instance ??= new BrowserBridgeService();

        public static void Start()
        {
            Instance.StartInternal();
        }

        public static void Stop()
        {
            _instance?.Dispose();
            _instance = null;
        }

        private readonly HttpListener _listener;
        private readonly Thread _thread;
        private CancellationTokenSource? _cts;
        private volatile bool _running;

        public event EventHandler<SniffedResource>? ResourceReceived;

        public BrowserBridgeService(int port = 8765)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _thread = new Thread(ListenLoop) { IsBackground = true, Name = "BrowserBridge" };
        }

        public void StartInternal()
        {
            try
            {
                _listener.Start();
                _running = true;
                _thread.Start();
                Logger.Info("BrowserBridge 已启动：http://127.0.0.1:8765");
            }
            catch (Exception ex)
            {
                Logger.Warn("BrowserBridge 启动失败（端口可能被占用）: " + ex.Message);
            }
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    _ = HandleAsync(ctx);
                }
                catch (HttpListenerException) { break; }
                catch (Exception ex) { Logger.Warn("BrowserBridge 处理失败: " + ex.Message); }
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                var resp = ctx.Response;
                resp.Headers.Add("Access-Control-Allow-Origin", "*");
                resp.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (req.HttpMethod == "OPTIONS")
                {
                    resp.StatusCode = 200;
                    resp.Close();
                    return;
                }

                if (req.HttpMethod == "GET" && req.Url?.AbsolutePath == "/api/ping")
                {
                    var json = JsonSerializer.Serialize(new { ok = true, version = "1.0.0" });
                    await WriteJsonAsync(resp, json);
                    return;
                }

                if (req.HttpMethod == "POST" && req.Url?.AbsolutePath == "/api/resources")
                {
                    using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("resources", out var arr))
                        {
                            int count = 0;
                            foreach (var item in arr.EnumerateArray())
                            {
                                var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                                var type = item.TryGetProperty("type", out var t) ? t.GetString() : "file";
                                var filename = item.TryGetProperty("filename", out var f) ? f.GetString() : null;
                                if (string.IsNullOrEmpty(url)) continue;
                                if (filename == null) filename = DownloadManager.SanitizeFileName(Path.GetFileName(new Uri(url).LocalPath));
                                if (string.IsNullOrEmpty(filename)) filename = $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
                                var res = new SniffedResource
                                {
                                    Url = url,
                                    Type = type ?? "file",
                                    Filename = filename
                                };
                                ResourceReceived?.Invoke(this, res);
                                count++;
                            }
                            Logger.Info($"BrowserBridge 接收 {count} 个资源");
                            await WriteJsonAsync(resp, JsonSerializer.Serialize(new { success = true, count }));
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        await WriteJsonAsync(resp, JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                        return;
                    }
                }

                resp.StatusCode = 404;
                resp.Close();
            }
            catch (Exception ex)
            {
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
                Logger.Warn("BrowserBridge 请求异常: " + ex.Message);
            }
        }

        private static async Task WriteJsonAsync(HttpListenerResponse resp, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType = "application/json; charset=utf-8";
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            resp.Close();
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }
}
