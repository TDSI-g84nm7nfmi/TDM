using System;
using System.Windows;
using System.Windows.Threading;

namespace TDM.Services
{
    /// <summary>
    /// 剪贴板监视器。
    /// </summary>
    public class ClipboardMonitor : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private string _lastText = string.Empty;
        private bool _disposed;

        public event EventHandler<string>? UrlDetected;

        public ClipboardMonitor()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            if (_disposed) return;
            try { _lastText = Clipboard.GetText() ?? string.Empty; } catch { }
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private void OnTick(object? sender, EventArgs e)
        {
            if (_disposed) return;
            try
            {
                var text = Clipboard.GetText() ?? string.Empty;
                if (string.IsNullOrEmpty(text) || text == _lastText) return;
                _lastText = text;
                if (IsUrl(text))
                {
                    UrlDetected?.Invoke(this, text);
                }
            }
            catch { /* 忽略 */ }
        }

        public static bool IsUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return false;
            return Uri.TryCreate(text, UriKind.Absolute, out var u)
                && !string.IsNullOrEmpty(u.Host)
                && u.Host.Contains(".");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
