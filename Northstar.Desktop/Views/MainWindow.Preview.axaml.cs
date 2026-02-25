using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Northstar.Desktop.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Northstar.Desktop.Views;

public partial class MainWindow
{
    private void ToggleQuickPreview()
    {
        SetQuickPreviewOpen(!_isQuickPreviewOpen);
    }

    private void SetQuickPreviewOpen(bool isOpen)
    {
        _isQuickPreviewOpen = isOpen;

        if (!isOpen)
        {
            SetPreviewPaneVisible(false);
            ClearPreviewSurface();
            return;
        }

        _ = RefreshQuickPreviewAsync();
    }

    private async Task RefreshQuickPreviewAsync()
    {
        if (!_isQuickPreviewOpen || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var requestVersion = Interlocked.Increment(ref _previewLoadVersion);
        var selectedItem = viewModel.SelectedExplorerItem;

        if (selectedItem is null ||
            !viewModel.ExplorerItems.Any(item => string.Equals(item.FullPath, selectedItem.FullPath, StringComparison.OrdinalIgnoreCase)))
        {
            SetPreviewPaneVisible(false);
            return;
        }

        var meta = $"{selectedItem.Type} • {selectedItem.Size} • {selectedItem.Modified}";
        if (selectedItem.IsDirectory)
        {
            SetPreviewPaneVisible(false);
            return;
        }

        var extension = Path.GetExtension(selectedItem.FullPath);
        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfPreview = await Task.Run(() => LoadPdfPreviewBitmap(selectedItem.FullPath));
            if (!IsCurrentPreviewRequest(requestVersion))
            {
                pdfPreview?.Dispose();
                return;
            }

            if (pdfPreview is null)
            {
                SetPreviewPaneVisible(false);
                return;
            }

            ShowImagePreview(selectedItem.Name, meta, pdfPreview);
            return;
        }

        if (PreviewImageExtensions.Contains(extension))
        {
            var imagePreview = await Task.Run(() => LoadBitmapSafe(selectedItem.FullPath));
            if (!IsCurrentPreviewRequest(requestVersion))
            {
                imagePreview?.Dispose();
                return;
            }

            if (imagePreview is null)
            {
                SetPreviewPaneVisible(false);
                return;
            }

            ShowImagePreview(selectedItem.Name, meta, imagePreview);
            return;
        }

        if (PreviewTextExtensions.Contains(extension))
        {
            var textPreview = await Task.Run(() => ReadTextPreview(selectedItem.FullPath));
            if (!IsCurrentPreviewRequest(requestVersion))
            {
                return;
            }

            if (textPreview is null)
            {
                SetPreviewPaneVisible(false);
                return;
            }

            ShowTextPreview(selectedItem.Name, meta, textPreview);
            return;
        }

        SetPreviewPaneVisible(false);
    }

    private bool IsCurrentPreviewRequest(int requestVersion)
    {
        return _isQuickPreviewOpen && requestVersion == _previewLoadVersion;
    }

    private void ShowPreviewMessage(string title, string message, string meta)
    {
        PreviewTitle.Text = title;
        PreviewMeta.Text = meta;
        PreviewMessageText.Text = message;
        PreviewMessageSurface.IsVisible = true;
        PreviewTextBox.IsVisible = false;
        PreviewImage.IsVisible = false;
        PreviewTextBox.Text = string.Empty;
        SetPreviewBitmap(null);
    }

    private void ShowTextPreview(string title, string meta, string text)
    {
        SetPreviewPaneVisible(true);
        PreviewTitle.Text = title;
        PreviewMeta.Text = meta;
        PreviewTextBox.Text = text;
        PreviewMessageSurface.IsVisible = false;
        PreviewTextBox.IsVisible = true;
        PreviewImage.IsVisible = false;
        SetPreviewBitmap(null);
    }

    private void ShowImagePreview(string title, string meta, Bitmap bitmap)
    {
        SetPreviewPaneVisible(true);
        PreviewTitle.Text = title;
        PreviewMeta.Text = meta;
        PreviewMessageSurface.IsVisible = false;
        PreviewTextBox.IsVisible = false;
        PreviewImage.IsVisible = true;
        SetPreviewBitmap(bitmap);
    }

    private void ClearPreviewSurface()
    {
        PreviewTitle.Text = "Quick Preview";
        PreviewMeta.Text = "Press Space to toggle preview.";
        PreviewMessageText.Text = "Select a file and press Space.";
        PreviewTextBox.Text = string.Empty;
        PreviewMessageSurface.IsVisible = true;
        PreviewTextBox.IsVisible = false;
        PreviewImage.IsVisible = false;
        SetPreviewBitmap(null);
    }

    private void SetPreviewPaneVisible(bool isVisible)
    {
        PreviewPane.IsVisible = isVisible;

        if (MainContentGrid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        MainContentGrid.ColumnDefinitions[2].Width = isVisible
            ? new GridLength(360)
            : new GridLength(0);
    }

    private void SetPreviewBitmap(Bitmap? bitmap)
    {
        _previewBitmap?.Dispose();
        _previewBitmap = bitmap;
        PreviewImage.Source = bitmap;
    }

    private static Bitmap? LoadBitmapSafe(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadTextPreview(string fullPath)
    {
        const int maxCharacters = 30000;

        try
        {
            using var stream = File.OpenRead(fullPath);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[maxCharacters];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, read);
            if (!reader.EndOfStream)
            {
                text += $"{Environment.NewLine}{Environment.NewLine}...preview truncated...";
            }

            return text;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadPdfPreviewBitmap(string fullPath)
    {
        string? tempDirectory = null;

        try
        {
            tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "northstar-preview",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "qlmanage",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-t", "-s", "1200", "-o", tempDirectory, fullPath },
            });

            if (process is null)
            {
                return null;
            }

            process.WaitForExit(7000);

            var previewImagePath = Directory
                .EnumerateFiles(tempDirectory, "*.png", SearchOption.AllDirectories)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(previewImagePath))
            {
                return null;
            }

            using var stream = File.OpenRead(previewImagePath);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch
                {
                    // Ignore temporary preview cleanup errors.
                }
            }
        }
    }
}
