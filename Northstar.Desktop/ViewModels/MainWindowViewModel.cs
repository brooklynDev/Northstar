using CommunityToolkit.Mvvm.ComponentModel;
using Northstar.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;

namespace Northstar.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly MacFileIconProvider _iconProvider = new();
    private readonly AppSettingsStore _settingsStore = new();
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
    private bool showHiddenFiles;

    [ObservableProperty]
    private string defaultStartFolder = string.Empty;

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

    public ObservableCollection<FileSystemItemViewModel> SelectedExplorerItems { get; } = [];

    public bool IsPathReadMode => !IsPathEditMode;

    public string NameSortIndicator => BuildSortIndicator("Name");

    public string TypeSortIndicator => BuildSortIndicator("Type");

    public string SizeSortIndicator => BuildSortIndicator("Size");

    public string ModifiedSortIndicator => BuildSortIndicator("Modified");

    public MainWindowViewModel()
    {
        var settings = _settingsStore.Load();
        ShowHiddenFiles = settings.ShowHiddenFiles;
        DefaultStartFolder = settings.DefaultStartFolder ?? string.Empty;

        BuildQuickAccess();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var startPath = !string.IsNullOrWhiteSpace(DefaultStartFolder) && Directory.Exists(DefaultStartFolder)
            ? DefaultStartFolder
            : home;
        OpenDirectory(startPath, addToHistory: false);
    }

    partial void OnSearchTextChanged(string value) => ApplySearchFilter();

    partial void OnShowHiddenFilesChanged(bool value)
    {
        SaveSettings();
        OpenDirectory(CurrentPath, addToHistory: false);
    }

    partial void OnDefaultStartFolderChanged(string value)
    {
        SaveSettings();
    }
}
