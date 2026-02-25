using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Northstar.Desktop.Views;

public partial class MainWindow
{
    private ScrollViewer? _explorerListScrollViewer;

    private void AttachExplorerHorizontalScrollSync()
    {
        if (_explorerListScrollViewer is not null)
        {
            return;
        }

        var scrollViewer = ExplorerList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            Dispatcher.UIThread.Post(AttachExplorerHorizontalScrollSync, DispatcherPriority.Loaded);
            return;
        }

        _explorerListScrollViewer = scrollViewer;
        _explorerListScrollViewer.ScrollChanged += ExplorerListScrollViewer_OnScrollChanged;
        UpdateExplorerHeaderHorizontalOffset(_explorerListScrollViewer.Offset.X);
    }

    private void DetachExplorerHorizontalScrollSync()
    {
        if (_explorerListScrollViewer is null)
        {
            return;
        }

        _explorerListScrollViewer.ScrollChanged -= ExplorerListScrollViewer_OnScrollChanged;
        _explorerListScrollViewer = null;
        UpdateExplorerHeaderHorizontalOffset(0);
    }

    private void ExplorerListScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateExplorerHeaderHorizontalOffset(_explorerListScrollViewer?.Offset.X ?? 0);
    }

    private void UpdateExplorerHeaderHorizontalOffset(double offsetX)
    {
        ExplorerHeaderGrid.RenderTransform = Math.Abs(offsetX) < 0.5
            ? null
            : new TranslateTransform(-offsetX, 0);
    }
}
