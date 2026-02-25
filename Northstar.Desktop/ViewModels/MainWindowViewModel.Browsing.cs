using Northstar.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Northstar.Desktop.ViewModels;

public partial class MainWindowViewModel
{
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
                if (!ShouldIncludeEntry(directory))
                {
                    continue;
                }

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
                    if (!ShouldIncludeEntry(directoryPath))
                    {
                        continue;
                    }

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
                    if (!ShouldIncludeEntry(filePath))
                    {
                        continue;
                    }

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

}
