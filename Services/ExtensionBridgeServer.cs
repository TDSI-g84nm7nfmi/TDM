using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TDM.Services;

namespace TDM.Bridge
{
    /// <summary>
    /// 浏览器扩展 WebSocket 桥接服务器。
    /// 替代 Native Messaging，TDM 直接监听 ws://127.0.0.1:19199/tdm，
    /// 扩展通过 WebSocket 连接，不依赖 Python / 注册表 / 扩展 ID 计算。
    /// </summary>
    public class ExtensionBridgeServer : IDisposable
    {
        private const int Port = 19199;
        private const string Path = "/tdm";

        /// <summary>全局单例</summary>
        public static ExtensionBridgeServer Instance { get; } = new();

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        // 当前连接的扩展（浏览器）
        private WebSocket? _currentSocket;
        private readonly object _socketLock = new();

        /// <summary>是否正在运行</summary>
        public bool IsRunning => _listener?.IsListening ?? false;

        /// <summary>扩展是否已连接</summary>
        public bool IsConnected
        {
            get { lock (_socketLock) return _currentSocket?.State == WebSocketState.Open; }
        }

        /// <summary>从扩展收到的消息</summary>
        public event Action<string>? OnMessage;

        /// <summary>扩展连接/断开事件</summary>
        public event Action<bool>? OnConnectionChanged;

        public void Start()
        {
            if (_listener?.IsListening == true) return;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}{Path}");
                _listener.Start();
                Logger.Info($"ExtensionBridgeServer 已启动: ws://127.0.0.1:{Port}{Path}");

                _loopTask = AcceptLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ExtensionBridgeServer 启动失败: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                lock (_socketLock)
                {
                    var ws = _currentSocket;
                    if (ws?.State == WebSocketState.Open)
                        ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "server stop", CancellationToken.None)
                            .GetAwaiter().GetResult();
                    ws?.Dispose();
                    _currentSocket = null;
                }
                _listener?.Stop();
                Logger.Info("ExtensionBridgeServer 已停止");
            }
            catch { }
        }

        /// <summary>向扩展发送 JSON 消息</summary>
        public void Send(JsonElement payload)
        {
            SendRaw(JsonSerializer.Serialize(payload));
        }

        /// <summary>向扩展发送原始 JSON 字符串</summary>
        public void Send(string jsonString)
        {
            SendRaw(jsonString);
        }

        private void SendRaw(string text)
        {
            WebSocket? ws;
            lock (_socketLock) ws = _currentSocket;

            if (ws?.State != WebSocketState.Open) return;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.Warn($"ExtensionBridgeServer 发送失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _listener?.Close();
            _cts?.Dispose();
        }

        // ========== 内部 ==========

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                try
                {
                    var ctxTask = _listener.GetContextAsync();
                    if (ctxTask == await Task.WhenAny(ctxTask, Task.Delay(-1, ct)))  // wait with cancellation
                    {
                        var ctx = ctxTask.Result;
                        if (ctx.Request.IsWebSocketRequest)
                        {
                            _ = HandleWebSocket(ctx, ct);
                        }
                        else
                        {
                            // 非 WS 请求 → 返回 400
                            ctx.Response.StatusCode = 400;
                            ctx.Response.Close();
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    Logger.Warn($"ExtensionBridgeServer AcceptLoop: {ex.Message}");
                    await Task.Delay(1000, ct);
                }
            }
        }

        private async Task HandleWebSocket(HttpListenerContext ctx, CancellationToken ct)
        {
            try
            {
                var wsCtx = await ctx.AcceptWebSocketAsync(null);
                var ws = wsCtx.WebSocket;

                lock (_socketLock)
                {
                    // 关闭旧连接
                    var old = _currentSocket;
                    if (old?.State == WebSocketState.Open)
                        old.CloseAsync(WebSocketCloseStatus.NormalClosure, "new connection", CancellationToken.None)
                                    .GetAwaiter().GetResult();
                    _currentSocket = ws;
                }

                Logger.Info("扩展已通过 WebSocket 连接");
                OnConnectionChanged?.Invoke(true);

                // 发送握手
                Send("{\"type\":\"connected\",\"version\":1}");

                // 读取消息循环
                var buffer = new byte[1024 * 64]; // 64KB
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        // 处理多帧消息
                        if (!result.EndOfMessage)
                        {
                            // 简单起见，组合多帧
                            var sb = new StringBuilder(text);
                            while (!result.EndOfMessage)
                            {
                                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                            }
                            text = sb.ToString();
                        }

                        OnMessage?.Invoke(text);
                    }
                }

                Logger.Info("扩展 WebSocket 已断开");
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Warn($"HandleWebSocket 异常: {ex.Message}");
            }
            finally
            {
                lock (_socketLock)
                {
                    if (_currentSocket?.State != WebSocketState.Open)
                    {
                        _currentSocket?.Dispose();
                        _currentSocket = null;
                    }
                }
                OnConnectionChanged?.Invoke(false);
            }
        }
    }
}