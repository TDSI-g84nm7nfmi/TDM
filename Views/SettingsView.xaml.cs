using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TDM.Services;
using TDM.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TDM.Views
{
    public sealed partial class SettingsView : Page
    {
        public SettingsViewModel ViewModel => SettingsViewModel.Instance;

        public SettingsView()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var s = SettingsService.Current;
            DownloadPathBox.Text = s.DefaultSaveDir;
            ThreadSlider.Value = s.DefaultThreads;
        }

        private async void OnBrowsePathClick(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads
            };
            picker.FileTypeFilter.Add("*");
            var hwnd = WindowNative.GetWindowHandle(App.CurrentWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                DownloadPathBox.Text = folder.Path;
                SettingsService.Update(s => s.DefaultSaveDir = folder.Path);
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            SettingsService.Update(s =>
            {
                s.DefaultSaveDir = DownloadPathBox.Text;
                s.DefaultThreads = (int)ThreadSlider.Value;
            });
            SettingsService.Save();
        }
    }
}
