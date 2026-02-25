using System;
using System.IO;
using Avalonia.Media;

namespace Northstar.Desktop.ViewModels;

public sealed class FileSystemItemViewModel
{
    private FileSystemItemViewModel(
        string name,
        string fullPath,
        bool isDirectory,
        string type,
        string size,
        DateTimeOffset modifiedUtc,
        IImage? icon,
        string iconGlyph)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Type = type;
        Size = size;
        Modified = modifiedUtc.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        Icon = icon;
        IconGlyph = iconGlyph;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public string Type { get; }

    public string Size { get; }

    public string Modified { get; }

    public IImage? Icon { get; }

    public string IconGlyph { get; }

    public static FileSystemItemViewModel FromDirectory(DirectoryInfo info, IImage? icon)
    {
        return new FileSystemItemViewModel(
            name: info.Name,
            fullPath: info.FullName,
            isDirectory: true,
            type: "Folder",
            size: "--",
            modifiedUtc: info.LastWriteTimeUtc,
            icon: icon,
            iconGlyph: "📁");
    }

    public static FileSystemItemViewModel FromFile(FileInfo info, IImage? icon)
    {
        return new FileSystemItemViewModel(
            name: info.Name,
            fullPath: info.FullName,
            isDirectory: false,
            type: BuildTypeLabel(info.Extension),
            size: FormatSize(info.Length),
            modifiedUtc: info.LastWriteTimeUtc,
            icon: icon,
            iconGlyph: BuildFallbackGlyph(info.Extension));
    }

    private static string BuildTypeLabel(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "File";
        }

        return $"{extension.TrimStart('.').ToUpperInvariant()} File";
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var suffixIndex = 0;

        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return $"{value:0.#} {suffixes[suffixIndex]}";
    }

    private static string BuildFallbackGlyph(string extension)
    {
        var ext = extension.Trim().ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".heic" => "🖼️",
            ".mp4" or ".mov" or ".mkv" or ".avi" => "🎬",
            ".mp3" or ".wav" or ".m4a" or ".flac" => "🎵",
            ".zip" or ".tar" or ".gz" or ".7z" => "🗜️",
            ".pdf" => "📕",
            ".doc" or ".docx" or ".pages" => "📘",
            ".xls" or ".xlsx" or ".numbers" => "📗",
            ".ppt" or ".pptx" or ".key" => "📙",
            ".cs" or ".js" or ".ts" or ".tsx" or ".jsx" or ".json" or ".xml" or ".yml" or ".yaml" or ".md" => "⌘",
            _ => "📄",
        };
    }
}
