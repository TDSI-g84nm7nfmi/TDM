using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TDM.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
                flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            return flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v != Visibility.Visible;
        }
    }

    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null
                || (value is string s && string.IsNullOrEmpty(s))
                || (value is int i && i == 0)
                || (value is long l && l == 0)
                || (value is double d && d == 0)
                || (value is System.Collections.ICollection c && c.Count == 0);
            if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
                isNull = !isNull;
            return isNull;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = 0;
            if (value is int i) count = i;
            else if (value is System.Collections.ICollection c) count = c.Count;
            bool visible = count > 0;
            if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
                visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BytesToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long bytes = 0;
            if (value is long l) bytes = l;
            else if (value is int i) bytes = i;
            else if (value is double d) bytes = (long)d;
            return FormatBytes(bytes);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024.0;
                unit++;
            }
            return $"{size:0.##} {units[unit]}";
        }
    }

    public class SpeedToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double speed = 0;
            if (value is double d) speed = d;
            else if (value is float f) speed = f;
            else if (value is long l) speed = l;
            else if (value is int i) speed = i;
            return $"{BytesToStringConverter.FormatBytes((long)speed)}/s";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class SecondsToDurationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return Format((long)Math.Max(0, d));
            if (value is long l) return Format(Math.Max(0, l));
            if (value is int i) return Format(Math.Max(0, i));
            if (value is TimeSpan ts) return Format((long)ts.TotalSeconds);
            return "--:--";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        public static string Format(long seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1)
                return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
            return $"{t.Minutes:00}:{t.Seconds:00}";
        }
    }

    public class EtaConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3) return "--:--";
            long total = ToLong(values[0]);
            long downloaded = ToLong(values[1]);
            double speed = ToDouble(values[2]);
            if (total <= 0 || speed <= 0) return "--:--";
            long remaining = total - downloaded;
            if (remaining <= 0) return "00:00";
            return SecondsToDurationConverter.Format((long)(remaining / speed));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        private static long ToLong(object v) => v switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            _ => 0
        };

        private static double ToDouble(object v) => v switch
        {
            double d => d,
            float f => f,
            long l => l,
            int i => i,
            _ => 0
        };
    }

    public class PercentFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double d = 0;
            if (value is double dv) d = dv;
            else if (value is float f) d = f;
            else if (value is int i) d = i;
            return $"{d:0.#}%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? status = value as string;
            return status switch
            {
                "完成" or "Completed" => new SolidColorBrush(Color.FromRgb(0x4c, 0xaf, 0x50)),
                "下载中" or "Downloading" => (System.Windows.Media.Brush)Application.Current.Resources["PrimaryBrush"],
                "已暂停" or "Paused" => new SolidColorBrush(Color.FromRgb(0xff, 0x98, 0x00)),
                "错误" or "Failed" or "失败" => new SolidColorBrush(Color.FromRgb(0xf4, 0x43, 0x36)),
                "等待中" or "Pending" or "队列中" or "连接中" => new SolidColorBrush(Color.FromRgb(0x9e, 0x9e, 0x9e)),
                _ => new SolidColorBrush(Color.FromRgb(0x9e, 0x9e, 0x9e)),
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
