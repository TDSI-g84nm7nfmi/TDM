using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using TDM.Services;

namespace TDM.Windows
{
    public partial class AboutWindow : Window
    {
        public ImageSource? AppIconSource { get; }

        public AboutWindow()
        {
            InitializeComponent();
            try { AppIconSource = IconHelper.GetAppIcon(); } catch { }
            VersionText.Text = $"v{App.AppVersion} Beta";
        }

        private void OnAuthorClick(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://space.bilibili.com/3537120060770693");
        }

        private void OnGithubClick(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/TDSI-g84nm7nfmi/TDM");
        }

        private void OnTdsiClick(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://tdsi.top");
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Warn($"打开链接失败: {ex.Message}");
            }
        }
    }
}
