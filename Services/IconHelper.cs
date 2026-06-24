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
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "image", name + ".png"),
                Path.Combine(baseDir, "image", name + ".ico"),
                Path.Combine(baseDir, "Assets", name + ".png"),
                Path.Combine(baseDir, "Assets", name + ".ico"),
                Path.Combine(baseDir, name + ".png"),
                Path.Combine(baseDir, name + ".ico"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        public static ImageSource? LoadImageSource(string name, int size = 32)
        {
            var path = GetIconPath(name);
            if (path == null) return null;
            try
            {
                // 如果扩展名和实际格式不符（如 .ico 实际是 PNG），用流解码更稳健
                using var fs = File.OpenRead(path);
                BitmapDecoder decoder;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png")
                    decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                else if (ext == ".ico")
                    decoder = new IconBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                else
                    decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

                var src = decoder.Frames[0];
                if (size > 0 && src.PixelWidth > size)
                {
                    var scaled = new TransformedBitmap(src, new ScaleTransform(size / (double)src.PixelWidth, size / (double)src.PixelWidth));
                    scaled.Freeze();
                    return scaled;
                }
                src.Freeze();
                return src;
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
            // 优先从嵌入资源加载 PNG 图标
            var packUris = new[]
            {
                "pack://application:,,,/image/icon.png",
                "pack://application:,,,/image/icon.ico",
                "pack://application:,,,/Assets/icon.png",
                "pack://application:,,,/Assets/icon.ico",
            };
            foreach (var pack in packUris)
            {
                try
                {
                    var uri = new Uri(pack, UriKind.Absolute);
                    var icon = new BitmapImage(uri);
                    icon.Freeze();
                    if (icon.PixelWidth > 0) return icon;
                }
                catch { }
            }

            // 文件加载
            try
            {
                var icoPath = GetIconPath("icon");
                if (!string.IsNullOrEmpty(icoPath) && File.Exists(icoPath))
                {
                    var src = LoadImageSource("icon", 64);
                    if (src != null) return src;
                }
            }
            catch { }

            // 绘制兜底图标
            return CreateDefaultIcon(64);
        }

        /// <summary>
        /// 将 Geometry 图标资源渲染为 WinForms 可用的 Bitmap。
        /// </summary>
        public static System.Drawing.Bitmap? GetIconBitmap(string resourceKey, int size)
        {
            try
            {
                if (Application.Current?.TryFindResource(resourceKey) is not Geometry geo)
                    return null;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    double scale = size / 24.0;
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.DrawGeometry(null, new Pen(Brushes.White, 1.6), geo);
                    dc.Pop();
                }

                var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                return new System.Drawing.Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }
    }
}
