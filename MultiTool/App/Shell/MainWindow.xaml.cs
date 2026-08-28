using System.Windows;
using MultiTool.Presentation.ViewModels;

namespace MultiTool.Presentation.Shell;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
