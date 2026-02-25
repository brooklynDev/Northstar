using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace Northstar.Desktop.Views;

public partial class MainWindow
{
    private const double MinimumExplorerColumnWidth = 90;

    public static readonly StyledProperty<GridLength> NameColumnWidthProperty =
        AvaloniaProperty.Register<MainWindow, GridLength>(
            nameof(NameColumnWidth),
            new GridLength(3.2, GridUnitType.Star));

    public static readonly StyledProperty<GridLength> TypeColumnWidthProperty =
        AvaloniaProperty.Register<MainWindow, GridLength>(
            nameof(TypeColumnWidth),
            new GridLength(1.4, GridUnitType.Star));

    public static readonly StyledProperty<GridLength> SizeColumnWidthProperty =
        AvaloniaProperty.Register<MainWindow, GridLength>(
            nameof(SizeColumnWidth),
            new GridLength(1.1, GridUnitType.Star));

    public static readonly StyledProperty<GridLength> ModifiedColumnWidthProperty =
        AvaloniaProperty.Register<MainWindow, GridLength>(
            nameof(ModifiedColumnWidth),
            new GridLength(1.8, GridUnitType.Star));

    private bool _explorerColumnWidthsInitialized;
    private int _activeResizeBoundaryIndex = -1;
    private IPointer? _activeResizePointer;
    private double _resizeStartX;
    private double _leftStartWidth;
    private double _rightStartWidth;

    public GridLength NameColumnWidth
    {
        get => GetValue(NameColumnWidthProperty);
        set => SetValue(NameColumnWidthProperty, value);
    }

    public GridLength TypeColumnWidth
    {
        get => GetValue(TypeColumnWidthProperty);
        set => SetValue(TypeColumnWidthProperty, value);
    }

    public GridLength SizeColumnWidth
    {
        get => GetValue(SizeColumnWidthProperty);
        set => SetValue(SizeColumnWidthProperty, value);
    }

    public GridLength ModifiedColumnWidth
    {
        get => GetValue(ModifiedColumnWidthProperty);
        set => SetValue(ModifiedColumnWidthProperty, value);
    }

    private void EnsureExplorerColumnWidthsInitialized()
    {
        if (_explorerColumnWidthsInitialized)
        {
            return;
        }

        var totalWidth = ExplorerHeaderGrid.Bounds.Width;
        if (totalWidth <= 0)
        {
            return;
        }

        var weightTotal = 3.2 + 1.4 + 1.1 + 1.8;
        SetColumnWidth(0, totalWidth * (3.2 / weightTotal));
        SetColumnWidth(1, totalWidth * (1.4 / weightTotal));
        SetColumnWidth(2, totalWidth * (1.1 / weightTotal));
        SetColumnWidth(3, totalWidth * (1.8 / weightTotal));

        _explorerColumnWidthsInitialized = true;
    }

    private void ColumnResizeHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle)
        {
            return;
        }

        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        EnsureExplorerColumnWidthsInitialized();

        if (handle.Tag is not string value || !int.TryParse(value, out var boundaryIndex))
        {
            return;
        }

        _activeResizeBoundaryIndex = boundaryIndex;
        _activeResizePointer = e.Pointer;
        _resizeStartX = e.GetPosition(ExplorerHeaderGrid).X;
        _leftStartWidth = GetColumnWidth(boundaryIndex);
        _rightStartWidth = GetColumnWidth(boundaryIndex + 1);

        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void ColumnResizeHandle_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_activeResizeBoundaryIndex < 0 || _activeResizePointer is null || !ReferenceEquals(e.Pointer, _activeResizePointer))
        {
            return;
        }

        var delta = e.GetPosition(ExplorerHeaderGrid).X - _resizeStartX;
        var adjustedDelta = delta;

        if (_leftStartWidth + adjustedDelta < MinimumExplorerColumnWidth)
        {
            adjustedDelta = MinimumExplorerColumnWidth - _leftStartWidth;
        }

        if (_rightStartWidth - adjustedDelta < MinimumExplorerColumnWidth)
        {
            adjustedDelta = _rightStartWidth - MinimumExplorerColumnWidth;
        }

        SetColumnWidth(_activeResizeBoundaryIndex, _leftStartWidth + adjustedDelta);
        SetColumnWidth(_activeResizeBoundaryIndex + 1, _rightStartWidth - adjustedDelta);

        _explorerColumnWidthsInitialized = true;
        e.Handled = true;
    }

    private void ColumnResizeHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_activeResizePointer is not null && ReferenceEquals(e.Pointer, _activeResizePointer))
        {
            e.Pointer.Capture(null);
        }

        EndColumnResize();
        e.Handled = true;
    }

    private void ColumnResizeHandle_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndColumnResize();
    }

    private void EndColumnResize()
    {
        _activeResizeBoundaryIndex = -1;
        _activeResizePointer = null;
    }

    private double GetColumnWidth(int index)
    {
        var width = index switch
        {
            0 => NameColumnWidth,
            1 => TypeColumnWidth,
            2 => SizeColumnWidth,
            3 => ModifiedColumnWidth,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        if (width.IsAbsolute)
        {
            return width.Value;
        }

        if (index >= 0 && index < ExplorerHeaderGrid.ColumnDefinitions.Count)
        {
            var actualWidth = ExplorerHeaderGrid.ColumnDefinitions[index].ActualWidth;
            if (actualWidth > 0)
            {
                return actualWidth;
            }
        }

        return MinimumExplorerColumnWidth;
    }

    private void SetColumnWidth(int index, double width)
    {
        var pixelWidth = new GridLength(Math.Max(MinimumExplorerColumnWidth, width), GridUnitType.Pixel);

        switch (index)
        {
            case 0:
                NameColumnWidth = pixelWidth;
                break;
            case 1:
                TypeColumnWidth = pixelWidth;
                break;
            case 2:
                SizeColumnWidth = pixelWidth;
                break;
            case 3:
                ModifiedColumnWidth = pixelWidth;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
