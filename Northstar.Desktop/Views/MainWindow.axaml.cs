using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia;
using Northstar.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Northstar.Desktop.Views;

public partial class MainWindow : Window
{
    private const int TypeAheadResetMilliseconds = 1200;
    private static readonly HashSet<string> PreviewImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".tiff", ".tif", ".ico"
    };
    private static readonly HashSet<string> PreviewTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".yml", ".yaml", ".csv", ".log", ".ini",
        ".cs", ".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".sql", ".sh", ".bash", ".zsh", ".ps1", ".toml"
    };

    private string _typeAheadBuffer = string.Empty;
    private DateTime _lastTypeAheadInputUtc = DateTime.MinValue;
    private bool _isQuickPreviewOpen;
    private int _previewLoadVersion;
    private Bitmap? _previewBitmap;

    public MainWindow()
    {
        InitializeComponent();
        SetPreviewPaneVisible(false);

        Opened += (_, _) => ExplorerList.Focus();
        Closing += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.Shutdown();
            }

            _previewBitmap?.Dispose();
            _previewBitmap = null;
        };

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

    private void ExplorerList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
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
                    _ = CreateNewFolderAndRenameAsync(viewModel);
                    e.Handled = true;
                    return;
                }

                if (e.Key is Key.Delete)
                {
                    viewModel.DeleteItemCommand.Execute(viewModel.SelectedExplorerItem);
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

    private void ToggleQuickPreview()
    {
        SetQuickPreviewOpen(!_isQuickPreviewOpen);
    }

    private void SetQuickPreviewOpen(bool isOpen)
    {
        _isQuickPreviewOpen = isOpen;

        if (!isOpen)
        {
            SetPreviewPaneVisible(false);
            ClearPreviewSurface();
            return;
        }

        _ = RefreshQuickPreviewAsync();
    }

    private async Task RefreshQuickPreviewAsync()
    {
        if (!_isQuickPreviewOpen || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var requestVersion = Interlocked.Increment(ref _previewLoadVersion);
        var selectedItem = viewModel.SelectedExplorerItem;

        if (selectedItem is null ||
            !viewModel.ExplorerItems.Any(item => string.Equals(item.FullPath, selectedItem.FullPath, StringComparison.OrdinalIgnoreCase)))
        {
            SetPreviewPaneVisible(false);
            return;
        }

        var meta = $"{selectedItem.Type} • {selectedItem.Size} • {selectedItem.Modified}";
        if (selectedItem.IsDirectory)
        {
            SetPreviewPaneVisible(false);
            return;
        }

        var extension = Path.GetExtension(selectedItem.FullPath);
        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfPreview = await Task.Run(() => LoadPdfPreviewBitmap(selectedItem.FullPath));
            if (!IsCurrentPreviewRequest(requestVersion))
            {
                pdfPreview?.Dispose();
                return;
            }

            if (pdfPreview is null)
            {
                SetPreviewPaneVisible(false);
                return;
            }

            ShowImagePreview(selectedItem.Name, meta, pdfPreview);
            return;
        }

        if (PreviewImageExtensions.Contains(extension))
        {
            var imagePreview = await Task.Run(() => LoadBitmapSafe(selectedItem.FullPath));
            if (!IsCurrentPreviewRequest(requestVersion))
            {
                imagePreview?.Dispose();
                return;
            }

            if (imagePreview is null)
            {
                SetPreviewPaneVisible(false);
                return;
            }

            ShowImagePreview(selectedItem.Name, meta, imagePreview);
            return;
        }

        if (PreviewTextExtensions.Contains(extension))
        {
            var textPreview = await Task.Run(() => ReadTextPreview(selectedItem.FullPath));
            if (!IsCurrentPreviewRequest(requestVersion))
            {
                return;
            }

            if (textPreview is null)
            {
                SetPreviewPaneVisible(false);
                return;
            }

            ShowTextPreview(selectedItem.Name, meta, textPreview);
            return;
        }

        SetPreviewPaneVisible(false);
    }

    private bool IsCurrentPreviewRequest(int requestVersion)
    {
        return _isQuickPreviewOpen && requestVersion == _previewLoadVersion;
    }

    private void ShowPreviewMessage(string title, string message, string meta)
    {
        PreviewTitle.Text = title;
        PreviewMeta.Text = meta;
        PreviewMessageText.Text = message;
        PreviewMessageSurface.IsVisible = true;
        PreviewTextBox.IsVisible = false;
        PreviewImage.IsVisible = false;
        PreviewTextBox.Text = string.Empty;
        SetPreviewBitmap(null);
    }

    private void ShowTextPreview(string title, string meta, string text)
    {
        SetPreviewPaneVisible(true);
        PreviewTitle.Text = title;
        PreviewMeta.Text = meta;
        PreviewTextBox.Text = text;
        PreviewMessageSurface.IsVisible = false;
        PreviewTextBox.IsVisible = true;
        PreviewImage.IsVisible = false;
        SetPreviewBitmap(null);
    }

    private void ShowImagePreview(string title, string meta, Bitmap bitmap)
    {
        SetPreviewPaneVisible(true);
        PreviewTitle.Text = title;
        PreviewMeta.Text = meta;
        PreviewMessageSurface.IsVisible = false;
        PreviewTextBox.IsVisible = false;
        PreviewImage.IsVisible = true;
        SetPreviewBitmap(bitmap);
    }

    private void ClearPreviewSurface()
    {
        PreviewTitle.Text = "Quick Preview";
        PreviewMeta.Text = "Press Space to toggle preview.";
        PreviewMessageText.Text = "Select a file and press Space.";
        PreviewTextBox.Text = string.Empty;
        PreviewMessageSurface.IsVisible = true;
        PreviewTextBox.IsVisible = false;
        PreviewImage.IsVisible = false;
        SetPreviewBitmap(null);
    }

    private void SetPreviewPaneVisible(bool isVisible)
    {
        PreviewPane.IsVisible = isVisible;

        if (MainContentGrid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        MainContentGrid.ColumnDefinitions[2].Width = isVisible
            ? new GridLength(360)
            : new GridLength(0);
    }

    private void SetPreviewBitmap(Bitmap? bitmap)
    {
        _previewBitmap?.Dispose();
        _previewBitmap = bitmap;
        PreviewImage.Source = bitmap;
    }

    private static Bitmap? LoadBitmapSafe(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadTextPreview(string fullPath)
    {
        const int maxCharacters = 30000;

        try
        {
            using var stream = File.OpenRead(fullPath);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[maxCharacters];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, read);
            if (!reader.EndOfStream)
            {
                text += $"{Environment.NewLine}{Environment.NewLine}...preview truncated...";
            }

            return text;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadPdfPreviewBitmap(string fullPath)
    {
        string? tempDirectory = null;

        try
        {
            tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "northstar-preview",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "qlmanage",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-t", "-s", "1200", "-o", tempDirectory, fullPath },
            });

            if (process is null)
            {
                return null;
            }

            process.WaitForExit(7000);

            var previewImagePath = Directory
                .EnumerateFiles(tempDirectory, "*.png", SearchOption.AllDirectories)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(previewImagePath))
            {
                return null;
            }

            using var stream = File.OpenRead(previewImagePath);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch
                {
                    // Ignore temporary preview cleanup errors.
                }
            }
        }
    }

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
