using CommunityToolkit.Mvvm.Input;
using Northstar.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Northstar.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private bool CanGoBack() => _backHistory.Count > 0;

    private bool CanGoForward() => _forwardHistory.Count > 0;

    private bool CanGoUp()
    {
        var parent = Directory.GetParent(CurrentPath);
        return parent is not null;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (_backHistory.Count == 0)
        {
            return;
        }

        _forwardHistory.Push(CurrentPath);
        var previousPath = _backHistory.Pop();
        OpenDirectory(previousPath, addToHistory: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (_forwardHistory.Count == 0)
        {
            return;
        }

        _backHistory.Push(CurrentPath);
        var nextPath = _forwardHistory.Pop();
        OpenDirectory(nextPath, addToHistory: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is null)
        {
            return;
        }

        OpenDirectory(parent.FullName);
    }

    [RelayCommand]
    private void Refresh()
    {
        OpenDirectory(CurrentPath, addToHistory: false);
    }

    [RelayCommand]
    private void OpenQuickAccess(QuickAccessItemViewModel? target)
    {
        if (target is null)
        {
            return;
        }

        OpenDirectory(target.FullPath);
    }

    [RelayCommand]
    private void OpenItem(FileSystemItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            OpenDirectory(item.FullPath);
            return;
        }

        TryLaunchFile(item.FullPath);
    }

    [RelayCommand]
    private void OpenSelected()
    {
        OpenItem(SelectedExplorerItem);
    }

    [RelayCommand]
    private void OpenInTerminal()
    {
        try
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            var preferredProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM");
            var candidates = BuildTerminalCandidates(preferredProgram);

            foreach (var appName in candidates)
            {
                var started = Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    UseShellExecute = false,
                    ArgumentList = { "-a", appName, CurrentPath },
                });

                if (started is not null)
                {
                    return;
                }
            }
        }
        catch
        {
            // Ignore terminal launch errors in this basic version.
        }
    }

    [RelayCommand]
    private void CopyItem(FileSystemItemViewModel? item)
    {
        var targets = ResolveTargets(item);
        if (targets.Count == 0)
        {
            return;
        }

        _clipboardPaths.Clear();
        _clipboardPaths.AddRange(targets.Select(target => target.FullPath));
        _clipboardIsCut = false;
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void CutItem(FileSystemItemViewModel? item)
    {
        var targets = ResolveTargets(item);
        if (targets.Count == 0)
        {
            return;
        }

        _clipboardPaths.Clear();
        _clipboardPaths.AddRange(targets.Select(target => target.FullPath));
        _clipboardIsCut = true;
        PasteCommand.NotifyCanExecuteChanged();
    }

    private bool CanPaste() => _clipboardPaths.Count > 0;

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (_clipboardPaths.Count == 0 || !Directory.Exists(CurrentPath))
        {
            return;
        }

        var allSucceeded = true;
        foreach (var sourcePath in _clipboardPaths.ToList())
        {
            try
            {
                var sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    allSucceeded = false;
                    continue;
                }

                var destinationPath = GetUniqueDestinationPath(Path.Combine(CurrentPath, sourceName));

                if (_clipboardIsCut)
                {
                    MoveEntry(sourcePath, destinationPath);
                }
                else
                {
                    CopyEntry(sourcePath, destinationPath);
                }
            }
            catch
            {
                allSucceeded = false;
            }
        }

        if (_clipboardIsCut && allSucceeded)
        {
            _clipboardPaths.Clear();
            _clipboardIsCut = false;
            PasteCommand.NotifyCanExecuteChanged();
        }

        OpenDirectory(CurrentPath, addToHistory: false);
    }

    [RelayCommand]
    private void DeleteItem(FileSystemItemViewModel? item)
    {
        var targets = ResolveTargets(item);
        if (targets.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var target in targets)
            {
                MoveToTrash(target.FullPath);
            }

            OpenDirectory(CurrentPath, addToHistory: false);
        }
        catch
        {
            // Ignore delete errors in this basic version.
        }
    }

    [RelayCommand]
    private void NewFolder()
    {
        CreateNewFolderAndSelect();
    }

    public FileSystemItemViewModel? CreateNewFolderAndSelect()
    {
        if (!Directory.Exists(CurrentPath))
        {
            return null;
        }

        try
        {
            var folderPath = GetUniqueDestinationPath(Path.Combine(CurrentPath, "New Folder"));
            Directory.CreateDirectory(folderPath);
            OpenDirectory(CurrentPath, addToHistory: false);

            var createdItem = ExplorerItems.FirstOrDefault(item =>
                string.Equals(item.FullPath, folderPath, StringComparison.OrdinalIgnoreCase));

            if (createdItem is not null)
            {
                SelectedExplorerItem = createdItem;
            }

            return createdItem;
        }
        catch
        {
            // Ignore new-folder errors in this basic version.
            return null;
        }
    }

    [RelayCommand]
    private void CopyPath(FileSystemItemViewModel? item)
    {
        var targets = ResolveTargets(item);
        if (targets.Count == 0)
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pbcopy",
                RedirectStandardInput = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return;
            }

            process.StandardInput.Write(string.Join(Environment.NewLine, targets.Select(target => target.FullPath)));
            process.StandardInput.Close();
            process.WaitForExit(1000);
        }
        catch
        {
            // Ignore clipboard errors in this basic version.
        }
    }

    public bool TryRenameItem(FileSystemItemViewModel? item, string? newName)
    {
        var target = item ?? SelectedExplorerItem;
        if (target is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        var trimmedName = newName.Trim();
        if (string.Equals(trimmedName, target.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var parentDirectory = Path.GetDirectoryName(target.FullPath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return false;
        }

        var destinationPath = Path.Combine(parentDirectory, trimmedName);
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            return false;
        }

        try
        {
            if (target.IsDirectory)
            {
                Directory.Move(target.FullPath, destinationPath);
            }
            else
            {
                File.Move(target.FullPath, destinationPath);
            }

            OpenDirectory(CurrentPath, addToHistory: false);
            SelectedExplorerItem = ExplorerItems.FirstOrDefault(i =>
                string.Equals(i.FullPath, destinationPath, StringComparison.OrdinalIgnoreCase));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private IReadOnlyList<FileSystemItemViewModel> ResolveTargets(FileSystemItemViewModel? item)
    {
        if (item is not null)
        {
            if (SelectedExplorerItems.Count > 1 &&
                SelectedExplorerItems.Any(selected =>
                    string.Equals(selected.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                return SelectedExplorerItems.ToList();
            }

            return [item];
        }

        if (SelectedExplorerItems.Count > 0)
        {
            return SelectedExplorerItems.ToList();
        }

        if (SelectedExplorerItem is not null)
        {
            return [SelectedExplorerItem];
        }

        return [];
    }

    [RelayCommand]
    private void SortBy(string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            return;
        }

        if (string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }

        ApplySearchFilter();
    }

    [RelayCommand]
    private void BeginPathEdit()
    {
        PathInputText = CurrentPath;
        IsPathEditMode = true;
    }

    [RelayCommand]
    private void CancelPathEdit()
    {
        IsPathEditMode = false;
        PathInputText = CurrentPath;
    }

    [RelayCommand]
    private void CommitPathEdit()
    {
        var requestedPath = PathInputText.Trim();
        IsPathEditMode = false;

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            PathInputText = CurrentPath;
            return;
        }

        if (Directory.Exists(requestedPath))
        {
            OpenDirectory(requestedPath);
            return;
        }

        PathInputText = CurrentPath;
    }

    [RelayCommand]
    private void NavigateToBreadcrumb(BreadcrumbItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        OpenDirectory(item.FullPath);
    }

    public void NavigateToPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        OpenDirectory(path);
    }

}
