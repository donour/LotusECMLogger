using System.Windows;
using LotusECMLogger.ViewModels;

namespace LotusECMLogger.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            
            // Create and set the view model
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;

            // Handle window closing
            Closing += (s, e) => _viewModel.Dispose();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            var about = new AboutBox1();
            about.ShowDialog();
        }
    }
} 