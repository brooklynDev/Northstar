using System;
using System.IO;

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
        string glyph)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Type = type;
        Size = size;
        Modified = modifiedUtc.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        Glyph = glyph;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public string Type { get; }

    public string Size { get; }

    public string Modified { get; }

    public string Glyph { get; }

    public static FileSystemItemViewModel FromDirectory(DirectoryInfo info)
    {
        return new FileSystemItemViewModel(
            name: info.Name,
            fullPath: info.FullName,
            isDirectory: true,
            type: "Folder",
            size: "--",
            modifiedUtc: info.LastWriteTimeUtc,
            glyph: "▣");
    }

    public static FileSystemItemViewModel FromFile(FileInfo info)
    {
        return new FileSystemItemViewModel(
            name: info.Name,
            fullPath: info.FullName,
            isDirectory: false,
            type: BuildTypeLabel(info.Extension),
            size: FormatSize(info.Length),
            modifiedUtc: info.LastWriteTimeUtc,
            glyph: "•");
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
}
