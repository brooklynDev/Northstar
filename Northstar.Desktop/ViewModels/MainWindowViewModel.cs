using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Northstar.Desktop.Services;

namespace Northstar.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly MacFileIconProvider _iconProvider = new();
    private readonly List<string> _clipboardPaths = [];
    private readonly object _watcherSync = new();
    private FileSystemWatcher? _directoryWatcher;
    private CancellationTokenSource? _watcherRefreshDebounceCts;
    private bool _clipboardIsCut;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortIndicator))]
    [NotifyPropertyChangedFor(nameof(TypeSortIndicator))]
    [NotifyPropertyChangedFor(nameof(SizeSortIndicator))]
    [NotifyPropertyChangedFor(nameof(ModifiedSortIndicator))]
    private string sortColumn = "Name";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortIndicator))]
    [NotifyPropertyChangedFor(nameof(TypeSortIndicator))]
    [NotifyPropertyChangedFor(nameof(SizeSortIndicator))]
    [NotifyPropertyChangedFor(nameof(ModifiedSortIndicator))]
    private bool sortAscending = true;

    public ObservableCollection<QuickAccessItemViewModel> QuickAccessItems { get; } = [];

    public ObservableCollection<QuickAccessItemViewModel> SidebarFolders { get; } = [];

    public ObservableCollection<BreadcrumbItemViewModel> BreadcrumbItems { get; } = [];

    public ObservableCollection<FileSystemItemViewModel> ExplorerItems { get; } = [];

    public bool IsPathReadMode => !IsPathEditMode;

    public string NameSortIndicator => BuildSortIndicator("Name");

    public string TypeSortIndicator => BuildSortIndicator("Type");

    public string SizeSortIndicator => BuildSortIndicator("Size");

    public string ModifiedSortIndicator => BuildSortIndicator("Modified");

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
        var target = item ?? SelectedExplorerItem;
        if (target is null)
        {
            return;
        }

        _clipboardPaths.Clear();
        _clipboardPaths.Add(target.FullPath);
        _clipboardIsCut = false;
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void CutItem(FileSystemItemViewModel? item)
    {
        var target = item ?? SelectedExplorerItem;
        if (target is null)
        {
            return;
        }

        _clipboardPaths.Clear();
        _clipboardPaths.Add(target.FullPath);
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
        var target = item ?? SelectedExplorerItem;
        if (target is null)
        {
            return;
        }

        try
        {
            MoveToTrash(target.FullPath);
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
        if (!Directory.Exists(CurrentPath))
        {
            return;
        }

        try
        {
            var folderPath = GetUniqueDestinationPath(Path.Combine(CurrentPath, "New Folder"));
            Directory.CreateDirectory(folderPath);
            OpenDirectory(CurrentPath, addToHistory: false);
        }
        catch
        {
            // Ignore new-folder errors in this basic version.
        }
    }

    [RelayCommand]
    private void CopyPath(FileSystemItemViewModel? item)
    {
        var target = item ?? SelectedExplorerItem;
        if (target is null)
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

            process.StandardInput.Write(target.FullPath);
            process.StandardInput.Close();
            process.WaitForExit(1000);
        }
        catch
        {
            // Ignore clipboard errors in this basic version.
        }
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
            ConfigureDirectoryWatcher(normalizedPath);
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

        foreach (var item in SortItems(filtered))
        {
            ExplorerItems.Add(item);
        }
    }

    private IEnumerable<FileSystemItemViewModel> SortItems(IEnumerable<FileSystemItemViewModel> source)
    {
        var ordered = source.OrderBy(item => !item.IsDirectory);

        ordered = SortColumn switch
        {
            "Type" => SortAscending
                ? ordered.ThenBy(item => item.Type, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(item => item.Type, StringComparer.OrdinalIgnoreCase),
            "Size" => SortAscending
                ? ordered.ThenBy(item => item.SortSize)
                : ordered.ThenByDescending(item => item.SortSize),
            "Modified" => SortAscending
                ? ordered.ThenBy(item => item.SortModifiedUtc)
                : ordered.ThenByDescending(item => item.SortModifiedUtc),
            _ => SortAscending
                ? ordered.ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
    }

    private string BuildSortIndicator(string column)
    {
        if (!string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return SortAscending ? "▲" : "▼";
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

    private static IEnumerable<string> BuildTerminalCandidates(string? preferredProgram)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredProgram))
        {
            if (preferredProgram.Contains("iTerm", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("iTerm");
            }
            else if (preferredProgram.Contains("Warp", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("Warp");
            }
            else if (preferredProgram.Contains("WezTerm", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("WezTerm");
            }
            else if (preferredProgram.Contains("Apple_Terminal", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("Terminal");
            }
        }

        candidates.Add("Terminal");
        candidates.Add("iTerm");
        candidates.Add("Warp");
        candidates.Add("WezTerm");

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public void Shutdown()
    {
        lock (_watcherSync)
        {
            _watcherRefreshDebounceCts?.Cancel();
            _watcherRefreshDebounceCts?.Dispose();
            _watcherRefreshDebounceCts = null;

            if (_directoryWatcher is not null)
            {
                _directoryWatcher.EnableRaisingEvents = false;
                _directoryWatcher.Created -= DirectoryWatcher_OnChanged;
                _directoryWatcher.Changed -= DirectoryWatcher_OnChanged;
                _directoryWatcher.Deleted -= DirectoryWatcher_OnChanged;
                _directoryWatcher.Renamed -= DirectoryWatcher_OnRenamed;
                _directoryWatcher.Error -= DirectoryWatcher_OnError;
                _directoryWatcher.Dispose();
                _directoryWatcher = null;
            }
        }
    }

    private void ConfigureDirectoryWatcher(string path)
    {
        lock (_watcherSync)
        {
            if (_directoryWatcher is not null)
            {
                _directoryWatcher.EnableRaisingEvents = false;
                _directoryWatcher.Created -= DirectoryWatcher_OnChanged;
                _directoryWatcher.Changed -= DirectoryWatcher_OnChanged;
                _directoryWatcher.Deleted -= DirectoryWatcher_OnChanged;
                _directoryWatcher.Renamed -= DirectoryWatcher_OnRenamed;
                _directoryWatcher.Error -= DirectoryWatcher_OnError;
                _directoryWatcher.Dispose();
                _directoryWatcher = null;
            }

            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = false,
                };

                watcher.Created += DirectoryWatcher_OnChanged;
                watcher.Changed += DirectoryWatcher_OnChanged;
                watcher.Deleted += DirectoryWatcher_OnChanged;
                watcher.Renamed += DirectoryWatcher_OnRenamed;
                watcher.Error += DirectoryWatcher_OnError;
                watcher.EnableRaisingEvents = true;

                _directoryWatcher = watcher;
            }
            catch
            {
                // Ignore watcher setup errors for unsupported/inaccessible paths.
            }
        }
    }

    private void DirectoryWatcher_OnChanged(object sender, FileSystemEventArgs e)
    {
        QueueDebouncedRefresh();
    }

    private void DirectoryWatcher_OnRenamed(object sender, RenamedEventArgs e)
    {
        QueueDebouncedRefresh();
    }

    private void DirectoryWatcher_OnError(object sender, ErrorEventArgs e)
    {
        QueueDebouncedRefresh();
    }

    private void QueueDebouncedRefresh()
    {
        CancellationTokenSource debounceCts;

        lock (_watcherSync)
        {
            _watcherRefreshDebounceCts?.Cancel();
            _watcherRefreshDebounceCts?.Dispose();
            _watcherRefreshDebounceCts = new CancellationTokenSource();
            debounceCts = _watcherRefreshDebounceCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(220, debounceCts.Token);
                await Dispatcher.UIThread.InvokeAsync(() => OpenDirectory(CurrentPath, addToHistory: false));
            }
            catch (OperationCanceledException)
            {
                // Expected when newer filesystem events arrive quickly.
            }
            catch
            {
                // Ignore watcher refresh errors in this basic version.
            }
        });
    }

    private static void CopyEntry(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
        {
            CopyDirectoryRecursive(sourcePath, destinationPath);
            return;
        }

        if (File.Exists(sourcePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
            File.Copy(sourcePath, destinationPath, overwrite: false);
        }
    }

    private static void MoveEntry(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        if (File.Exists(sourcePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
            File.Move(sourcePath, destinationPath);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var destinationSubdirectory = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            CopyDirectoryRecursive(directory, destinationSubdirectory);
        }
    }

    private static string GetUniqueDestinationPath(string targetPath)
    {
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return targetPath;
        }

        var directory = Path.GetDirectoryName(targetPath) ?? ".";
        var originalName = Path.GetFileName(targetPath);
        var extension = Path.GetExtension(originalName);
        var baseName = Path.GetFileNameWithoutExtension(originalName);

        for (var i = 1; i < 1000; i++)
        {
            var candidateName = string.IsNullOrWhiteSpace(extension)
                ? $"{baseName} - Copy{(i == 1 ? string.Empty : $" {i}")}"
                : $"{baseName} - Copy{(i == 1 ? string.Empty : $" {i}")}{extension}";
            var candidatePath = Path.Combine(directory, candidateName);

            if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return targetPath;
    }

    private static void MoveToTrash(string fullPath)
    {
        if (!OperatingSystem.IsMacOS())
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return;
        }

        var escapedPath = EscapeAppleScriptString(fullPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            ArgumentList =
            {
                "-e",
                $"tell application \"Finder\" to delete POSIX file \"{escapedPath}\"",
            },
        })?.WaitForExit(2000);
    }

    private static string EscapeAppleScriptString(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        foreach (var ch in value)
        {
            if (ch is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
