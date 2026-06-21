using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TDM.Services;

namespace TDM.Windows
{
    public partial class LogWindow : Window
    {
        private readonly string _logFile;
        private readonly DispatcherTimer _tailTimer;
        private long _lastPosition = 0;
        private readonly List<string> _allLines = new();
        private int _lineCount;

        // 颜色定义（VS Code 暗色风格）
        private static readonly SolidColorBrush BrushTimestamp = new(Color.FromRgb(0x80, 0x80, 0x80));
        private static readonly SolidColorBrush BrushInfo = new(Color.FromRgb(0x4A, 0x9E, 0xFF));
        private static readonly SolidColorBrush BrushWarn = new(Color.FromRgb(0xFF, 0x98, 0x00));
        private static readonly SolidColorBrush BrushError = new(Color.FromRgb(0xF4, 0x43, 0x36));
        private static readonly SolidColorBrush BrushDebug = new(Color.FromRgb(0xBC, 0xAA, 0xFF));
        private static readonly SolidColorBrush BrushHighlight = new(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly SolidColorBrush BrushText = new(Color.FromRgb(0xD4, 0xD4, 0xD4));

        public LogWindow()
        {
            InitializeComponent();
            _logFile = Path.Combine(App.LogDirectory, $"TDM-{DateTime.Now:yyyyMMdd}.log");
            FilePathLabel.Text = _logFile;

            // 初次加载
            LoadAllLines();

            // 实时跟踪定时器
            _tailTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _tailTimer.Tick += OnTailTick;
            Loaded += (_, _) => _tailTimer.Start();
            Closed += (_, _) => _tailTimer.Stop();

            // 过滤变化时重绘
            FilterInfo.Checked += (_, _) => Refresh();
            FilterInfo.Unchecked += (_, _) => Refresh();
            FilterWarn.Checked += (_, _) => Refresh();
            FilterWarn.Unchecked += (_, _) => Refresh();
            FilterError.Checked += (_, _) => Refresh();
            FilterError.Unchecked += (_, _) => Refresh();
            FilterDebug.Checked += (_, _) => Refresh();
            FilterDebug.Unchecked += (_, _) => Refresh();
            SearchBox.TextChanged += (_, _) => Refresh();
        }

        private void LoadAllLines()
        {
            try
            {
                if (!File.Exists(_logFile))
                {
                    _allLines.Clear();
                    _lastPosition = 0;
                    Refresh();
                    return;
                }

                using var fs = new FileStream(_logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(0, SeekOrigin.End);
                _lastPosition = fs.Length;
                fs.Seek(0, SeekOrigin.Begin);
                using var sr = new StreamReader(fs);
                var content = sr.ReadToEnd();
                _allLines.Clear();
                _allLines.AddRange(content.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)));
                Refresh();
            }
            catch (Exception ex)
            {
                Logger.Warn("日志初次加载失败: " + ex.Message);
            }
        }

        private void OnTailTick(object? sender, EventArgs e)
        {
            if (!FollowTailCheck.IsChecked == true) return;
            if (!File.Exists(_logFile)) return;
            try
            {
                using var fs = new FileStream(_logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var len = fs.Length;
                if (len <= _lastPosition)
                {
                    // 文件被截断或重置
                    if (len < _lastPosition)
                    {
                        _lastPosition = 0;
                        _allLines.Clear();
                    }
                    else return;
                }
                fs.Seek(_lastPosition, SeekOrigin.Begin);
                using var sr = new StreamReader(fs);
                var newContent = sr.ReadToEnd();
                _lastPosition = len;
                if (!string.IsNullOrEmpty(newContent))
                {
                    var newLines = newContent.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l));
                    _allLines.AddRange(newLines);
                    // 限制总行数（避免内存爆炸）
                    const int maxLines = 5000;
                    if (_allLines.Count > maxLines)
                    {
                        _allLines.RemoveRange(0, _allLines.Count - maxLines);
                    }
                    Refresh();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("日志 tail 失败: " + ex.Message);
            }
        }

        private void Refresh()
        {
            try
            {
                LogParagraph.Inlines.Clear();
                _lineCount = 0;
                var keyword = SearchBox.Text?.Trim() ?? "";
                var regex = string.IsNullOrEmpty(keyword) ? null
                    : new Regex(Regex.Escape(keyword), RegexOptions.IgnoreCase);

                foreach (var raw in _allLines)
                {
                    var line = raw.TrimEnd('\r');
                    var level = DetectLevel(line);
                    if (!IsLevelEnabled(level)) continue;
                    if (regex != null && !regex.IsMatch(line)) continue;

                    AppendLine(line, level, regex);
                    _lineCount++;
                }

                LineCountLabel.Text = $"共 {_lineCount} 行 / 总 {_allLines.Count} 行";
                LastUpdateLabel.Text = $"更新于 {DateTime.Now:HH:mm:ss}";

                if (AutoScrollCheck.IsChecked == true)
                {
                    LogView.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("日志渲染失败: " + ex.Message);
            }
        }

        private bool IsLevelEnabled(string level)
        {
            return level switch
            {
                "INFO" => FilterInfo.IsChecked == true,
                "WARN" => FilterWarn.IsChecked == true,
                "ERROR" => FilterError.IsChecked == true,
                "DEBUG" => FilterDebug.IsChecked == true,
                _ => true
            };
        }

        private static string DetectLevel(string line)
        {
            int second = line.IndexOf("] ", StringComparison.Ordinal);
            if (second < 0) return "";
            int start = line.IndexOf('[', 1);
            if (start < 0 || start > second) return "";
            var token = line.Substring(start + 1, second - start - 1).Trim();
            return token;
        }

        private void AppendLine(string line, string level, Regex? highlight)
        {
            // 行格式：[时间戳] [LEVEL] 消息
            // 拆成 3 段
            var first = line.IndexOf("] ", StringComparison.Ordinal);
            if (first > 0)
            {
                AppendText("[" + line.Substring(1, first - 1) + "] ", BrushTimestamp, null);
                var rest = line.Substring(first + 2);
                var second = rest.IndexOf("] ", StringComparison.Ordinal);
                if (second > 0)
                {
                    var lvlToken = rest.Substring(0, second);
                    AppendText("[" + lvlToken + "] ", GetLevelBrush(lvlToken.Trim()), null);
                    var msg = rest.Substring(second + 2);
                    AppendText(msg, BrushText, highlight);
                }
                else
                {
                    AppendText(rest, BrushText, highlight);
                }
            }
            else
            {
                AppendText(line, BrushText, highlight);
            }
            LogParagraph.Inlines.Add(new LineBreak());
        }

        private static SolidColorBrush GetLevelBrush(string level)
        {
            return level switch
            {
                "INFO" => BrushInfo,
                "WARN" => BrushWarn,
                "ERROR" => BrushError,
                "DEBUG" => BrushDebug,
                _ => BrushText
            };
        }

        private void AppendText(string text, Brush brush, Regex? highlight)
        {
            if (highlight == null)
            {
                LogParagraph.Inlines.Add(new Run(text) { Foreground = brush });
                return;
            }
            int idx = 0;
            foreach (Match m in highlight.Matches(text))
            {
                if (m.Index > idx)
                {
                    LogParagraph.Inlines.Add(new Run(text.Substring(idx, m.Index - idx)) { Foreground = brush });
                }
                LogParagraph.Inlines.Add(new Run(m.Value)
                {
                    Foreground = BrushHighlight,
                    FontWeight = FontWeights.Bold
                });
                idx = m.Index + m.Length;
            }
            if (idx < text.Length)
            {
                LogParagraph.Inlines.Add(new Run(text.Substring(idx)) { Foreground = brush });
            }
        }

        private void OnClearLog(object sender, RoutedEventArgs e)
        {
            LogParagraph.Inlines.Clear();
            _allLines.Clear();
            _lineCount = 0;
            LineCountLabel.Text = "共 0 行";
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(App.LogDirectory))
                    System.Diagnostics.Process.Start("explorer.exe", App.LogDirectory);
            }
            catch { }
        }

        private void OnCopyAll(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = string.Join(Environment.NewLine, _allLines);
                Clipboard.SetText(text);
                SetStatus("已复制全部日志到剪贴板（" + _allLines.Count + " 行）");
            }
            catch (Exception ex)
            {
                SetStatus("复制失败: " + ex.Message);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void SetStatus(string text)
        {
            LastUpdateLabel.Text = text;
        }
    }
}
