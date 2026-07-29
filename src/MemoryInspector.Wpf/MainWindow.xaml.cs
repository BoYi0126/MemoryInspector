using System.Windows;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(ProcessExplorerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
