using System.Windows;
using System.Windows.Controls;
using TDM.Services;

namespace TDM.Views
{
    public partial class WelcomeView : UserControl
    {
        public WelcomeView()
        {
            InitializeComponent();
        }

        private void OnScanBrowsersClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var browsers = BrowserScanner.Scan();
                var win = new Windows.ExtensionInstallerWindow(browsers)
                {
                    Owner = Window.GetWindow(this)
                };
                win.ShowDialog();
            }
            catch (System.Exception ex)
            {
                Logger.Warn("扫描浏览器失败: " + ex.Message);
                MessageBox.Show("扫描浏览器失败：" + ex.Message, "TDM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}