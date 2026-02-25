using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Northstar.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    private readonly StringBuilder _ansiPendingBuffer = new();
    private readonly SemaphoreSlim _terminalStartGate = new(1, 1);
    private Process? _embeddedTerminalProcess;
    private CancellationTokenSource? _terminalReadCts;
    private readonly List<string> _terminalCompletionMatches = [];
    private string _terminalCompletionContextKey = string.Empty;
    private int _terminalCompletionIndex = -1;
    private int _terminalOutputCharCount;
    private int _terminalCurrentAnsiColor = -1;
    private bool _terminalAnsiBold;
    private bool _capturingTerminalPathMarker;
    private readonly StringBuilder _terminalPathMarkerBuffer = new();

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
            ShutdownEmbeddedTerminal();
        };

        AddHandler(KeyDownEvent, Window_OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, Window_OnPreviewTextInput, RoutingStrategies.Tunnel);
        SetEmbeddedTerminalVisible(false);
    }
}
