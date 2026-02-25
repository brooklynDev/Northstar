using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Northstar.Desktop.Services;

namespace Northstar.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly MacFileIconProvider _iconProvider = new();
    private List<FileSystemItemViewModel> _allItems = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoUpCommand))]
    private string currentPath = "/";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPathReadMode))]
    private bool isPathEditMode;

    [ObservableProperty]
    private string pathInputText = string.Empty;

    [ObservableProperty]
    private FileSystemItemViewModel? selectedExplorerItem;

    public ObservableCollection<QuickAccessItemViewModel> QuickAccessItems { get; } = [];

    public ObservableCollection<QuickAccessItemViewModel> SidebarFolders { get; } = [];

    public ObservableCollection<BreadcrumbItemViewModel> BreadcrumbItems { get; } = [];

    public ObservableCollection<FileSystemItemViewModel> ExplorerItems { get; } = [];

    public bool IsPathReadMode => !IsPathEditMode;

    public MainWindowViewModel()
    {
        BuildQuickAccess();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        OpenDirectory(home, addToHistory: false);
    }

    partial void OnSearchTextChanged(string value) => ApplySearchFilter();

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

    private void OpenDirectory(string path, bool addToHistory = true)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            var normalizedPath = Path.GetFullPath(path);
            if (addToHistory && !string.Equals(CurrentPath, normalizedPath, StringComparison.Ordinal))
            {
                _backHistory.Push(CurrentPath);
                _forwardHistory.Clear();
            }

            CurrentPath = normalizedPath;
            PathInputText = normalizedPath;
            LoadFolderItems(normalizedPath);
            BuildSidebarFolders(normalizedPath);
            BuildBreadcrumbs(normalizedPath);
            ApplySearchFilter();
        }
        catch
        {
            // Skip inaccessible paths in this basic version.
        }
        finally
        {
            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
            GoUpCommand.NotifyCanExecuteChanged();
        }
    }

    private void BuildQuickAccess()
    {
        QuickAccessItems.Clear();

        AddQuickAccess("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "⌂");
        AddQuickAccess("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "▣");
        AddQuickAccess("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "▤");
        AddQuickAccess("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "⇩");
        AddQuickAccess("Applications", "/Applications", "◈");
        AddQuickAccess("Macintosh HD", "/", "◉");
    }

    private void AddQuickAccess(string label, string fullPath, string glyph)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Directory.Exists(fullPath))
        {
            return;
        }

        QuickAccessItems.Add(new QuickAccessItemViewModel(label, fullPath, glyph));
    }

    private void BuildSidebarFolders(string path)
    {
        SidebarFolders.Clear();

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Take(80))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = directory;
                }

                SidebarFolders.Add(new QuickAccessItemViewModel(name, directory, "▸"));
            }
        }
        catch
        {
            // Ignore inaccessible entries in the sidebar.
        }
    }

    private void BuildBreadcrumbs(string path)
    {
        BreadcrumbItems.Clear();

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                normalizedRoot = Path.DirectorySeparatorChar.ToString();
            }

            BreadcrumbItems.Add(new BreadcrumbItemViewModel("Macintosh HD", normalizedRoot));

            var relative = path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? path[root.Length..]
                : string.Empty;

            var parts = relative
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            var cumulative = normalizedRoot;
            foreach (var part in parts)
            {
                cumulative = Path.Combine(cumulative, part);
                BreadcrumbItems.Add(new BreadcrumbItemViewModel(part, cumulative));
            }
        }
        catch
        {
            // Ignore breadcrumb build failures for malformed paths.
        }
    }

    private void LoadFolderItems(string path)
    {
        var directories = new List<FileSystemItemViewModel>();
        var files = new List<FileSystemItemViewModel>();

        try
        {
            foreach (var directoryPath in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var info = new DirectoryInfo(directoryPath);
                    directories.Add(FileSystemItemViewModel.FromDirectory(
                        info,
                        _iconProvider.GetIcon(info.FullName, isDirectory: true)));
                }
                catch
                {
                    // Ignore inaccessible entries.
                }
            }

            foreach (var filePath in Directory.EnumerateFiles(path))
            {
                try
                {
                    var info = new FileInfo(filePath);
                    files.Add(FileSystemItemViewModel.FromFile(
                        info,
                        _iconProvider.GetIcon(info.FullName, isDirectory: false)));
                }
                catch
                {
                    // Ignore inaccessible entries.
                }
            }
        }
        catch
        {
            // Ignore inaccessible folders in this basic version.
        }

        _allItems = directories
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(files.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private void ApplySearchFilter()
    {
        ExplorerItems.Clear();

        var query = SearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var item in filtered)
        {
            ExplorerItems.Add(item);
        }
    }

    private static void TryLaunchFile(string fullPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { fullPath },
                UseShellExecute = false,
            });
        }
        catch
        {
            // Ignore open failures in this basic version.
        }
    }
}
