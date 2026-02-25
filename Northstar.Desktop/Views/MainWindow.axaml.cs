using Avalonia.Controls;
using Avalonia.Input;
using Northstar.Desktop.ViewModels;

namespace Northstar.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ExplorerList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.OpenSelectedCommand.CanExecute(null))
        {
            viewModel.OpenSelectedCommand.Execute(null);
        }
    }
}
