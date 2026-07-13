using System;
using System.IO;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TDM.Services
{
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
                var uri = new Uri(path, UriKind.Absolute);
                var bitmap = new BitmapImage(uri);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static ImageSource GetAppIcon()
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "image", "icon.png"),
                Path.Combine(baseDir, "image", "icon.ico"),
                Path.Combine(baseDir, "Assets", "icon.png"),
                Path.Combine(baseDir, "Assets", "icon.ico"),
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        return new BitmapImage(new Uri(path, UriKind.Absolute));
                    }
                    catch { }
                }
            }
            return null!;
        }
    }
}
