using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Northstar.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private static void CopyEntry(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
        {
            CopyDirectoryRecursive(sourcePath, destinationPath);
            return;
        }

        if (File.Exists(sourcePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
            File.Copy(sourcePath, destinationPath, overwrite: false);
        }
    }

    private static void MoveEntry(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        if (File.Exists(sourcePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
            File.Move(sourcePath, destinationPath);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var destinationSubdirectory = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            CopyDirectoryRecursive(directory, destinationSubdirectory);
        }
    }

    private static string GetUniqueDestinationPath(string targetPath)
    {
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return targetPath;
        }

        var directory = Path.GetDirectoryName(targetPath) ?? ".";
        var originalName = Path.GetFileName(targetPath);
        var extension = Path.GetExtension(originalName);
        var baseName = Path.GetFileNameWithoutExtension(originalName);

        for (var i = 1; i < 1000; i++)
        {
            var candidateName = string.IsNullOrWhiteSpace(extension)
                ? $"{baseName} - Copy{(i == 1 ? string.Empty : $" {i}")}"
                : $"{baseName} - Copy{(i == 1 ? string.Empty : $" {i}")}{extension}";
            var candidatePath = Path.Combine(directory, candidateName);

            if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return targetPath;
    }

    private static bool EntryExists(string path)
        => File.Exists(path) || Directory.Exists(path);

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void DeleteEntry(string fullPath)
    {
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
            return;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private static void MoveToTrash(string fullPath)
    {
        if (!OperatingSystem.IsMacOS())
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return;
        }

        var escapedPath = EscapeAppleScriptString(fullPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            ArgumentList =
            {
                "-e",
                $"tell application \"Finder\" to delete POSIX file \"{escapedPath}\"",
            },
        })?.WaitForExit(2000);
    }

    private static string EscapeAppleScriptString(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        foreach (var ch in value)
        {
            if (ch is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
