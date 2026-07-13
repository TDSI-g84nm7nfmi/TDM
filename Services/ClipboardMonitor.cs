using System;
using Microsoft.UI.Xaml;

namespace TDM.Services
{
    public class ClipboardMonitor : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private string _lastText = string.Empty;
        private bool _disposed;

        public event EventHandler<string>? UrlDetected;

        public ClipboardMonitor()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            if (_disposed) return;
            try
            {
                var data = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                {
                    var task = data.GetTextAsync().AsTask();
                    task.Wait(1000);
                    _lastText = task.Result ?? string.Empty;
                }
            }
            catch { }
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private void OnTick(object? sender, object e)
        {
            if (_disposed) return;
            try
            {
                var data = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (!data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                    return;
                var task = data.GetTextAsync().AsTask();
                task.Wait(1000);
                var text = task.Result ?? string.Empty;
                if (string.IsNullOrEmpty(text) || text == _lastText) return;
                _lastText = text;
                if (IsUrl(text))
                    UrlDetected?.Invoke(this, text);
            }
            catch { }
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
