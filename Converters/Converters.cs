using System;
using System.Collections;
using System.Globalization;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TDM.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool flag = value is bool b && b;
            if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
                flag = !flag;
            return flag;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is bool b && b;
        }
    }

    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b) return !b;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b) return !b;
            return false;
        }
    }

    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isNull = value == null
                || (value is string s && string.IsNullOrEmpty(s))
                || (value is int i && i == 0)
                || (value is long l && l == 0)
                || (value is double d && d == 0)
                || (value is ICollection c && c.Count == 0);
            if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
                isNull = !isNull;
            return isNull;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            int count = 0;
            if (value is int i) count = i;
            else if (value is ICollection c) count = c.Count;
            bool visible = count > 0;
            if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
                visible = !visible;
            return visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class BytesToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long bytes = 0;
            if (value is long l) bytes = l;
            else if (value is int i) bytes = i;
            else if (value is double d) bytes = (long)d;
            return FormatBytes(bytes);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
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
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            double speed = 0;
            if (value is double d) speed = d;
            else if (value is float f) speed = f;
            else if (value is long l) speed = l;
            else if (value is int i) speed = i;
            return $"{BytesToStringConverter.FormatBytes((long)speed)}/s";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class PercentFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            double d = 0;
            if (value is double dv) d = dv;
            else if (value is float f) d = f;
            else if (value is int i) d = i;
            return $"{d:0.#}%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string? status = value as string;
            return status switch
            {
                "完成" or "Completed" => new SolidColorBrush(Color.FromArgb(255, 0x4c, 0xaf, 0x50)),
                "下载中" or "Downloading" => new SolidColorBrush(Color.FromArgb(255, 0x4a, 0x9e, 0xff)),
                "已暂停" or "Paused" => new SolidColorBrush(Color.FromArgb(255, 0xff, 0x98, 0x00)),
                "错误" or "Failed" or "失败" => new SolidColorBrush(Color.FromArgb(255, 0xf4, 0x43, 0x36)),
                "等待中" or "Pending" or "队列中" or "连接中" => new SolidColorBrush(Color.FromArgb(255, 0x9e, 0x9e, 0x9e)),
                "HTTP" => new SolidColorBrush(Color.FromArgb(255, 0x4f, 0x8b, 0xff)),
                "BT" => new SolidColorBrush(Color.FromArgb(255, 0x1b, 0xc9, 0x82)),
                "eD2k" => new SolidColorBrush(Color.FromArgb(255, 0x6e, 0xc6, 0xff)),
                _ => new SolidColorBrush(Color.FromArgb(255, 0x9e, 0x9e, 0x9e)),
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
