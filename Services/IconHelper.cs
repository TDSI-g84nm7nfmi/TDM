using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TDM.Services
{
    /// <summary>
    /// 应用图标辅助。
    /// </summary>
    public static class IconHelper
    {
        public static string AssetsDir
        {
            get
            {
                var baseDir = AppContext.BaseDirectory;
                var assets = Path.Combine(baseDir, "Assets");
                if (Directory.Exists(assets)) return assets;
                return baseDir;
            }
        }

        public static string? GetIconPath(string name = "icon")
        {
            var path = Path.Combine(AssetsDir, name + ".ico");
            if (File.Exists(path)) return path;
            path = Path.Combine(AssetsDir, "icon.ico");
            return File.Exists(path) ? path : null;
        }

        public static ImageSource? LoadImageSource(string name, int size = 32)
        {
            var path = GetIconPath(name);
            if (path == null) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = size;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 创建一个默认的下载图标。
        /// </summary>
        public static ImageSource CreateDefaultIcon(int size = 64)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var brush = new SolidColorBrush(Color.FromRgb(0x7c, 0x4d, 0xff));
                brush.Freeze();
                dc.DrawRoundedRectangle(brush, null, new Rect(0, 0, size, size), size * 0.18, size * 0.18);

                var white = Brushes.White;
                double cx = size / 2.0;
                double w = size * 0.45;
                double h = size * 0.5;
                double top = size * 0.18;

                var streamGeo = new StreamGeometry();
                using (var ctx = streamGeo.Open())
                {
                    ctx.BeginFigure(new Point(cx, top + h * 0.55), true, true);
                    ctx.LineTo(new Point(cx + w * 0.5, top + h * 0.55), false, false);
                    ctx.LineTo(new Point(cx + w * 0.15, top + h * 0.55), false, false);
                    ctx.LineTo(new Point(cx + w * 0.15, top + h), false, false);
                    ctx.LineTo(new Point(cx - w * 0.15, top + h), false, false);
                    ctx.LineTo(new Point(cx - w * 0.15, top + h * 0.55), false, false);
                    ctx.LineTo(new Point(cx - w * 0.5, top + h * 0.55), false, false);
                }
                streamGeo.Freeze();
                dc.DrawGeometry(white, null, streamGeo);
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public static ImageSource GetAppIcon()
        {
            try
            {
                // 优先从嵌入资源加载
                var uri = new Uri("pack://application:,,,/image/icon.ico", UriKind.Absolute);
                var icon = new BitmapImage(uri);
                icon.Freeze();
                return icon;
            }
            catch
            {
                // 文件加载
                try
                {
                    var icoPath = GetIconPath("icon");
                    if (!string.IsNullOrEmpty(icoPath) && File.Exists(icoPath))
                    {
                        return LoadImageSource("icon", 64);
                    }
                }
                catch { }
                // 绘制兜底图标
                return CreateDefaultIcon(64);
            }
        }
    }
}
