using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Northstar.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Northstar.Desktop.Views;

public partial class MainWindow
{
    private async void PreferencesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await OpenPreferencesAsync(viewModel);
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

    private void ExplorerContextMenu_OnOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu contextMenu || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (contextMenu.PlacementTarget is Control placementTarget &&
            placementTarget.DataContext is FileSystemItemViewModel item)
        {
            viewModel.SelectedExplorerItem = item;
            ExplorerList.SelectedItem = item;
        }

        var hasSelection = viewModel.SelectedExplorerItem is not null;
        var canPaste = viewModel.PasteCommand.CanExecute(null);

        foreach (var menuItem in EnumerateMenuItems(contextMenu.Items))
        {
            var header = menuItem.Header?.ToString();
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (header is "Open" or "Rename" or "Copy" or "Cut" or "Copy Path" or "Move to Trash")
            {
                menuItem.IsEnabled = hasSelection;
                continue;
            }

            if (header == "Paste")
            {
                menuItem.IsEnabled = canPaste;
            }
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

    private async void RenameItemMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await RenameSelectedAsync((sender as MenuItem)?.Tag as FileSystemItemViewModel);
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

    private async void NewFolderMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await CreateNewFolderAndRenameAsync(viewModel);
    }

    private void RefreshMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.RefreshCommand.Execute(null);
    }

    private static IEnumerable<MenuItem> EnumerateMenuItems(object? items)
    {
        if (items is not IEnumerable<object> enumerable)
        {
            yield break;
        }

        foreach (var obj in enumerable)
        {
            if (obj is MenuItem menuItem)
            {
                yield return menuItem;
            }
        }
    }

    private async Task CreateNewFolderAndRenameAsync(MainWindowViewModel viewModel)
    {
        var createdItem = viewModel.CreateNewFolderAndSelect();
        if (createdItem is null)
        {
            return;
        }

        await RenameSelectedAsync(createdItem);
    }

    private async Task RenameSelectedAsync(FileSystemItemViewModel? item)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var target = item ?? viewModel.SelectedExplorerItem;
        if (target is null)
        {
            return;
        }

        var proposedName = await ShowRenameDialogAsync(target.Name);
        if (string.IsNullOrWhiteSpace(proposedName))
        {
            return;
        }

        _ = viewModel.TryRenameItem(target, proposedName);
    }

    private async Task OpenPreferencesAsync(MainWindowViewModel viewModel)
    {
        var settings = viewModel.GetPreferences();

        var dialog = new Window
        {
            Title = "Preferences",
            Width = 520,
            Height = 240,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendClientAreaToDecorationsHint = false,
        };

        var showHiddenCheck = new CheckBox
        {
            Content = "Show hidden files",
            IsChecked = settings.ShowHiddenFiles,
            Margin = new Thickness(0, 6, 0, 10),
        };

        var startFolderBox = new TextBox
        {
            Text = settings.DefaultStartFolder,
            Watermark = "Optional. Leave blank to start at your Home folder.",
        };

        var useCurrentButton = new Button
        {
            Content = "Use Current Folder",
            MinWidth = 140,
            Margin = new Thickness(0, 8, 0, 0),
        };
        useCurrentButton.Click += (_, _) => startFolderBox.Text = viewModel.CurrentPath;

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 88,
            Margin = new Thickness(0, 0, 8, 0),
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var saveButton = new Button
        {
            Content = "Save",
            MinWidth = 88,
        };
        saveButton.Click += (_, _) =>
        {
            viewModel.ApplyPreferences(
                showHiddenCheck.IsChecked == true,
                startFolderBox.Text);
            dialog.Close();
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "General",
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 0, 4),
                    },
                    showHiddenCheck,
                    new TextBlock
                    {
                        Text = "Default start folder",
                    },
                    startFolderBox,
                    useCurrentButton,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 12, 0, 0),
                        Children =
                        {
                            cancelButton,
                            saveButton,
                        },
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
        ExplorerList.Focus();
    }

    private async Task<string?> ShowRenameDialogAsync(string currentName)
    {
        var dialog = new Window
        {
            Title = "Rename",
            Width = 440,
            Height = 150,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendClientAreaToDecorationsHint = false,
        };

        string? result = null;
        var textBox = new TextBox
        {
            Text = currentName,
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            Margin = new Thickness(0, 0, 8, 0),
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var renameButton = new Button
        {
            Content = "Rename",
            MinWidth = 80,
        };
        renameButton.Click += (_, _) =>
        {
            result = textBox.Text?.Trim();
            dialog.Close();
        };

        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                result = textBox.Text?.Trim();
                dialog.Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Enter a new name:",
                    },
                    textBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            cancelButton,
                            renameButton,
                        },
                    },
                },
            },
        };

        dialog.Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                textBox.Focus();
                textBox.SelectAll();
            });
        };

        await dialog.ShowDialog(this);
        return result;
    }
}
