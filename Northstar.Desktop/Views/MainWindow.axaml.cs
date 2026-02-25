using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Northstar.Desktop.ViewModels;
using System;
using System.Linq;

namespace Northstar.Desktop.Views;

public partial class MainWindow : Window
{
    private const int TypeAheadResetMilliseconds = 1200;
    private string _typeAheadBuffer = string.Empty;
    private DateTime _lastTypeAheadInputUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) => ExplorerList.Focus();

        AddHandler(KeyDownEvent, Window_OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, Window_OnPreviewTextInput, RoutingStrategies.Tunnel);
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

    private void ExplorerList_OnTextInput(object? sender, TextInputEventArgs e)
    {
        HandleExplorerTextInput(e, sender as ListBox);
    }

    private void ExplorerList_OnKeyDown(object? sender, KeyEventArgs e)
    {
        HandleExplorerKeyDown(e, sender as ListBox);
    }

    private void Window_OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (!ShouldRouteToExplorer(e.Source))
        {
            return;
        }

        ExplorerList.Focus();
        HandleExplorerTextInput(e, ExplorerList);
    }

    private void Window_OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var hasPrimaryModifier = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

            if (hasPrimaryModifier && e.Key == Key.L)
            {
                viewModel.BeginPathEditCommand.Execute(null);
                FocusPathEditor();
                e.Handled = true;
                return;
            }

            if (ShouldRouteToExplorer(e.Source))
            {
                if (hasPrimaryModifier && e.Key == Key.C)
                {
                    viewModel.CopyItemCommand.Execute(viewModel.SelectedExplorerItem);
                    e.Handled = true;
                    return;
                }

                if (hasPrimaryModifier && e.Key == Key.X)
                {
                    viewModel.CutItemCommand.Execute(viewModel.SelectedExplorerItem);
                    e.Handled = true;
                    return;
                }

                if (hasPrimaryModifier && e.Key == Key.V)
                {
                    if (viewModel.PasteCommand.CanExecute(null))
                    {
                        viewModel.PasteCommand.Execute(null);
                    }

                    e.Handled = true;
                    return;
                }

                if (hasPrimaryModifier && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.N)
                {
                    viewModel.NewFolderCommand.Execute(null);
                    e.Handled = true;
                    return;
                }

                if (e.Key is Key.Delete)
                {
                    viewModel.DeleteItemCommand.Execute(viewModel.SelectedExplorerItem);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (!ShouldRouteToExplorer(e.Source))
        {
            return;
        }

        if (e.Key is not (Key.Up or Key.Down or Key.Back or Key.Escape or Key.Enter))
        {
            return;
        }

        ExplorerList.Focus();
        HandleExplorerKeyDown(e, ExplorerList);
    }

    private void HandleExplorerTextInput(TextInputEventArgs e, ListBox? listBox)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(e.Text))
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        if ((nowUtc - _lastTypeAheadInputUtc).TotalMilliseconds > TypeAheadResetMilliseconds)
        {
            _typeAheadBuffer = string.Empty;
        }

        _lastTypeAheadInputUtc = nowUtc;
        _typeAheadBuffer += e.Text;

        TrySelectByPrefix(viewModel, listBox, _typeAheadBuffer);
        e.Handled = true;
    }

    private void HandleExplorerKeyDown(KeyEventArgs e, ListBox? listBox)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _typeAheadBuffer = string.Empty;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (viewModel.OpenSelectedCommand.CanExecute(null))
            {
                viewModel.OpenSelectedCommand.Execute(null);
                _typeAheadBuffer = string.Empty;
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Back)
        {
            if (viewModel.GoBackCommand.CanExecute(null))
            {
                viewModel.GoBackCommand.Execute(null);
                _typeAheadBuffer = string.Empty;
                e.Handled = true;
            }

            return;
        }

        if (e.Key is Key.Down or Key.Up)
        {
            if (listBox is null || viewModel.ExplorerItems.Count == 0)
            {
                return;
            }

            var currentIndex = listBox.SelectedIndex;
            if (currentIndex < 0 && viewModel.SelectedExplorerItem is not null)
            {
                currentIndex = viewModel.ExplorerItems.IndexOf(viewModel.SelectedExplorerItem);
            }

            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var nextIndex = e.Key == Key.Down
                ? Math.Min(currentIndex + 1, viewModel.ExplorerItems.Count - 1)
                : Math.Max(currentIndex - 1, 0);

            SelectItemByIndex(viewModel, listBox, nextIndex);
            e.Handled = true;
        }
    }

    private static bool ShouldRouteToExplorer(object? eventSource)
    {
        var current = eventSource as Control;
        while (current is not null)
        {
            if (current is TextBox)
            {
                return false;
            }

            current = current.Parent as Control;
        }

        return true;
    }

    private void PathSurface_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.BeginPathEditCommand.Execute(null);
        FocusPathEditor();
        e.Handled = true;
    }

    private void PathEditBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            viewModel.CommitPathEditCommand.Execute(null);
            ExplorerList.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.CancelPathEditCommand.Execute(null);
            ExplorerList.Focus();
            e.Handled = true;
        }
    }

    private void PathEditBox_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsPathEditMode)
        {
            viewModel.CommitPathEditCommand.Execute(null);
        }
    }

    private void FocusPathEditor()
    {
        Dispatcher.UIThread.Post(() =>
        {
            PathEditBox.Focus();
            PathEditBox.SelectAll();
        });
    }

    private void OpenTerminalButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.OpenInTerminalCommand.Execute(null);
    }

    private static void TrySelectByPrefix(MainWindowViewModel viewModel, ListBox? listBox, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var match = viewModel.ExplorerItems
            .FirstOrDefault(item => item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return;
        }

        var index = viewModel.ExplorerItems.IndexOf(match);
        SelectItemByIndex(viewModel, listBox, index);
    }

    private static void SelectItemByIndex(MainWindowViewModel viewModel, ListBox? listBox, int index)
    {
        if (index < 0 || index >= viewModel.ExplorerItems.Count)
        {
            return;
        }

        var item = viewModel.ExplorerItems[index];

        if (listBox is not null)
        {
            listBox.SelectedIndex = index;
            listBox.SelectedItem = item;
            listBox.ScrollIntoView(item);
        }

        viewModel.SelectedExplorerItem = item;
    }

    private void ExplorerRow_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control row || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (row.DataContext is not FileSystemItemViewModel item)
        {
            return;
        }

        if (e.GetCurrentPoint(row).Properties.IsRightButtonPressed)
        {
            viewModel.SelectedExplorerItem = item;
            ExplorerList.SelectedItem = item;
        }
    }

    private void OpenItemMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.OpenItemCommand.Execute((sender as MenuItem)?.Tag as FileSystemItemViewModel);
    }

    private void CopyItemMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.CopyItemCommand.Execute((sender as MenuItem)?.Tag as FileSystemItemViewModel);
    }

    private void CutItemMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.CutItemCommand.Execute((sender as MenuItem)?.Tag as FileSystemItemViewModel);
    }

    private void DeleteItemMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.DeleteItemCommand.Execute((sender as MenuItem)?.Tag as FileSystemItemViewModel);
    }

    private void CopyPathMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.CopyPathCommand.Execute((sender as MenuItem)?.Tag as FileSystemItemViewModel);
    }

    private void PasteMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.PasteCommand.CanExecute(null))
        {
            viewModel.PasteCommand.Execute(null);
        }
    }

    private void NewFolderMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.NewFolderCommand.Execute(null);
    }

    private void RefreshMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.RefreshCommand.Execute(null);
    }
}
