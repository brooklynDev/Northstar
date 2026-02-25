using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Northstar.Desktop.ViewModels;
using System;
using System.Linq;

namespace Northstar.Desktop.Views;

public partial class MainWindow
{
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

    private void ExplorerList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedExplorerItems.Clear();
        foreach (var selected in (ExplorerList.SelectedItems?.OfType<FileSystemItemViewModel>() ?? Enumerable.Empty<FileSystemItemViewModel>()))
        {
            viewModel.SelectedExplorerItems.Add(selected);
        }

        var primarySelection = ExplorerList.SelectedItem as FileSystemItemViewModel
            ?? viewModel.SelectedExplorerItems.FirstOrDefault();
        viewModel.SelectedExplorerItem = primarySelection;

        if (!_isApplyingRangeSelection)
        {
            var selectedIndex = ExplorerList.SelectedIndex;
            if (selectedIndex >= 0)
            {
                _selectionAnchorIndex = selectedIndex;
                _selectionRangeEndIndex = selectedIndex;
            }
        }

        if (_isQuickPreviewOpen)
        {
            _ = RefreshQuickPreviewAsync();
        }
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

            if (hasPrimaryModifier && e.Key == Key.OemComma)
            {
                _ = OpenPreferencesAsync(viewModel);
                e.Handled = true;
                return;
            }

            if (hasPrimaryModifier && e.Key == Key.T)
            {
                viewModel.NewTabCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (hasPrimaryModifier && e.Key == Key.W)
            {
                viewModel.CloseTabCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (ShouldRouteToExplorer(e.Source))
            {
                if (hasPrimaryModifier && e.Key == Key.C)
                {
                    viewModel.CopyItemCommand.Execute(null);
                    e.Handled = true;
                    return;
                }

                if (hasPrimaryModifier && e.Key == Key.X)
                {
                    viewModel.CutItemCommand.Execute(null);
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
                    _ = CreateNewFolderAndRenameAsync(viewModel);
                    e.Handled = true;
                    return;
                }

                if (e.Key is Key.Delete)
                {
                    viewModel.DeleteItemCommand.Execute(null);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.F2)
                {
                    _ = RenameSelectedAsync(viewModel.SelectedExplorerItem);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (!ShouldRouteToExplorer(e.Source))
        {
            return;
        }

        if (e.Key is not (Key.Up or Key.Down or Key.Back or Key.Escape or Key.Enter or Key.Space))
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

        if (e.Key == Key.Space)
        {
            ToggleQuickPreview();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_isQuickPreviewOpen)
            {
                SetQuickPreviewOpen(false);
                _typeAheadBuffer = string.Empty;
                e.Handled = true;
                return;
            }

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

            var shiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (shiftHeld)
            {
                if (_selectionAnchorIndex is null)
                {
                    _selectionAnchorIndex = currentIndex;
                }

                var currentEndIndex = _selectionRangeEndIndex ?? currentIndex;
                var nextEndIndex = e.Key == Key.Down
                    ? Math.Min(currentEndIndex + 1, viewModel.ExplorerItems.Count - 1)
                    : Math.Max(currentEndIndex - 1, 0);

                ApplyRangeSelection(viewModel, listBox, _selectionAnchorIndex.Value, nextEndIndex);
                _selectionRangeEndIndex = nextEndIndex;
                e.Handled = true;
                return;
            }

            var nextIndex = e.Key == Key.Down
                ? Math.Min(currentIndex + 1, viewModel.ExplorerItems.Count - 1)
                : Math.Max(currentIndex - 1, 0);

            _selectionAnchorIndex = nextIndex;
            _selectionRangeEndIndex = nextIndex;
            SelectItemByIndex(viewModel, listBox, nextIndex);
            e.Handled = true;
        }
    }

    private void ApplyRangeSelection(MainWindowViewModel viewModel, ListBox listBox, int anchorIndex, int endIndex)
    {
        if (anchorIndex < 0 || endIndex < 0 || viewModel.ExplorerItems.Count == 0)
        {
            return;
        }

        var start = Math.Min(anchorIndex, endIndex);
        var finish = Math.Max(anchorIndex, endIndex);

        var selectedItems = listBox.SelectedItems;
        if (selectedItems is null)
        {
            return;
        }

        _isApplyingRangeSelection = true;
        try
        {
            selectedItems.Clear();
            for (var i = start; i <= finish; i++)
            {
                if (i >= 0 && i < viewModel.ExplorerItems.Count)
                {
                    selectedItems.Add(viewModel.ExplorerItems[i]);
                }
            }

            if (endIndex >= 0 && endIndex < viewModel.ExplorerItems.Count)
            {
                var activeItem = viewModel.ExplorerItems[endIndex];
                listBox.ScrollIntoView(activeItem);
            }
        }
        finally
        {
            _isApplyingRangeSelection = false;
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
}
