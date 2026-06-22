using System.Windows;
using System.Windows.Input;

namespace TDM.Windows
{
    public partial class CloseChoiceWindow : Window
    {
        public string SelectedAction { get; private set; } = "tray";
        public bool RememberChoice => RememberCheck?.IsChecked == true;

        public CloseChoiceWindow()
        {
            InitializeComponent();
        }

        private void OnDragMove(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnTrayClick(object sender, RoutedEventArgs e)
        {
            SelectedAction = "tray";
            DialogResult = true;
            Close();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            SelectedAction = "exit";
            DialogResult = true;
            Close();
        }
    }
}
