using System.Windows;
using AttendanceAgent.ViewModels;

namespace AttendanceAgent;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
