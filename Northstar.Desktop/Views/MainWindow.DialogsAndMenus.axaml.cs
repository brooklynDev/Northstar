using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Northstar.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
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
            EnsureItemIsContextSelected(viewModel, item);
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
            EnsureItemIsContextSelected(viewModel, item);
        }

        var hasSelection = viewModel.SelectedExplorerItem is not null || viewModel.SelectedExplorerItems.Count > 0;
        var canPaste = viewModel.PasteCommand.CanExecute(null);

        foreach (var menuItem in EnumerateMenuItems(contextMenu.Items))
        {
            var header = menuItem.Header?.ToString();
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (header is "Open" or "Open in New Tab" or "Rename" or "Get Info" or "Copy" or "Cut" or "Copy Path" or "Move to Trash")
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

    private void OpenInNewTabMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.OpenItemInNewTabCommand.Execute((sender as MenuItem)?.Tag as FileSystemItemViewModel);
    }

    private async void RenameItemMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await RenameSelectedAsync((sender as MenuItem)?.Tag as FileSystemItemViewModel);
    }

    private async void GetInfoMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var target = (sender as MenuItem)?.Tag as FileSystemItemViewModel;
        await ShowGetInfoDialogAsync(viewModel, target);
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

    private async void PasteMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.PasteCommand.CanExecute(null))
        {
            await PasteWithConflictWorkflowAsync(viewModel);
        }
    }

    private Task<bool> PasteWithConflictWorkflowAsync(MainWindowViewModel viewModel)
        => viewModel.PasteWithConflictResolutionAsync(ShowPasteConflictDialogAsync);

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

    private void NewTabButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.NewTabCommand.Execute(null);
    }

    private void CloseTabButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.CloseTabCommand.Execute((sender as Button)?.Tag as ExplorerTabViewModel);
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

    private void EnsureItemIsContextSelected(MainWindowViewModel viewModel, FileSystemItemViewModel item)
    {
        var selectedItems = ExplorerList.SelectedItems;
        if (selectedItems is null)
        {
            viewModel.SelectedExplorerItem = item;
            ExplorerList.SelectedItem = item;
            return;
        }

        var isAlreadySelected = selectedItems
            .OfType<FileSystemItemViewModel>()
            .Any(selected => string.Equals(selected.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase));

        if (!isAlreadySelected)
        {
            selectedItems.Clear();
            selectedItems.Add(item);
        }

        viewModel.SelectedExplorerItems.Clear();
        foreach (var selected in selectedItems.OfType<FileSystemItemViewModel>())
        {
            viewModel.SelectedExplorerItems.Add(selected);
        }

        viewModel.SelectedExplorerItem = item;
        ExplorerList.SelectedItem = item;
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
            Height = 320,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendClientAreaToDecorationsHint = false,
        };
        dialog.Background = GetThemeBrush("WindowBackgroundBrush", new SolidColorBrush(Color.Parse("#10131A")));
        dialog.Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White);

        var showHiddenCheck = new CheckBox
        {
            Content = "Show hidden files",
            IsChecked = settings.ShowHiddenFiles,
            Margin = new Thickness(0, 6, 0, 10),
            Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White),
        };

        var startFolderBox = new TextBox
        {
            Text = settings.DefaultStartFolder,
            Watermark = "Optional. Leave blank to start at your Home folder.",
            Background = GetThemeBrush("PathSurfaceBackgroundBrush", new SolidColorBrush(Color.Parse("#0E131D"))),
            Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White),
            BorderBrush = GetThemeBrush("PathSurfaceBorderBrush", new SolidColorBrush(Color.Parse("#2A344A"))),
        };

        var useCurrentButton = new Button
        {
            Content = "Use Current Folder",
            MinWidth = 140,
            Margin = new Thickness(0, 8, 0, 0),
        };
        useCurrentButton.Click += (_, _) => startFolderBox.Text = viewModel.CurrentPath;

        var themeComboBox = new ComboBox
        {
            ItemsSource = viewModel.AvailableThemes,
            SelectedItem = viewModel.AvailableThemes.Contains(settings.ThemeName)
                ? settings.ThemeName
                : viewModel.SelectedTheme,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = GetThemeBrush("PathSurfaceBackgroundBrush", new SolidColorBrush(Color.Parse("#0E131D"))),
            Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White),
        };

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
                startFolderBox.Text,
                themeComboBox.SelectedItem as string ?? settings.ThemeName);
            ApplyColorTheme(viewModel.SelectedTheme);
            dialog.Close();
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(14),
            Background = GetThemeBrush("ExplorerPaneBackgroundBrush", new SolidColorBrush(Color.Parse("#121A28"))),
            BorderBrush = GetThemeBrush("ChromeBorderBrush", new SolidColorBrush(Color.Parse("#2A3140"))),
            BorderThickness = new Thickness(1),
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
                    new TextBlock
                    {
                        Text = "Color theme",
                        Margin = new Thickness(0, 10, 0, 0),
                    },
                    themeComboBox,
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
        dialog.Background = GetThemeBrush("WindowBackgroundBrush", new SolidColorBrush(Color.Parse("#10131A")));
        dialog.Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White);

        string? result = null;
        var textBox = new TextBox
        {
            Text = currentName,
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = GetThemeBrush("PathSurfaceBackgroundBrush", new SolidColorBrush(Color.Parse("#0E131D"))),
            Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White),
            BorderBrush = GetThemeBrush("PathSurfaceBorderBrush", new SolidColorBrush(Color.Parse("#2A344A"))),
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
            Background = GetThemeBrush("ExplorerPaneBackgroundBrush", new SolidColorBrush(Color.Parse("#121A28"))),
            BorderBrush = GetThemeBrush("ChromeBorderBrush", new SolidColorBrush(Color.Parse("#2A3140"))),
            BorderThickness = new Thickness(1),
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

    private async Task ShowGetInfoDialogAsync(MainWindowViewModel viewModel, FileSystemItemViewModel? contextItem)
    {
        var selectedItems = viewModel.SelectedExplorerItems
            .Where(item => item is not null)
            .ToList();

        var target = contextItem ?? viewModel.SelectedExplorerItem;
        if (target is not null && selectedItems.All(item => !PathsEqual(item.FullPath, target.FullPath)))
        {
            selectedItems = [target];
        }
        else if (selectedItems.Count == 0 && target is not null)
        {
            selectedItems.Add(target);
        }

        if (selectedItems.Count == 0)
        {
            return;
        }

        var dialog = new Window
        {
            Title = selectedItems.Count == 1 ? "Get Info" : $"Get Info ({selectedItems.Count} Items)",
            Width = 540,
            Height = 360,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendClientAreaToDecorationsHint = false,
        };
        dialog.Background = GetThemeBrush("WindowBackgroundBrush", new SolidColorBrush(Color.Parse("#10131A")));
        dialog.Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White);

        var lines = BuildInfoLines(selectedItems);
        var contentPanel = new StackPanel { Spacing = 8 };

        foreach (var (label, value) in lines)
        {
            contentPanel.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeight.SemiBold,
                Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White),
            });
            contentPanel.Children.Add(new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GetThemeBrush("ColumnHeaderForegroundBrush", Brushes.White),
                Margin = new Thickness(0, 0, 0, 6),
            });
        }

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 88,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        closeButton.Click += (_, _) => dialog.Close();

        dialog.Content = new Border
        {
            Padding = new Thickness(14),
            Background = GetThemeBrush("ExplorerPaneBackgroundBrush", new SolidColorBrush(Color.Parse("#121A28"))),
            BorderBrush = GetThemeBrush("ChromeBorderBrush", new SolidColorBrush(Color.Parse("#2A3140"))),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = contentPanel,
                    },
                    closeButton,
                },
            },
        };
        Grid.SetRow(closeButton, 1);

        await dialog.ShowDialog(this);
    }

    private static List<(string Label, string Value)> BuildInfoLines(IReadOnlyList<FileSystemItemViewModel> items)
    {
        if (items.Count == 1)
        {
            return BuildSingleItemInfoLines(items[0]);
        }

        var directoryCount = items.Count(item => item.IsDirectory);
        var fileCount = items.Count - directoryCount;
        var totalKnownSize = items.Where(item => !item.IsDirectory).Sum(item => item.SortSize);
        var latestModified = items.Max(item => item.SortModifiedUtc).ToLocalTime().ToString("MMM d, yyyy h:mm tt");

        return
        [
            ("Selection", $"{items.Count} items"),
            ("Contains", $"{directoryCount} folder(s), {fileCount} file(s)"),
            ("Combined file size", FormatBytes(totalKnownSize)),
            ("Latest modified", latestModified),
        ];
    }

    private static List<(string Label, string Value)> BuildSingleItemInfoLines(FileSystemItemViewModel item)
    {
        var path = item.FullPath;
        var parent = Path.GetDirectoryName(path) ?? "/";
        var lines = new List<(string Label, string Value)>
        {
            ("Name", item.Name),
            ("Kind", item.Type),
            ("Path", path),
            ("Location", parent),
            ("Modified", item.Modified),
        };

        try
        {
            if (item.IsDirectory)
            {
                var directoryInfo = new DirectoryInfo(path);
                lines.Add(("Created", directoryInfo.CreationTime.ToString("MMM d, yyyy h:mm tt")));
                lines.Add(("Contents", $"{SafeCount(() => Directory.EnumerateFileSystemEntries(path).Count())} top-level item(s)"));
            }
            else
            {
                var fileInfo = new FileInfo(path);
                lines.Add(("Created", fileInfo.CreationTime.ToString("MMM d, yyyy h:mm tt")));
                lines.Add(("Size", $"{item.Size} ({fileInfo.Length:N0} bytes)"));
            }
        }
        catch
        {
            lines.Add(("Status", "Some details are unavailable for this item."));
        }

        return lines;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static int SafeCount(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(bytes, 0);
        var suffixIndex = 0;

        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return $"{value:0.#} {suffixes[suffixIndex]}";
    }

    private IBrush GetThemeBrush(string key, IBrush fallback)
    {
        var themeVariant = ActualThemeVariant;

        if (Resources.TryGetResource(key, themeVariant, out var resource) && resource is IBrush localBrush)
        {
            return localBrush;
        }

        if (Application.Current?.Resources.TryGetResource(key, themeVariant, out resource) == true && resource is IBrush appBrush)
        {
            return appBrush;
        }

        return fallback;
    }

    private async Task<PasteConflictResolution> ShowPasteConflictDialogAsync(PasteConflictRequest request)
    {
        var result = new PasteConflictResolution(PasteConflictAction.KeepBoth, ApplyToAll: false);
        var operationLabel = request.IsCutOperation ? "move" : "copy";
        var itemName = Path.GetFileName(request.SourcePath.TrimEnd(Path.DirectorySeparatorChar));

        var dialog = new Window
        {
            Title = "Name Conflict",
            Width = 560,
            Height = 240,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendClientAreaToDecorationsHint = false,
            Background = GetThemeBrush("WindowBackgroundBrush", new SolidColorBrush(Color.Parse("#10131A"))),
            Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White),
        };

        var applyToAllCheck = new CheckBox
        {
            Content = "Apply this choice to all conflicts",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = GetThemeBrush("ColumnHeaderForegroundBrush", Brushes.White),
        };

        var replaceButton = new Button
        {
            Content = "Replace",
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0),
        };
        replaceButton.Click += (_, _) =>
        {
            result = new PasteConflictResolution(PasteConflictAction.Replace, applyToAllCheck.IsChecked == true);
            dialog.Close();
        };

        var keepBothButton = new Button
        {
            Content = "Keep Both",
            MinWidth = 96,
            Margin = new Thickness(0, 0, 8, 0),
        };
        keepBothButton.Click += (_, _) =>
        {
            result = new PasteConflictResolution(PasteConflictAction.KeepBoth, applyToAllCheck.IsChecked == true);
            dialog.Close();
        };

        var skipButton = new Button
        {
            Content = "Skip",
            MinWidth = 84,
        };
        skipButton.Click += (_, _) =>
        {
            result = new PasteConflictResolution(PasteConflictAction.Skip, applyToAllCheck.IsChecked == true);
            dialog.Close();
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(14),
            Background = GetThemeBrush("ExplorerPaneBackgroundBrush", new SolidColorBrush(Color.Parse("#121A28"))),
            BorderBrush = GetThemeBrush("ChromeBorderBrush", new SolidColorBrush(Color.Parse("#2A3140"))),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"A file or folder named \"{itemName}\" already exists in this location.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = GetThemeBrush("NavForegroundBrush", Brushes.White),
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = $"Choose what to do with this {operationLabel} operation.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = GetThemeBrush("ColumnHeaderForegroundBrush", Brushes.White),
                    },
                    applyToAllCheck,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 14, 0, 0),
                        Children =
                        {
                            skipButton,
                            keepBothButton,
                            replaceButton,
                        },
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
        return result;
    }
}
