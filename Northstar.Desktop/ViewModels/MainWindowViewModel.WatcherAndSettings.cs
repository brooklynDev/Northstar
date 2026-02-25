using Avalonia.Threading;
using Northstar.Desktop.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Northstar.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    public AppSettings GetPreferences()
    {
        return new AppSettings
        {
            ShowHiddenFiles = ShowHiddenFiles,
            DefaultStartFolder = DefaultStartFolder,
            ThemeName = SelectedTheme,
        };
    }

    public void ApplyPreferences(bool showHidden, string? defaultStartFolderValue, string? themeName)
    {
        ShowHiddenFiles = showHidden;
        DefaultStartFolder = defaultStartFolderValue?.Trim() ?? string.Empty;
        SelectedTheme = string.IsNullOrWhiteSpace(themeName) ? "Midnight" : themeName.Trim();
        SaveSettings();
        OpenDirectory(CurrentPath, addToHistory: false);
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

    private bool ShouldIncludeEntry(string path)
    {
        if (ShowHiddenFiles)
        {
            return true;
        }

        var name = Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(name) && name.StartsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.Hidden))
            {
                return false;
            }
        }
        catch
        {
            // If attributes cannot be read, keep the entry visible.
        }

        return true;
    }

    private void SaveSettings()
    {
        _settingsStore.Save(new AppSettings
        {
            ShowHiddenFiles = ShowHiddenFiles,
            DefaultStartFolder = DefaultStartFolder,
            ThemeName = SelectedTheme,
        });
    }

}
