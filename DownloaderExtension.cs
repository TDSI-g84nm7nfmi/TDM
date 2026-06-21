using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TDM.Controls
{
    /// <summary>
    /// 现代化的下载进度控件
    /// </summary>
    public class ModernProgressBar : Control
    {
        private Rectangle _progressRect;
        private Rectangle _backgroundRect;
        private TextBlock _textBlock;
        
        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register("Progress", typeof(double), typeof(ModernProgressBar),
                new PropertyMetadata(0.0, OnProgressChanged));
        
        public static readonly DependencyProperty ProgressTextProperty =
            DependencyProperty.Register("ProgressText", typeof(string), typeof(ModernProgressBar),
                new PropertyMetadata("0%"));
        
        public double Progress
        {
            get { return (double)GetValue(ProgressProperty); }
            set { SetValue(ProgressProperty, value); }
        }
        
        public string ProgressText
        {
            get { return (string)GetValue(ProgressTextProperty); }
            set { SetValue(ProgressTextProperty, value); }
        }
        
        static ModernProgressBar()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ModernProgressBar), 
                new FrameworkPropertyMetadata(typeof(ModernProgressBar)));
        }
        
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            
            _progressRect = GetTemplateChild("PART_ProgressRect") as Rectangle;
            _backgroundRect = GetTemplateChild("PART_BackgroundRect") as Rectangle;
            _textBlock = GetTemplateChild("PART_TextBlock") as TextBlock;
        }
        
        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as ModernProgressBar;
            control?.AnimateProgress((double)e.NewValue);
        }
        
        private void AnimateProgress(double newValue)
        {
            if (_progressRect == null) return;
            
            var animation = new DoubleAnimation(newValue, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            _progressRect.BeginAnimation(WidthProperty, animation);
        }
    }
    
    /// <summary>
    /// 文件拖放控件
    /// </summary>
    public class FileDropZone : Border
    {
        public static readonly DependencyProperty DroppedFilesProperty =
            DependencyProperty.Register("DroppedFiles", typeof(List<string>), typeof(FileDropZone),
                new PropertyMetadata(new List<string>()));
        
        public List<string> DroppedFiles
        {
            get { return (List<string>)GetValue(DroppedFilesProperty); }
            set { SetValue(DroppedFilesProperty, value); }
        }
        
        public event EventHandler<List<string>> FilesDropped;
        
        public FileDropZone()
        {
            AllowDrop = true;
            Background = new SolidColorBrush(Colors.Transparent);
            BorderBrush = new SolidColorBrush(Colors.Gray);
            BorderThickness = new Thickness(2);
            CornerRadius = new CornerRadius(8);
            
            Drop += OnDrop;
            DragEnter += OnDragEnter;
            DragLeave += OnDragLeave;
        }
        
        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                DroppedFiles = new List<string>(files);
                FilesDropped?.Invoke(this, DroppedFiles);
                
                Background = new SolidColorBrush(Colors.Transparent);
            }
        }
        
        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 0, 150, 255));
                e.Effects = DragDropEffects.Copy;
            }
        }
        
        private void OnDragLeave(object sender, DragEventArgs e)
        {
            Background = new SolidColorBrush(Colors.Transparent);
        }
    }
    
    /// <summary>
    /// 高性能文件下载器
    /// </summary>
    public class HighPerformanceDownloader
    {
        private readonly HttpClient _httpClient;
        
        public event EventHandler<DownloadProgressEventArgs> ProgressChanged;
        public event EventHandler<DownloadCompletedEventArgs> DownloadCompleted;
        
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
                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    
                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    var downloadedBytes = 0L;
                    
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    {
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
                    }
                    
                    DownloadCompleted?.Invoke(this, new DownloadCompletedEventArgs
                    {
                        FilePath = filePath,
                        Success = true
                    });
                }
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
        public string FilePath { get; set; }
        public bool Success { get; set; }
        public Exception Error { get; set; }
    }
    
    /// <summary>
    /// 动画按钮控件
    /// </summary>
    public class AnimatedButton : Button
    {
        public static readonly DependencyProperty HoverColorProperty =
            DependencyProperty.Register("HoverColor", typeof(Brush), typeof(AnimatedButton),
                new PropertyMetadata(new SolidColorBrush(Colors.LightBlue)));
        
        public Brush HoverColor
        {
            get { return (Brush)GetValue(HoverColorProperty); }
            set { SetValue(HoverColorProperty, value); }
        }
        
        public AnimatedButton()
        {
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;
        }
        
        private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var animation = new ColorAnimation(
                ((SolidColorBrush)HoverColor).Color,
                TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            Background.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }
        
        private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var animation = new ColorAnimation(
                Colors.Transparent,
                TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            Background.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }
    }
} 