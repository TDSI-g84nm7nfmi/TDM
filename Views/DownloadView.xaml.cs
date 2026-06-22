using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TDM.Services;
using TDM.ViewModels;

namespace TDM.Views
{
    public partial class DownloadView : UserControl
    {
        public DownloadViewModel ViewModel => (DownloadViewModel)DataContext;

        public string CurrentUrl => ViewModel.Url;

        public DownloadView()
        {
            InitializeComponent();
            DataContext = new DownloadViewModel();
        }

        public void SetUrl(string url)
        {
            ViewModel.Url = url;
            UrlTextBox.Focus();
            UrlTextBox.CaretIndex = url.Length;
        }

        public void FocusUrlBox()
        {
            try
            {
                UrlTextBox.Focus();
                UrlTextBox.SelectAll();
            }
            catch { }
        }

        public void TogglePause() => ViewModel.TogglePause();

        private void OnUrlKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ViewModel.CanStart())
            {
                ViewModel.Start();
                e.Handled = true;
            }
        }

        private void OnPasteClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = Clipboard.GetText()?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    SetUrl(text);
                }
            }
            catch { }
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择保存目录",
                SelectedPath = Directory.Exists(ViewModel.SavePath) ? ViewModel.SavePath : SettingsService.Current.DefaultSaveDir
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ViewModel.SavePath = dlg.SelectedPath;
            }
        }

        private void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            var dir = ViewModel.SavePath;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start("explorer.exe", dir);
            }
        }
    }
}
