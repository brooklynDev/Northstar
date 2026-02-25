using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Northstar.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Threading;

namespace Northstar.Desktop.Views;

public partial class MainWindow : Window
{
    private const int TypeAheadResetMilliseconds = 1200;
    private const char TerminalPathMarkerStart = '\u001e';
    private const char TerminalPathMarkerEnd = '\u001f';

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
    private int? _selectionAnchorIndex;
    private int? _selectionRangeEndIndex;
    private bool _isApplyingRangeSelection;

    private readonly SemaphoreSlim _terminalStartGate = new(1, 1);
    private readonly Dictionary<ExplorerTabViewModel, TerminalTabSession> _terminalSessions = new();
    private TerminalTabSession? _activeTerminalSession;
    private MainWindowViewModel? _subscribedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        SetPreviewPaneVisible(false);

        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                ApplyColorTheme(viewModel.SelectedTheme);
                EnsureViewModelSubscriptions(viewModel);
            }

            ExplorerList.Focus();
        };
        Closing += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.Shutdown();
            }

            _previewBitmap?.Dispose();
            _previewBitmap = null;
            ShutdownAllEmbeddedTerminals();
            DetachViewModelSubscriptions();
        };

        AddHandler(KeyDownEvent, Window_OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, Window_OnPreviewTextInput, RoutingStrategies.Tunnel);
        SetEmbeddedTerminalVisible(false);

        if (DataContext is MainWindowViewModel viewModel)
        {
            ApplyColorTheme(viewModel.SelectedTheme);
            EnsureViewModelSubscriptions(viewModel);
        }

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                EnsureViewModelSubscriptions(viewModel);
                ApplyColorTheme(viewModel.SelectedTheme);
                SwitchTerminalSessionForSelectedTab();
            }
            else
            {
                DetachViewModelSubscriptions();
            }
        };
    }

    private void EnsureViewModelSubscriptions(MainWindowViewModel viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        DetachViewModelSubscriptions();
        _subscribedViewModel = viewModel;
        _subscribedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        _subscribedViewModel.Tabs.CollectionChanged += Tabs_OnCollectionChanged;
    }

    private void DetachViewModelSubscriptions()
    {
        if (_subscribedViewModel is null)
        {
            return;
        }

        _subscribedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _subscribedViewModel.Tabs.CollectionChanged -= Tabs_OnCollectionChanged;
        _subscribedViewModel = null;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab))
        {
            SwitchTerminalSessionForSelectedTab();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTheme))
        {
            ApplyColorTheme(viewModel.SelectedTheme);
        }
    }

    private void Tabs_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null)
        {
            return;
        }

        foreach (var removed in e.OldItems)
        {
            if (removed is not ExplorerTabViewModel removedTab)
            {
                continue;
            }

            if (_terminalSessions.TryGetValue(removedTab, out var session))
            {
                ShutdownEmbeddedTerminal(session);
                _terminalSessions.Remove(removedTab);
            }
        }
    }
}
