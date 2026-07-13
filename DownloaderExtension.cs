using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace TDM.Controls
{
    public class ModernProgressBar : Control
    {
        private FrameworkElement? _progressElement;
        private CompositeTransform? _transform;

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(nameof(Progress), typeof(double), typeof(ModernProgressBar),
                new PropertyMetadata(0.0, OnProgressChanged));

        public string ProgressText
        {
            get => (string)GetValue(ProgressTextProperty);
            set => SetValue(ProgressTextProperty, value);
        }

        public static readonly DependencyProperty ProgressTextProperty =
            DependencyProperty.Register(nameof(ProgressText), typeof(string), typeof(ModernProgressBar),
                new PropertyMetadata("0%"));

        public ModernProgressBar()
        {
            DefaultStyleKey = typeof(ModernProgressBar);
        }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _progressElement = GetTemplateChild("PART_ProgressRect") as FrameworkElement;
            if (_progressElement != null)
            {
                _transform = new CompositeTransform();
                _progressElement.RenderTransform = _transform;
            }
            UpdateProgress(Progress, animate: false);
        }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ModernProgressBar)d;
            control.UpdateProgress((double)e.NewValue, animate: true);
        }

        private void UpdateProgress(double newValue, bool animate)
        {
            if (_transform == null) return;
            double scaleX = Math.Clamp(newValue / 100.0, 0, 1);

            if (animate)
            {
                var anim = new DoubleAnimation
                {
                    To = scaleX,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(anim, _transform);
                Storyboard.SetTargetProperty(anim, "ScaleX");
                var sb = new Storyboard();
                sb.Children.Add(anim);
                sb.Begin();
            }
            else
            {
                _transform.ScaleX = scaleX;
            }
        }
    }

    public class FileDropZone : Control
    {
        public List<string> DroppedFiles
        {
            get => (List<string>)GetValue(DroppedFilesProperty);
            set => SetValue(DroppedFilesProperty, value);
        }

        public static readonly DependencyProperty DroppedFilesProperty =
            DependencyProperty.Register(nameof(DroppedFiles), typeof(List<string>), typeof(FileDropZone),
                new PropertyMetadata(null));

        public event EventHandler<List<string>>? FilesDropped;

        public FileDropZone()
        {
            AllowDrop = true;
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            BorderThickness = new Thickness(2);
            CornerRadius = new CornerRadius(8);

            Drop += OnDrop;
            DragEnter += OnDragEnter;
            DragLeave += OnDragLeave;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                DropFiles();
            }
        }

        private async void DropFiles()
        {
            try
            {
                var items = await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent()
                    .GetStorageItemsAsync();
                var files = new List<string>();
                foreach (var item in items)
                    files.Add(item.Path);
                DroppedFiles = files;
                FilesDropped?.Invoke(this, files);
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            catch { }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 0, 150, 255));
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    public class HighPerformanceDownloader : IDisposable
    {
        private readonly HttpClient _httpClient;

        public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;
        public event EventHandler<DownloadCompletedEventArgs>? DownloadCompleted;

        public HighPerformanceDownloader()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public async Task DownloadFileAsync(string url, string filePath, int bufferSize = 8192)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                var buffer = new byte[bufferSize];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;

                    var progress = totalBytes > 0 ? (double)downloadedBytes / totalBytes : 0;
                    ProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                    {
                        BytesReceived = downloadedBytes,
                        TotalBytesToReceive = totalBytes,
                        ProgressPercentage = progress * 100
                    });
                }

                DownloadCompleted?.Invoke(this, new DownloadCompletedEventArgs
                {
                    FilePath = filePath,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                DownloadCompleted?.Invoke(this, new DownloadCompletedEventArgs
                {
                    FilePath = filePath,
                    Success = false,
                    Error = ex
                });
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class DownloadProgressEventArgs : EventArgs
    {
        public long BytesReceived { get; set; }
        public long TotalBytesToReceive { get; set; }
        public double ProgressPercentage { get; set; }
    }

    public class DownloadCompletedEventArgs : EventArgs
    {
        public string FilePath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public Exception? Error { get; set; }
    }

    public class AnimatedButton : Button
    {
        public Brush HoverColor
        {
            get => (Brush)GetValue(HoverColorProperty);
            set => SetValue(HoverColorProperty, value);
        }

        public static readonly DependencyProperty HoverColorProperty =
            DependencyProperty.Register(nameof(HoverColor), typeof(Brush), typeof(AnimatedButton),
                new PropertyMetadata(new SolidColorBrush(Microsoft.UI.Colors.LightBlue)));

        private Brush? _originalBackground;

        public AnimatedButton()
        {
            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
        }

        private void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _originalBackground = Background;
            Background = HoverColor;
        }

        private void OnPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_originalBackground != null)
                Background = _originalBackground;
        }
    }
}
