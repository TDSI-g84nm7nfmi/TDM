using System.Windows.Controls;
using TDM.ViewModels;

namespace TDM.Views
{
    public partial class HistoryView : UserControl
    {
        public HistoryViewModel ViewModel => (HistoryViewModel)DataContext;

        public HistoryView()
        {
            InitializeComponent();
            DataContext = new HistoryViewModel();
        }

        public void Refresh() => ViewModel.Refresh();

        public void ApplySearchFilter(string keyword)
        {
            try
            {
                ViewModel.Filter = keyword;
            }
            catch { }
        }
    }
}
