using Microsoft.UI.Xaml.Controls;
using TDM.ViewModels;

namespace TDM.Views
{
    public sealed partial class HistoryView : Page
    {
        public HistoryViewModel ViewModel => HistoryViewModel.Instance;

        public HistoryView()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            ViewModel.Load();
        }
    }
}
