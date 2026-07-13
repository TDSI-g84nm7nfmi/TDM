using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TDM.ViewModels;

namespace TDM.Views
{
    public sealed partial class DownloadView : Page
    {
        public DownloadViewModel ViewModel => DownloadViewModel.Instance;

        public DownloadView()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            ViewModel.OnViewActivated();
        }

        private void OnNewTaskClick(object sender, RoutedEventArgs e)
        {
            ViewModel.AddNewTask();
        }

        private void OnPauseAllClick(object sender, RoutedEventArgs e)
        {
            ViewModel.PauseAll();
        }

        private void OnResumeAllClick(object sender, RoutedEventArgs e)
        {
            ViewModel.ResumeAll();
        }
    }
}
