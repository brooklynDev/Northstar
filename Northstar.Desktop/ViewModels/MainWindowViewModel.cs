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
    private bool _isSwitchingTabs;
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
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private FileSystemItemViewModel? selectedExplorerItem;

    [ObservableProperty]
    private bool showHiddenFiles;

    [ObservableProperty]
    private string defaultStartFolder = string.Empty;

    [ObservableProperty]
    private string selectedTheme = "Midnight";

    [ObservableProperty]
    private ExplorerTabViewModel? selectedTab;

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

    public ObservableCollection<ExplorerTabViewModel> Tabs { get; } = [];

    public IReadOnlyList<string> AvailableThemes { get; } =
    [
        "Midnight",
        "Graphite",
        "One Dark",
        "Emerald Night",
        "Ocean Deep",
        "Daylight",
    ];

    public bool IsPathReadMode => !IsPathEditMode;

    public string NameSortIndicator => BuildSortIndicator("Name");

    public string TypeSortIndicator => BuildSortIndicator("Type");

    public string SizeSortIndicator => BuildSortIndicator("Size");

    public string ModifiedSortIndicator => BuildSortIndicator("Modified");

    public string StatusBarText
    {
        get
        {
            var selectedCount = SelectedExplorerItems.Count;
            if (selectedCount > 1)
            {
                return $"{selectedCount} items selected";
            }

            if (selectedCount == 1)
            {
                return SelectedExplorerItems[0].Name;
            }

            if (SelectedExplorerItem is not null)
            {
                return SelectedExplorerItem.Name;
            }

            return FormatItemCount(ExplorerItems.Count);
        }
    }

    public MainWindowViewModel()
    {
        var settings = _settingsStore.Load();
        ShowHiddenFiles = settings.ShowHiddenFiles;
        DefaultStartFolder = settings.DefaultStartFolder ?? string.Empty;
        SelectedTheme = string.IsNullOrWhiteSpace(settings.ThemeName) ? "Midnight" : settings.ThemeName;

        BuildQuickAccess();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var startPath = !string.IsNullOrWhiteSpace(DefaultStartFolder) && Directory.Exists(DefaultStartFolder)
            ? DefaultStartFolder
            : home;

        var initialPath = Path.GetFullPath(startPath);
        var initialTab = new ExplorerTabViewModel(BuildTabTitle(initialPath), initialPath);
        Tabs.Add(initialTab);
        SelectedTab = initialTab;

        SelectedExplorerItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StatusBarText));
        ExplorerItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StatusBarText));

        OpenDirectory(startPath, addToHistory: false);
    }

    partial void OnSearchTextChanged(string value) => ApplySearchFilter();

    private static string FormatItemCount(int count) => count == 1 ? "1 item" : $"{count} items";

    partial void OnShowHiddenFilesChanged(bool value)
    {
        SaveSettings();
        OpenDirectory(CurrentPath, addToHistory: false);
    }

    partial void OnDefaultStartFolderChanged(string value)
    {
        SaveSettings();
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        SaveSettings();
    }

    partial void OnSelectedTabChanged(ExplorerTabViewModel? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Path))
        {
            return;
        }

        if (string.Equals(CurrentPath, value.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _isSwitchingTabs = true;
        try
        {
            _backHistory.Clear();
            _forwardHistory.Clear();
            OpenDirectory(value.Path, addToHistory: false);
        }
        finally
        {
            _isSwitchingTabs = false;
        }
    }
}
