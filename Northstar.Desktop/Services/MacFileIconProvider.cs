using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Northstar.Desktop.Services;

public sealed class MacFileIconProvider
{
    private readonly Dictionary<string, IImage?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _cacheDirectory;
    private readonly string _probeDirectory;
    private bool _quickLookAvailable = true;

    public MacFileIconProvider()
    {
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "northstar-icons-cache");
        _probeDirectory = Path.Combine(_cacheDirectory, "probes");
        Directory.CreateDirectory(_cacheDirectory);
        Directory.CreateDirectory(_probeDirectory);
    }

    public IImage? GetIcon(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            // qlmanage can hang on folder probes; keep folder icons on the fallback path.
            return null;
        }

        var key = isDirectory
            ? "__folder"
            : string.IsNullOrWhiteSpace(Path.GetExtension(path)) ? "__file" : Path.GetExtension(path);

        if (_cache.TryGetValue(key, out var icon))
        {
            return icon;
        }

        icon = OperatingSystem.IsMacOS() && _quickLookAvailable
            ? LoadIconViaQuickLook(path, isDirectory, key)
            : null;
        _cache[key] = icon;
        return icon;
    }

    private IImage? LoadIconViaQuickLook(string sourcePath, bool isDirectory, string key)
    {
        try
        {
            var probePath = GetProbePath(sourcePath, isDirectory, key);
            var outputFileName = $"{Path.GetFileName(probePath)}.png";
            var outputPath = Path.Combine(_cacheDirectory, outputFileName);

            if (!File.Exists(outputPath))
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "qlmanage",
                    UseShellExecute = false,
                    ArgumentList =
                    {
                        "-t",
                        "-s",
                        "32",
                        "-o",
                        _cacheDirectory,
                        probePath,
                    },
                });

                if (process is null)
                {
                    return null;
                }

                process.WaitForExit(1200);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    _quickLookAvailable = false;
                    return null;
                }

                if (process.ExitCode != 0)
                {
                    _quickLookAvailable = false;
                    return null;
                }
            }

            if (!File.Exists(outputPath))
            {
                return null;
            }

            using var iconStream = File.OpenRead(outputPath);
            return new Bitmap(iconStream);
        }
        catch
        {
            _quickLookAvailable = false;
            return null;
        }
    }

    private string GetProbePath(string sourcePath, bool isDirectory, string key)
    {
        if (isDirectory)
        {
            var folderProbe = Path.Combine(_probeDirectory, "folder-probe");
            Directory.CreateDirectory(folderProbe);
            return folderProbe;
        }

        var extension = Path.GetExtension(sourcePath);
        var probeName = string.IsNullOrWhiteSpace(extension)
            ? "file-probe"
            : $"file-probe{extension}";
        var fileProbe = Path.Combine(_probeDirectory, probeName);

        if (!File.Exists(fileProbe))
        {
            File.WriteAllText(fileProbe, $"northstar icon probe: {key}");
        }

        return fileProbe;
    }
}
